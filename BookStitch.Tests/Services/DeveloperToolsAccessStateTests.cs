using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DeveloperToolsAccessStateTests
{
    [Theory]
    [InlineData(BookStitchEnvironment.Production)]
    [InlineData(BookStitchEnvironment.Development)]
    [InlineData(BookStitchEnvironment.XTester)]
    public void Legacy_visible_access_survives_hiding_wrenches(BookStitchEnvironment environment)
    {
        var settings = new AppSettings { DeveloperToolsVisible = true };
        var access = new DeveloperToolsAccessState();
        access.Initialize(settings, environment, () => true);
        access.VisibilityChoice = DeveloperToolsVisibilityChoice.Hide;
        Assert.True(access.IsEnabled);
        Assert.False(access.IsVisible);
        Assert.Equal(DeveloperToolsVisibilityChoice.Hide, access.VisibilityChoice);
    }

    [Fact]
    public void Hide_keeps_access_after_reload_while_conceal_turns_it_off()
    {
        var settings = new AppSettings { DeveloperToolsSelectorUnlocked = true };
        var state = new DeveloperToolsAccessState();
        state.Initialize(settings, BookStitchEnvironment.Production, () => true);


        state.VisibilityChoice = DeveloperToolsVisibilityChoice.Show;
        state.VisibilityChoice = DeveloperToolsVisibilityChoice.Hide;

        Assert.True(state.IsEnabled);
        Assert.False(state.IsVisible);
        var reloaded = new DeveloperToolsAccessState();
        reloaded.Initialize(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!, BookStitchEnvironment.Production, () => true);
        Assert.True(reloaded.IsEnabled);
        reloaded.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
        Assert.False(reloaded.IsEnabled);
        Assert.False(reloaded.IsSelectorAvailable);
    }

    [Fact]
    public void Conceal_persists_locked_state_and_requires_ten_new_clicks_after_reload()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "settings.json");
        var settings = new AppSettings();
        var state = new DeveloperToolsAccessState();
        state.Initialize(settings, BookStitchEnvironment.Production, () => { new AtomicJsonStore().Save(path, settings, null); return true; });
        for (var i = 0; i < 10; i++) state.RegisterDiscoveryClick();
        state.VisibilityChoice = DeveloperToolsVisibilityChoice.Show;
        state.VisibilityChoice = DeveloperToolsVisibilityChoice.Hide;
        Assert.True(state.IsEnabled);
        Assert.False(state.IsVisible);
        Assert.True(state.IsSelectorAvailable);
        state.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
        Assert.False(state.IsVisible);
        Assert.False(state.IsSelectorAvailable);
        Assert.Equal(DeveloperToolsVisibilityChoice.Hide, state.VisibilityChoice);
        var persisted = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path))!;
        state.Initialize(persisted, BookStitchEnvironment.Production, () => true);
        Assert.False(state.IsSelectorAvailable);
        for (var i = 0; i < 9; i++) state.RegisterDiscoveryClick();
        Assert.False(state.IsSelectorAvailable);
        state.RegisterDiscoveryClick();
        Assert.True(state.IsSelectorAvailable);
        Assert.True(state.IsEnabled);
        Assert.False(state.IsVisible);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_conceal_restores_both_preferences(bool throws)
    {
        var settings = new AppSettings { DeveloperToolsVisible = true, DeveloperToolsSelectorUnlocked = true };
        var state = new DeveloperToolsAccessState();
        state.Initialize(settings, BookStitchEnvironment.Production, () => throws ? throw new IOException("injected") : false);
        state.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
        Assert.True(state.IsVisible);
        Assert.True(state.IsSelectorAvailable);
        Assert.True(settings.DeveloperToolsSelectorUnlocked);
        Assert.Equal(DeveloperToolsVisibilityChoice.Show, state.VisibilityChoice);
    }

    [Theory]
    [InlineData(BookStitchEnvironment.Development)]
    [InlineData(BookStitchEnvironment.XTester)]
    public void Conceal_explicitly_disables_development_environment_access(BookStitchEnvironment environment)
    {
        var state = new DeveloperToolsAccessState();
        state.Initialize(new(), environment, () => true);
        Assert.True(state.CanConceal);
        state.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
        Assert.False(state.IsVisible);
        Assert.False(state.IsSelectorAvailable);
    }

    [Theory]
    [InlineData(BookStitchEnvironment.Production, false)]
    [InlineData(BookStitchEnvironment.Development, true)]
    [InlineData(BookStitchEnvironment.XTester, true)]
    public void Missing_preference_uses_environment_default(BookStitchEnvironment environment, bool visible)
    {
        var state = new DeveloperToolsAccessState();
        state.Initialize(new AppSettings(), environment, () => true);
        Assert.Equal(visible, state.IsVisible);
        Assert.Equal(visible, state.IsSelectorAvailable);
    }

    [Fact]
    public void Ten_clicks_enable_access_with_hidden_wrenches_and_further_clicks_do_not_save_again()
    {
        var settings = new AppSettings();
        var saves = 0;
        var state = new DeveloperToolsAccessState();
        state.Initialize(settings, BookStitchEnvironment.Production, () => { saves++; return true; });
        for (var click = 0; click < 9; click++) state.RegisterDiscoveryClick();
        Assert.False(state.IsSelectorAvailable);
        Assert.False(state.IsVisible);
        state.RegisterDiscoveryClick();
        Assert.True(state.IsSelectorAvailable);
        Assert.True(state.IsEnabled);
        Assert.False(state.IsVisible);
        for (var click = 0; click < 20; click++) state.RegisterDiscoveryClick();
        Assert.Equal(1, saves);
    }

    [Theory]
    [InlineData(BookStitchEnvironment.Production, true)]
    [InlineData(BookStitchEnvironment.Production, false)]
    [InlineData(BookStitchEnvironment.Development, true)]
    [InlineData(BookStitchEnvironment.Development, false)]
    [InlineData(BookStitchEnvironment.XTester, true)]
    [InlineData(BookStitchEnvironment.XTester, false)]
    public void Selected_visibility_and_discovery_survive_atomic_settings_reload(BookStitchEnvironment environment, bool visible)
    {
        using var folder = new TemporaryFolder();
        var store = new AtomicJsonStore();
        var file = Path.Combine(folder.Path, "settings.json");
        var settings = new AppSettings();
        var state = new DeveloperToolsAccessState();
        state.Initialize(settings, environment, () => { store.Save(file, settings, null); return true; });
        if (environment == BookStitchEnvironment.Production)
            for (var i = 0; i < 10; i++) state.RegisterDiscoveryClick();
        state.IsVisible = !state.IsVisible;
        state.IsVisible = visible;
        var reloaded = new DeveloperToolsAccessState();
        reloaded.Initialize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file))!, environment, () => true);
        Assert.Equal(visible, reloaded.IsVisible);
        Assert.True(reloaded.IsSelectorAvailable);
    }

    [Fact]
    public void Failed_save_keeps_previous_visibility_and_locked_selector()
    {
        var settings = new AppSettings();
        var state = new DeveloperToolsAccessState();
        state.Initialize(settings, BookStitchEnvironment.Production, () => false);
        for (var i = 0; i < 10; i++) state.RegisterDiscoveryClick();
        Assert.False(state.IsSelectorAvailable);
        state.IsVisible = true;
        Assert.False(state.IsVisible);
        state.Initialize(settings, BookStitchEnvironment.Development, () => false);
        state.IsVisible = false;
        Assert.True(state.IsVisible);
        Assert.Null(settings.DeveloperToolsVisible);
    }
}
