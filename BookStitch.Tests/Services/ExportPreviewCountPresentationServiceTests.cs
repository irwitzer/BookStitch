using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ExportPreviewCountPresentationServiceTests
{
    [Theory]
    [InlineData(0, 0, "Tracks: 0", "Kapitel: 0")]
    [InlineData(1, 1, "Tracks: 1", "Kapitel: 1 geplant")]
    [InlineData(2, 5, "Tracks: 2", "Kapitel: 5 geplant")]
    public void Create_ReturnsExpectedSummaries(
        int trackCount,
        int chapterCount,
        string expectedTrackSummary,
        string expectedChapterSummary)
    {
        var result = ExportPreviewCountPresentationService.Create(
            trackCount,
            chapterCount);

        Assert.Equal(expectedTrackSummary, result.TrackSummary);
        Assert.Equal(expectedChapterSummary, result.ChapterSummary);
    }

    [Fact]
    public void Create_RejectsNegativeCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExportPreviewCountPresentationService.Create(-1, 0));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExportPreviewCountPresentationService.Create(0, -1));
    }
}