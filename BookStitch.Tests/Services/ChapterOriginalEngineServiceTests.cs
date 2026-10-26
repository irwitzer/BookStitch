using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterOriginalEngineServiceTests
{
    private readonly ChapterOriginalEngineService _service = new();

    [Fact]
    public void Apply_NoEmbeddedChaptersProducesEmptyTimelineWithoutSyntheticFallback()
    {
        var track = Track("Title", seconds: 5);

        var result = _service.Apply(Request([track]));

        Assert.Equal(ChapterSettingsService.ModeOriginal, result.Mode);
        Assert.Null(result.EngineResult);
        Assert.Empty(result.RenderedTimeline);
        Assert.Equal(string.Empty, track.ChapterTitle);
    }

    [Fact]
    public void Apply_PreservesOriginalTitlesExactlyIncludingWhitespaceAndEmptyTitle()
    {
        var track = Track("Working title", seconds: 10);
        track.EmbeddedChapters =
        [
            Chapter("  Original Intro  ", 0, 3),
            Chapter("", 3, 6)
        ];

        var result = _service.Apply(Request([track]));

        Assert.Equal(["  Original Intro  ", ""], result.RenderedTimeline.Select(chapter => chapter.Title));
        Assert.All(result.RenderedTimeline, chapter => Assert.False(chapter.IsSynthetic));
    }

    [Fact]
    public void Apply_MixedProjectKeepsOnlyTracksWithOriginalChaptersAndOffsetsByActiveOrder()
    {
        var first = Track("First", seconds: 10);
        first.EmbeddedChapters = [Chapter("First Original", 2, 4)];
        var withoutChapters = Track("No original", seconds: 20);
        var second = Track("Second", seconds: 8);
        second.EmbeddedChapters = [Chapter("Second Original", 1, 3)];

        var result = _service.Apply(Request([first, withoutChapters, second]));

        Assert.Equal(["First Original", "Second Original"], result.RenderedTimeline.Select(chapter => chapter.Title));
        Assert.Equal(TimeSpan.FromSeconds(2).Ticks, result.RenderedTimeline[0].StartTicks);
        Assert.Equal(TimeSpan.FromSeconds(31).Ticks, result.RenderedTimeline[1].StartTicks);
    }

    [Fact]
    public void Apply_ExcludedTracksAreOmittedAndDoNotConsumeTimelineOffset()
    {
        var excluded = Track("Excluded", seconds: 20);
        excluded.IsExcluded = true;
        excluded.EmbeddedChapters = [Chapter("Hidden", 0, 5)];
        var active = Track("Active", seconds: 10);
        active.EmbeddedChapters = [Chapter("Visible", 1, 2)];

        var result = _service.Apply(Request([excluded, active]));

        var chapter = Assert.Single(result.RenderedTimeline);
        Assert.Equal("Visible", chapter.Title);
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks, chapter.StartTicks);
        Assert.Equal(0, excluded.Index);
        Assert.Equal(1, active.Index);
    }

    [Fact]
    public void Apply_CurrentTrackOrderControlsChapterOrder()
    {
        var first = Track("First", seconds: 5);
        first.EmbeddedChapters = [Chapter("B", 0, 1)];
        var second = Track("Second", seconds: 5);
        second.EmbeddedChapters = [Chapter("A", 0, 1)];

        var result = _service.Apply(Request([second, first]));

        Assert.Equal(["A", "B"], result.RenderedTimeline.Select(chapter => chapter.Title));
        Assert.Equal(1, second.Index);
        Assert.Equal(2, first.Index);
    }

    [Fact]
    public void Apply_NumberingStyleDoesNotChangeOriginalTitles()
    {
        var track = Track("Working", seconds: 5);
        track.EmbeddedChapters = [Chapter("001 Already Original", 0, 2)];

        var plain = _service.Apply(Request([track], ChapterNumberingStyle.Plain));
        var padded = _service.Apply(Request([track], ChapterNumberingStyle.ZeroPadded));
        var none = _service.Apply(Request([track], ChapterNumberingStyle.None));

        Assert.Equal("001 Already Original", Assert.Single(plain.RenderedTimeline).Title);
        Assert.Equal("001 Already Original", Assert.Single(padded.RenderedTimeline).Title);
        Assert.Equal("001 Already Original", Assert.Single(none.RenderedTimeline).Title);
    }

    [Fact]
    public void Apply_ClampsIntervalsConservativelyWithoutCleaningContent()
    {
        var track = Track("Working", seconds: 10);
        track.EmbeddedChapters =
        [
            Chapter("Invalid", 8, 8),
            Chapter("Negative Start", -2, 2),
            Chapter("Clamped End", 8, 12),
            Chapter("Beyond", 12, 13)
        ];

        var result = _service.Apply(Request([track]));

        Assert.Equal(["Negative Start", "Clamped End"], result.RenderedTimeline.Select(chapter => chapter.Title));
        Assert.Equal(0, result.RenderedTimeline[0].SourceStartTicks);
        Assert.Equal(TimeSpan.FromSeconds(10).Ticks, result.RenderedTimeline[1].SourceEndTicks);
    }

    [Fact]
    public void Apply_UsesPersistedSourceSnapshotBeforeMutableWorkingTrackTitle()
    {
        using var folder = new TemporaryFolder();
        var sourceId = Guid.NewGuid();
        var track = Track("Working", seconds: 5, sourceId);
        track.EmbeddedChapters = [Chapter("Mutated Working", 0, 2)];
        new SourceMetadataSnapshotStore().Save(
            folder.Path,
            new SourceMetadataSnapshot
            {
                SourceId = sourceId,
                CanonicalFields = new SourceMetadataCanonicalFields { Title = "Source" },
                OriginalChapters = [Chapter("Original Snapshot", 0, 2)],
                TechnicalInfo = new SourceTechnicalInfo { SourcePath = track.FilePath }
            });

        var result = _service.Apply(Request([track], projectFolder: folder.Path));

        Assert.Equal("Original Snapshot", Assert.Single(result.RenderedTimeline).Title);
    }

    private static ChapterAutoEngineRequest Request(
        IList<TrackInfo> tracks,
        ChapterNumberingStyle numberingStyle = ChapterNumberingStyle.Plain,
        string projectFolder = "") =>
        new(
            tracks,
            ProjectManifestTypes.FolderProject,
            "original-test",
            projectFolder,
            new ChapterCanonicalMetadataSnapshot(1, "Book", MetadataOrigin.User, "Album", MetadataOrigin.User),
            new ChapterSettings { Mode = ChapterSettingsService.ModeOriginal, NumberingStyle = numberingStyle.ToString() },
            LegacyUseLeadingZeros: false);

    private static TrackInfo Track(string title, int seconds) => Track(title, seconds, Guid.Empty);

    private static TrackInfo Track(string title, int seconds, Guid withSourceId) => new()
    {
        SourceId = withSourceId,
        FilePath = title + ".m4a",
        FileName = title + ".m4a",
        TagTitle = title,
        ChapterTitle = title,
        DurationTicks = TimeSpan.FromSeconds(seconds).Ticks
    };

    private static TrackEmbeddedChapterInfo Chapter(string title, double startSeconds, double endSeconds) => new()
    {
        Title = title,
        StartTicks = TimeSpan.FromSeconds(startSeconds).Ticks,
        EndTicks = TimeSpan.FromSeconds(endSeconds).Ticks
    };
}
