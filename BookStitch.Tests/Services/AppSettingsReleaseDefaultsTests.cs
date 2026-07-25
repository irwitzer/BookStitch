using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AppSettingsReleaseDefaultsTests
{
    [Fact]
    public void Defaults_HideDeveloperTabAndKeepDriveRoundDisabled()
    {
        var settings = new AppSettings();

        Assert.False(settings.ShowDeveloperTab);
        Assert.False(settings.ShowPipelineStateDebug);
        Assert.False(settings.ForceShowFfmpegSetupButton);
        Assert.False(settings.ExperimentalDriveRoundEnabled);
        Assert.Empty(settings.DiscDriveOrder);
        Assert.False(settings.UsePrivateGenreList);
    }
}
