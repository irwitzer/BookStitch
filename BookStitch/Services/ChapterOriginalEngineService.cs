using BookStitch.Models;

namespace BookStitch.Services;

public sealed class ChapterOriginalEngineService
{
    private readonly SourceMetadataSnapshotStore _snapshotStore;

    public ChapterOriginalEngineService()
        : this(new SourceMetadataSnapshotStore())
    {
    }

    public ChapterOriginalEngineService(SourceMetadataSnapshotStore snapshotStore)
    {
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
    }

    public ActiveChapterResult Apply(ChapterAutoEngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeline = BuildOriginalTimeline(request);
        ApplyIndexesAndOriginalChapterTitles(request.Tracks, timeline);
        return new ActiveChapterResult(ChapterSettingsService.ModeOriginal, timeline);
    }

    public IReadOnlyList<EffectiveChapterInfo> BuildOriginalTimeline(ChapterAutoEngineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outputChapterIndex = 0;
        var activeTrackOffset = 0L;
        var result = new List<EffectiveChapterInfo>();

        foreach (var track in request.Tracks)
        {
            if (track.IsExcluded)
                continue;

            var trackDurationTicks = TrackDurationService.GetEffectiveDurationTicks(track);
            foreach (var chapter in GetOriginalChapters(request.ProjectFolder, track).OrderBy(chapter => chapter.StartTicks))
            {
                var startTicks = Math.Max(0, chapter.StartTicks);
                if (startTicks >= trackDurationTicks)
                    continue;

                var endTicks = Math.Min(chapter.EndTicks, trackDurationTicks);
                if (endTicks <= startTicks)
                    continue;

                result.Add(new EffectiveChapterInfo(
                    ++outputChapterIndex,
                    track,
                    ResolvePresentationChapter(track, chapter),
                    chapter.Title ?? string.Empty,
                    activeTrackOffset + startTicks,
                    activeTrackOffset + endTicks,
                    startTicks,
                    endTicks,
                    IsSynthetic: false));
            }

            activeTrackOffset += trackDurationTicks;
        }

        return result;
    }

    private IReadOnlyList<TrackEmbeddedChapterInfo> GetOriginalChapters(string projectFolder, TrackInfo track)
    {
        if (!string.IsNullOrWhiteSpace(projectFolder) &&
            track.SourceId != Guid.Empty)
        {
            var snapshot = _snapshotStore.TryLoad(projectFolder, track.SourceId);
            if (snapshot is not null)
                return snapshot.OriginalChapters;
        }

        return track.EmbeddedChapters;
    }

    private static TrackEmbeddedChapterInfo ResolvePresentationChapter(
        TrackInfo track,
        TrackEmbeddedChapterInfo originalChapter)
    {
        return track.EmbeddedChapters.FirstOrDefault(chapter =>
                   chapter.StartTicks == originalChapter.StartTicks &&
                   chapter.EndTicks == originalChapter.EndTicks &&
                   string.Equals(chapter.Title, originalChapter.Title, StringComparison.Ordinal))
               ?? originalChapter;
    }

    private static void ApplyIndexesAndOriginalChapterTitles(
        IEnumerable<TrackInfo> tracks,
        IReadOnlyList<EffectiveChapterInfo> timeline)
    {
        var firstChapterByTrack = timeline
            .GroupBy(chapter => chapter.SourceTrack, ReferenceEqualityComparer<TrackInfo>.Instance)
            .ToDictionary(group => group.Key, group => group.First(), ReferenceEqualityComparer<TrackInfo>.Instance);
        var activeTrackIndex = 0;

        foreach (var track in tracks)
        {
            track.Index = track.IsExcluded ? 0 : ++activeTrackIndex;
            track.ChapterTitle = firstChapterByTrack.TryGetValue(track, out var chapter)
                ? chapter.Title
                : string.Empty;
        }
    }
}
