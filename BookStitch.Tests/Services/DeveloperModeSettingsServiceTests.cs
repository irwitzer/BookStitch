using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DeveloperModeSettingsServiceTests
{
    private readonly DeveloperModeSettingsService _service = new();

    [Fact]
    public void Normalize_EnablesDeveloperModeWhenPipelineDebugIsEnabled()
    {
        var settings = new AppSettings
        {
            ShowDeveloperTab = false,
            ShowPipelineStateDebug = true,
            ForceShowFfmpegSetupButton = false
        };

        _service.Normalize(settings);

        Assert.True(settings.ShowDeveloperTab);
        Assert.True(settings.ShowPipelineStateDebug);
        Assert.False(settings.ForceShowFfmpegSetupButton);
    }

    [Fact]
    public void Normalize_EnablesDeveloperModeWhenFfmpegButtonIsForced()
    {
        var settings = new AppSettings
        {
            ShowDeveloperTab = false,
            ShowPipelineStateDebug = false,
            ForceShowFfmpegSetupButton = true
        };

        _service.Normalize(settings);

        Assert.True(settings.ShowDeveloperTab);
        Assert.False(settings.ShowPipelineStateDebug);
        Assert.True(settings.ForceShowFfmpegSetupButton);
    }

    [Fact]
    public void SetDeveloperMode_Off_DisablesDependentDeveloperOptions()
    {
        var settings = new AppSettings
        {
            ShowDeveloperTab = true,
            ShowPipelineStateDebug = true,
            ForceShowFfmpegSetupButton = true
        };

        _service.SetDeveloperMode(settings, enabled: false);

        Assert.False(settings.ShowDeveloperTab);
        Assert.False(settings.ShowPipelineStateDebug);
        Assert.False(settings.ForceShowFfmpegSetupButton);
    }

    [Fact]
    public void SetPipelineStateDebugVisible_On_EnablesDeveloperMode()
    {
        var settings = new AppSettings
        {
            ShowDeveloperTab = false,
            ShowPipelineStateDebug = false
        };

        _service.SetPipelineStateDebugVisible(settings, enabled: true);

        Assert.True(settings.ShowDeveloperTab);
        Assert.True(settings.ShowPipelineStateDebug);
    }

    [Fact]
    public void SetFfmpegSetupButtonAlwaysVisible_On_EnablesDeveloperMode()
    {
        var settings = new AppSettings
        {
            ShowDeveloperTab = false,
            ForceShowFfmpegSetupButton = false
        };

        _service.SetFfmpegSetupButtonAlwaysVisible(settings, enabled: true);

        Assert.True(settings.ShowDeveloperTab);
        Assert.True(settings.ForceShowFfmpegSetupButton);
    }
}
