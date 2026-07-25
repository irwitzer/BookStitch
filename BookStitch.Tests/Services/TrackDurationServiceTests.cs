using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackDurationServiceTests
{
    [Fact]
    public void GetPreciseDuration_PrefersDurationTicks()
    {
        var track = new TrackInfo
        {
            Duration = "00:01:00",
            DurationTicks = TimeSpan.FromSeconds(42).Ticks
        };

        Assert.Equal(TimeSpan.FromSeconds(42), TrackDurationService.GetPreciseDuration(track));
    }

    [Theory]
    [InlineData("02:03", 123)]
    [InlineData("01:02:03", 3723)]
    public void GetPreciseDuration_ParsesDisplayedDuration(string value, int expectedSeconds)
    {
        var track = new TrackInfo { Duration = value };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TrackDurationService.GetPreciseDuration(track));
    }

    [Fact]
    public void GetEffectiveDurationTicks_UsesOneSecondFallback()
    {
        var track = new TrackInfo();

        Assert.Equal(TimeSpan.FromSeconds(1).Ticks, TrackDurationService.GetEffectiveDurationTicks(track));
    }
}
