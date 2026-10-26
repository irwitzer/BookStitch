using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ExportPreviewMetricsServiceTests
{
    [Fact]
    public void CountPlannedChapters_CountsTracksAndEmbeddedChapters()
    {
        var tracks = new[]
        {
            new TrackInfo { DurationTicks = TimeSpan.FromSeconds(1).Ticks },
            EmbeddedTrack(3),
            EmbeddedTrack(5, isExcluded: true),
            EmbeddedTrack(1)
        };

        var result = ExportPreviewMetricsService.CountPlannedChapters(tracks);

        Assert.Equal(5, result);
    }

    [Fact]
    public void CountPlannedChapters_ReturnsZeroForNoIncludedTracks()
    {
        var tracks = new[]
        {
            new TrackInfo { IsExcluded = true },
            EmbeddedTrack(2, isExcluded: true)
        };

        var result = ExportPreviewMetricsService.CountPlannedChapters(tracks);

        Assert.Equal(0, result);
    }

    [Fact]
    public void CountPlannedChapters_UsesTimelineNormalizationForDefectiveEmbeddedChapters()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                DurationTicks = TimeSpan.FromSeconds(10).Ticks,
                EmbeddedChapters =
                [
                    Chapter(3, 3)
                ]
            },
            new TrackInfo
            {
                DurationTicks = TimeSpan.FromSeconds(10).Ticks,
                EmbeddedChapters =
                [
                    Chapter(0, 2),
                    Chapter(12, 15)
                ]
            }
        };

        var result = ExportPreviewMetricsService.CountPlannedChapters(tracks);

        Assert.Equal(2, result);
    }

    [Fact]
    public void GetKnownDurations_RemovesMissingDurationsAndPreservesOrder()
    {
        var durations = new TimeSpan?[]
        {
            TimeSpan.FromMinutes(3),
            null,
            TimeSpan.FromSeconds(45)
        };

        var result = ExportPreviewMetricsService.GetKnownDurations(durations);

        Assert.Equal(
            new[]
            {
                TimeSpan.FromMinutes(3),
                TimeSpan.FromSeconds(45)
            },
            result);
    }

    [Fact]
    public void GetKnownDurations_ReturnsEmptyListForUnknownDurations()
    {
        var result = ExportPreviewMetricsService.GetKnownDurations(
            new TimeSpan?[] { null, null });

        Assert.Empty(result);
    }

    private static TrackInfo EmbeddedTrack(int chapterCount, bool isExcluded = false)
    {
        return new TrackInfo
        {
            IsExcluded = isExcluded,
            DurationTicks = TimeSpan.FromSeconds(chapterCount).Ticks,
            EmbeddedChapters = Enumerable.Range(0, chapterCount)
                .Select(index => new TrackEmbeddedChapterInfo
                {
                    StartTicks = TimeSpan.FromSeconds(index).Ticks,
                    EndTicks = TimeSpan.FromSeconds(index + 1).Ticks
                })
                .ToList()
        };
    }

    private static TrackEmbeddedChapterInfo Chapter(double startSeconds, double endSeconds)
    {
        return new TrackEmbeddedChapterInfo
        {
            StartTicks = TimeSpan.FromSeconds(startSeconds).Ticks,
            EndTicks = TimeSpan.FromSeconds(endSeconds).Ticks
        };
    }
}
