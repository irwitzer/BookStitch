using BookStitch.Models;
using BookStitch.Services;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DeveloperAppearancePreviewTests
{
    private static (DeveloperToolsAccessState Access, DeveloperAppearancePreview Preview) Create()
    {
        var access = new DeveloperToolsAccessState();
        access.Initialize(new AppSettings { DeveloperToolsEnabled = true, DeveloperToolsVisible = true }, BookStitchEnvironment.Production, () => true);
        return (access, new DeveloperAppearancePreview(access));
    }

    [Fact]
    public void Overrides_never_mutate_regular_settings_hide_keeps_them_and_conceal_discards_them()
    {
        var (access, preview) = Create();
        var regular = new AppSettings { BackgroundPreset = "DeepPetrol" };
        var before = JsonSerializer.Serialize(regular);
        preview.SetBackground("Ivory");
        preview.SetTextColor("invalid-text");
        preview.SetImage(AppearanceAssetRole.HeaderBrand, "HeaderBrand.Dark");
        Assert.Equal(before, JsonSerializer.Serialize(regular));
        Assert.Equal("Ivory", preview.Resolve(regular).BackgroundPreset);
        access.VisibilityChoice = DeveloperToolsVisibilityChoice.Hide;
        Assert.True(access.IsEnabled);
        Assert.Equal("HeaderBrand.Dark", preview.GetImageId(AppearanceAssetRole.HeaderBrand));
        Assert.Equal("Ivory", preview.Resolve(regular).BackgroundPreset);
        access.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
        Assert.False(access.IsEnabled);
        Assert.Null(preview.BackgroundOverride);
        Assert.Null(preview.TextColorOverride);
        Assert.Null(preview.GetImageId(AppearanceAssetRole.HeaderBrand));
        Assert.Equal("DeepPetrol", preview.Resolve(regular).BackgroundPreset);
        Assert.Equal(before, JsonSerializer.Serialize(regular));
    }

    [Fact]
    public void Ending_preview_uses_latest_regular_preferences_and_restart_has_no_overrides()
    {
        var (access, preview) = Create();
        var regular = new AppSettings { BackgroundPreset = "DeepPetrol" };
        preview.SetBackground("Ivory");
        regular.BackgroundPreset = "MidnightBlue";
        Assert.Equal("Ivory", preview.Resolve(regular).BackgroundPreset);
        var restarted = new DeveloperAppearancePreview(access);
        Assert.Equal("MidnightBlue", restarted.Resolve(regular).BackgroundPreset);
        access.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
        Assert.Equal("MidnightBlue", preview.Resolve(regular).BackgroundPreset);
    }

    [Fact]
    public void Disabled_access_filters_experimental_or_broken_settings()
    {
        var access = new DeveloperToolsAccessState();
        access.Initialize(new AppSettings(), BookStitchEnvironment.Production, () => true);
        var preview = new DeveloperAppearancePreview(access);
        Assert.False(access.IsSelectorAvailable);
        Assert.False(access.IsEnabled);
        preview.SetBackground("Ivory");
        foreach (var background in new[] { "PearlStone", "missing-background" })
        {
            var effective = preview.Resolve(new AppSettings { BackgroundPreset = background, TextColorPreset = "broken" });
            Assert.Equal("Black", effective.BackgroundPreset);
            Assert.Equal(AppearanceSettingsService.GetTextColorPreset(effective.TextColorPreset, AppearanceMode.Dark).Preset.ToString(), effective.TextColorPreset);
        }
        Assert.Equal("DeepPetrol", preview.Resolve(new AppSettings { BackgroundPreset = "DeepPetrol" }).BackgroundPreset);
    }

    [Fact]
    public void Image_overrides_allow_theme_mismatches_survive_theme_changes_and_auto_restores_default()
    {
        var (_, preview) = Create();
        preview.SetImage(AppearanceAssetRole.HeaderBrand, "HeaderBrand.Light");
        preview.SetBackground("Black");
        Assert.False(preview.GetImage(AppearanceAssetRole.HeaderBrand)!.Matches(AppearanceMode.Dark));
        preview.SetBackground("Ivory");
        Assert.Equal("HeaderBrand.Light", preview.GetImageId(AppearanceAssetRole.HeaderBrand));
        Assert.Null(preview.GetImage(AppearanceAssetRole.MetadataBanner));
        preview.SetImage(AppearanceAssetRole.HeaderBrand, "");
        Assert.Null(preview.GetImage(AppearanceAssetRole.HeaderBrand));
        preview.SetImage(AppearanceAssetRole.HeaderBrand, "missing-id");
        Assert.Null(preview.GetImage(AppearanceAssetRole.HeaderBrand));
        preview.SetImage(AppearanceAssetRole.HeaderBrand, "MetadataBanner.Dark");
        Assert.Null(preview.GetImage(AppearanceAssetRole.HeaderBrand));
    }

    [Fact]
    public void Placeholder_override_never_replaces_an_actual_cover_source()
    {
        var alternate = AppearanceAssetCatalog.Get(AppearanceAssetRole.CoverPlaceholder, AppearanceMode.Light).RelativePath;
        Assert.Equal("actual-cover.jpg", CoverPreviewPresentationService.NormalizeSource("actual-cover.jpg", AppearanceMode.Dark, alternate));
        Assert.Equal(alternate, CoverPreviewPresentationService.NormalizeSource("", AppearanceMode.Dark, alternate));
        Assert.Equal(alternate, CoverPreviewPresentationService.NormalizeSource(
            AppearanceAssetCatalog.Get(AppearanceAssetRole.CoverPlaceholder, AppearanceMode.Dark).RelativePath, AppearanceMode.Dark, alternate));
    }
}
