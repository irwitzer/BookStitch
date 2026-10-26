using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ExportDurationPresentationServiceTests
{
    [Fact]
    public void Create_ReturnsZeroDurationWithoutTracks()
    {
        var result = ExportDurationPresentationService.Create(
            0,
            Array.Empty<TimeSpan>());

        Assert.Equal("Gesamtdauer: 00:00:00", result);
    }

    [Fact]
    public void Create_ReturnsUnknownWhenNoDurationIsKnown()
    {
        var result = ExportDurationPresentationService.Create(
            2,
            Array.Empty<TimeSpan>());

        Assert.Equal("Gesamtdauer: noch nicht bekannt", result);
    }

    [Fact]
    public void Create_ReturnsCompleteDurationWhenAllTracksAreKnown()
    {
        var result = ExportDurationPresentationService.Create(
            2,
            new[]
            {
                TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(45)
            });

        Assert.Equal("Gesamtdauer: 01:15:00", result);
    }

    [Fact]
    public void Create_ReturnsPartialProgressWhenOnlySomeTracksAreKnown()
    {
        var result = ExportDurationPresentationService.Create(
            3,
            new[]
            {
                TimeSpan.FromHours(1),
                TimeSpan.FromSeconds(5)
            });

        Assert.Equal("Gesamtdauer: 01:00:05 (2/3 bekannt)", result);
    }

    [Fact]
    public void Create_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExportDurationPresentationService.Create(
                -1,
                Array.Empty<TimeSpan>()));

        Assert.Throws<ArgumentNullException>(
            () => ExportDurationPresentationService.Create(
                1,
                null!));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExportDurationPresentationService.Create(
                1,
                new[] { TimeSpan.FromSeconds(-1) }));
    }
}