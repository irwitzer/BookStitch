using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterEffectiveCountServiceTests
{
    [Fact]
    public void EstimateEffectiveActiveChapterCount_UsesDiscCountAndImportedAverage()
    {
        var tracks = CreateDiscTracks(1, 20);

        var result = ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(tracks, totalDiscs: 2);

        Assert.Equal(40, result);
    }

    [Theory]
    [InlineData(126, 10, 1260, 4)]
    [InlineData(15, 99, 1485, 4)]
    public void EstimateEffectiveActiveChapterCount_UsesEnteredDiscCountForNumberWidth(
        int firstDiscTracks,
        int totalDiscs,
        int expectedEffectiveCount,
        int expectedWidth)
    {
        var tracks = CreateDiscTracks(1, firstDiscTracks);

        var result = ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(tracks, totalDiscs);

        Assert.Equal(expectedEffectiveCount, result);
        Assert.Equal(expectedWidth, ChapterTitleRenderer.CalculateNumberWidth(result));
    }

    [Fact]
    public void EstimateEffectiveActiveChapterCount_ChangingDiscCountCanChangeNumberWidthWithoutTrackAnalysis()
    {
        var tracks = CreateDiscTracks(1, 126);

        var oneDisc = ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(tracks, totalDiscs: 1);
        var tenDiscs = ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(tracks, totalDiscs: 10);

        Assert.Equal(3, ChapterTitleRenderer.CalculateNumberWidth(oneDisc));
        Assert.Equal(4, ChapterTitleRenderer.CalculateNumberWidth(tenDiscs));
    }

    [Fact]
    public void EstimateEffectiveActiveChapterCount_DoesNotUndercutActualImportedTracks()
    {
        var tracks = CreateDiscTracks(1, 40)
            .Concat(CreateDiscTracks(2, 1))
            .ToList();

        var result = ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(tracks, totalDiscs: 1);

        Assert.Equal(41, result);
    }

    [Fact]
    public void EstimateEffectiveActiveChapterCount_IgnoresExcludedTracks()
    {
        var tracks = CreateDiscTracks(1, 100);
        tracks[0].IsExcluded = true;

        var result = ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(tracks, totalDiscs: 1);

        Assert.Equal(99, result);
    }

    [Fact]
    public void EstimateEffectiveActiveChapterCount_UsesEffectiveChapterContributionPerImportedDisc()
    {
        var tracks = new List<TrackInfo>
        {
            EmbeddedDiscTrack(1, 20),
            new() { DiscNumber = 1, TrackNumber = 2 },
            new() { DiscNumber = 1, TrackNumber = 3 }
        };

        var result = ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(tracks, totalDiscs: 2);

        Assert.Equal(44, result);
    }

    [Fact]
    public void EstimateEffectiveActiveChapterCount_CountsAllInvalidEmbeddedChaptersAsOneSyntheticChapter()
    {
        var tracks = new List<TrackInfo>
        {
            new()
            {
                DiscNumber = 1,
                DurationTicks = TimeSpan.FromSeconds(10).Ticks,
                EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo
                    {
                        StartTicks = TimeSpan.FromSeconds(3).Ticks,
                        EndTicks = TimeSpan.FromSeconds(3).Ticks
                    }
                ]
            }
        };

        var result = ChapterEffectiveCountService.EstimateEffectiveActiveChapterCount(tracks, totalDiscs: 3);

        Assert.Equal(3, result);
    }

    private static List<TrackInfo> CreateDiscTracks(int discNumber, int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new TrackInfo
            {
                DiscNumber = discNumber,
                TrackNumber = index,
                ChapterTitle = $"{index} Titel"
            })
            .ToList();
    }

    private static TrackInfo EmbeddedDiscTrack(int discNumber, int chapterCount)
    {
        return new TrackInfo
        {
            DiscNumber = discNumber,
            TrackNumber = 1,
            DurationTicks = TimeSpan.FromSeconds(chapterCount).Ticks,
            EmbeddedChapters = Enumerable.Range(0, chapterCount)
                .Select(index => new TrackEmbeddedChapterInfo
                {
                    Title = $"Kapitel {index + 1}",
                    StartTicks = TimeSpan.FromSeconds(index).Ticks,
                    EndTicks = TimeSpan.FromSeconds(index + 1).Ticks
                })
                .ToList()
        };
    }
}
