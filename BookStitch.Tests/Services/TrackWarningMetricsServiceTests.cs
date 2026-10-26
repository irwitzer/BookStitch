using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TrackWarningMetricsServiceTests
{
    [Fact]
    public void Create_CountsFileAndChapterWarningsIndependently()
    {
        var tracks = new[]
        {
            new TrackWarningMetric("Dateiwarnung", null),
            new TrackWarningMetric(null, "Kapitelwarnung"),
            new TrackWarningMetric("Dateiwarnung · Weitere Dateiwarnung", "Kapitelwarnung · Weitere Kapitelwarnung"),
            new TrackWarningMetric("   ", ""),
            new TrackWarningMetric(null, null)
        };

        var result = TrackWarningMetricsService.Create(tracks);

        Assert.Equal(2, result.FileWarningCount);
        Assert.Equal(2, result.ChapterWarningCount);
    }

    [Fact]
    public void Create_ReturnsZeroForNoWarnings()
    {
        var tracks = new[]
        {
            new TrackWarningMetric(null, null),
            new TrackWarningMetric("", "   ")
        };

        var result = TrackWarningMetricsService.Create(tracks);

        Assert.Equal(0, result.FileWarningCount);
        Assert.Equal(0, result.ChapterWarningCount);
    }
}
