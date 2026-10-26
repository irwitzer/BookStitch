using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AppSettingsReleaseDefaultsTests
{
    [Fact]
    public void Defaults_KeepReleaseExperimentsDisabled()
    {
        var settings = new AppSettings();

        Assert.False(settings.ExperimentalDriveRoundEnabled);
        Assert.Empty(settings.DiscDriveOrder);
        Assert.False(settings.UsePrivateGenreList);
        Assert.Equal("Audiobook", settings.DefaultGenre);
    }
}
