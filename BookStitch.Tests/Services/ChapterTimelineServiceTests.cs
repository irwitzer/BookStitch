using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterTimelineServiceTests
{
    private readonly ChapterTimelineService _service = new();

    [Fact]
    public void BuildTimeline_ExpandsMultipleEmbeddedSources()
    {
        var timeline = _service.BuildTimeline(
        [
            EmbeddedTrack("A", 30, 3),
            EmbeddedTrack("B", 40, 4),
            EmbeddedTrack("C", 20, 2)
        ]);

        Assert.Equal(9, timeline.Count);
        Assert.Equal(Enumerable.Range(1, 9), timeline.Select(chapter => chapter.OutputChapterIndex));
        Assert.Equal([0, 10000, 20000, 30000, 40000, 50000, 60000, 70000, 80000], StartsInMilliseconds(timeline));
        Assert.Equal(["A 1", "A 2", "A 3", "B 1", "B 2", "B 3", "B 4", "C 1", "C 2"], Titles(timeline));
        Assert.All(timeline, chapter => Assert.False(chapter.IsSynthetic));
    }

    [Fact]
    public void BuildTimeline_MixesEmbeddedAndSyntheticSourcesWithGlobalChapterPositions()
    {
        var timeline = _service.BuildTimeline(
        [
            EmbeddedTrack("M4B", 100, 100, index: 1),
            new TrackInfo
            {
                Index = 2,
                FileName = "Plain MP3.mp3",
                DurationTicks = TimeSpan.FromSeconds(5).Ticks
            },
            EmbeddedTrack("M4A", 50, 50, index: 3),
            new TrackInfo
            {
                Index = 4,
                ChapterTitle = "FLAC fallback",
                DurationTicks = TimeSpan.FromSeconds(7).Ticks
            }
        ]);

        Assert.Equal(152, timeline.Count);
        Assert.Equal(101, timeline[100].OutputChapterIndex);
        Assert.Equal("Plain MP3", timeline[100].Title);
        Assert.True(timeline[100].IsSynthetic);
        Assert.Equal(TimeSpan.FromSeconds(100).Ticks, timeline[100].StartTicks);
        Assert.Equal(152, timeline[^1].OutputChapterIndex);
        Assert.Equal("FLAC fallback", timeline[^1].Title);
        Assert.True(timeline[^1].IsSynthetic);
        Assert.Equal([1, 2, 3, 4], timeline.Select(chapter => chapter.SourceTrack.Index).Distinct());
    }

    [Fact]
    public void BuildTimeline_HandlesLargeEmbeddedChapterSetWithoutDrift()
    {
        var timeline = _service.BuildTimeline(
            Enumerable.Range(1, 5)
                .Select(index => EmbeddedTrack($"T{index}", 200, 200))
                .ToList());

        Assert.Equal(1000, timeline.Count);
        Assert.Equal(0, ToMilliseconds(timeline[0].StartTicks));
        Assert.Equal(199000, ToMilliseconds(timeline[199].StartTicks));
        Assert.Equal(200000, ToMilliseconds(timeline[200].StartTicks));
        Assert.Equal(999000, ToMilliseconds(timeline[^1].StartTicks));
        Assert.Equal(1000000, ToMilliseconds(timeline[^1].EndTicks));
    }

    [Fact]
    public void BuildTimeline_NormalizesDefectiveEmbeddedChaptersConservatively()
    {
        var timeline = _service.BuildTimeline(
        [
            new TrackInfo
            {
                DurationTicks = TimeSpan.FromSeconds(10).Ticks,
                EmbeddedChapters =
                [
                    Chapter("EndBeforeStart", 9, 8),
                    Chapter("NegativeStart", -2, 2),
                    Chapter("BeyondDuration", 12, 15),
                    Chapter("ClampedEnd", 8, 12)
                ]
            }
        ]);

        Assert.Equal(2, timeline.Count);
        Assert.Equal(["NegativeStart", "ClampedEnd"], Titles(timeline));
        Assert.Equal([0, 8000], StartsInMilliseconds(timeline));
        Assert.Equal([2000, 10000], EndsInMilliseconds(timeline));
    }

    [Fact]
    public void BuildTimeline_UsesSyntheticChapterWhenAllEmbeddedChaptersAreInvalid()
    {
        var timeline = _service.BuildTimeline(
        [
            new TrackInfo
            {
                ChapterTitle = "Track fallback",
                DurationTicks = TimeSpan.FromSeconds(10).Ticks,
                EmbeddedChapters =
                [
                    Chapter("Invalid", 3, 3)
                ]
            }
        ]);

        var chapter = Assert.Single(timeline);
        Assert.True(chapter.IsSynthetic);
        Assert.Equal("Track fallback", chapter.Title);
        Assert.Equal(0, chapter.StartTicks);
        Assert.Equal(TimeSpan.FromSeconds(10).Ticks, chapter.EndTicks);
    }

    [Fact]
    public void BuildTimeline_UsesGlobalFallbackTitlesForUntitledEmbeddedChapters()
    {
        var timeline = _service.BuildTimeline(
        [
            new TrackInfo
            {
                DurationTicks = TimeSpan.FromSeconds(2).Ticks,
                EmbeddedChapters =
                [
                    Chapter("", 0, 1),
                    Chapter("   ", 1, 2)
                ]
            },
            new TrackInfo
            {
                DurationTicks = TimeSpan.FromSeconds(2).Ticks,
                EmbeddedChapters =
                [
                    Chapter("", 0, 1),
                    Chapter("", 1, 2)
                ]
            }
        ]);

        Assert.Equal(["Kapitel 1", "Kapitel 2", "Kapitel 3", "Kapitel 4"], Titles(timeline));
    }

    [Fact]
    public void BuildTimeline_UsesTrackDurationInsteadOfLastEmbeddedChapterForNextTrackStart()
    {
        var timeline = _service.BuildTimeline(
        [
            new TrackInfo
            {
                DurationTicks = TimeSpan.FromSeconds(10).Ticks,
                EmbeddedChapters =
                [
                    Chapter("Only early chapter", 0, 3)
                ]
            },
            new TrackInfo
            {
                ChapterTitle = "Next",
                DurationTicks = TimeSpan.FromSeconds(2).Ticks
            }
        ]);

        Assert.Equal(TimeSpan.FromSeconds(10).Ticks, timeline[1].StartTicks);
        Assert.False(timeline[0].IsSynthetic);
        Assert.Equal("Only early chapter", timeline[0].Title);
    }

    [Fact]
    public void BuildTimeline_UsesSingleValidEmbeddedChapterWithoutSyntheticParent()
    {
        var sourceChapter = Chapter("Only embedded", 1, 4);
        var track = new TrackInfo
        {
            ChapterTitle = "Parent fallback",
            DurationTicks = TimeSpan.FromSeconds(5).Ticks,
            EmbeddedChapters = [sourceChapter]
        };

        var chapter = Assert.Single(_service.BuildTimeline([track]));

        Assert.False(chapter.IsSynthetic);
        Assert.Same(sourceChapter, chapter.SourceEmbeddedChapter);
        Assert.Equal("Only embedded", chapter.Title);
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks, chapter.SourceStartTicks);
        Assert.Equal(TimeSpan.FromSeconds(4).Ticks, chapter.SourceEndTicks);
    }

    [Fact]
    public void BuildTimeline_UsesTrackDurationServiceFallbacksForSyntheticChapters()
    {
        var timeline = _service.BuildTimeline(
        [
            new TrackInfo
            {
                ChapterTitle = "Parsed duration",
                Duration = "00:01.250"
            },
            new TrackInfo
            {
                ChapterTitle = "Missing duration"
            }
        ]);

        Assert.Equal([0, 1250], StartsInMilliseconds(timeline));
        Assert.Equal([1250, 2250], EndsInMilliseconds(timeline));
    }

    private static TrackInfo EmbeddedTrack(string prefix, int durationSeconds, int chapterCount, int? index = null)
    {
        var chapterDuration = TimeSpan.FromSeconds(durationSeconds / (double)chapterCount);
        return new TrackInfo
        {
            Index = index ?? (int.TryParse(prefix.TrimStart('T'), out var parsedIndex) ? parsedIndex : 0),
            DurationTicks = TimeSpan.FromSeconds(durationSeconds).Ticks,
            EmbeddedChapters = Enumerable.Range(0, chapterCount)
                .Select(index => new TrackEmbeddedChapterInfo
                {
                    Title = $"{prefix} {index + 1}",
                    StartTicks = (long)Math.Round(chapterDuration.Ticks * (double)index),
                    EndTicks = (long)Math.Round(chapterDuration.Ticks * (double)(index + 1))
                })
                .ToList()
        };
    }

    private static TrackEmbeddedChapterInfo Chapter(string title, double startSeconds, double endSeconds)
    {
        return new TrackEmbeddedChapterInfo
        {
            Title = title,
            StartTicks = TimeSpan.FromSeconds(startSeconds).Ticks,
            EndTicks = TimeSpan.FromSeconds(endSeconds).Ticks
        };
    }

    private static List<string> Titles(IReadOnlyList<EffectiveChapterInfo> timeline)
    {
        return timeline.Select(chapter => chapter.Title).ToList();
    }

    private static List<long> StartsInMilliseconds(IReadOnlyList<EffectiveChapterInfo> timeline)
    {
        return timeline.Select(chapter => ToMilliseconds(chapter.StartTicks)).ToList();
    }

    private static List<long> EndsInMilliseconds(IReadOnlyList<EffectiveChapterInfo> timeline)
    {
        return timeline.Select(chapter => ToMilliseconds(chapter.EndTicks)).ToList();
    }

    private static long ToMilliseconds(long ticks)
    {
        return (long)Math.Round(ticks / (double)TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero);
    }
}
