using BookStitch.Models;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace BookStitch.Services;

public sealed class ChapterTimelinePresentationService
{
    public void ApplyResolvedTimeline(
        IReadOnlyList<TrackInfo> tracks,
        IReadOnlyList<EffectiveChapterInfo> timeline)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(timeline);

        var syntheticByTrack = timeline
            .Where(chapter => chapter.IsSynthetic)
            .ToDictionary(chapter => chapter.SourceTrack, ReferenceEqualityComparer<TrackInfo>.Instance);
        var singleEmbeddedByTrack = timeline
            .Where(chapter => !chapter.IsSynthetic)
            .GroupBy(chapter => chapter.SourceTrack, ReferenceEqualityComparer<TrackInfo>.Instance)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First(), ReferenceEqualityComparer<TrackInfo>.Instance);
        var embeddedByKey = timeline
            .Where(chapter => !chapter.IsSynthetic)
            .GroupBy(
                chapter => new EmbeddedChapterKey(chapter.SourceTrack, chapter.SourceEmbeddedChapter))
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var track in tracks)
        {
            if (track.IsExcluded)
            {
                track.ApplyTimelinePresentation(string.Empty, string.Empty);
                ApplyEmbeddedChapterPresentation(track, embeddedByKey);
                continue;
            }

            if (track.HasEmbeddedChapterDetails)
            {
                track.ApplyTimelinePresentation(string.Empty, string.Empty);
                ApplyEmbeddedChapterPresentation(track, embeddedByKey);
                continue;
            }

            if (syntheticByTrack.TryGetValue(track, out var syntheticChapter))
            {
                track.ApplyTimelinePresentation(
                    syntheticChapter.Title,
                    FormatTimelineStart(syntheticChapter.StartTicks));
            }
            else if (singleEmbeddedByTrack.TryGetValue(track, out var embeddedChapter))
            {
                track.ApplyTimelinePresentation(
                    embeddedChapter.Title,
                    FormatTimelineStart(embeddedChapter.StartTicks));
            }
            else
            {
                track.ApplyTimelinePresentation(track.ChapterTitle, string.Empty);
            }

            ApplyEmbeddedChapterPresentation(track, embeddedByKey);
        }
    }

    private static void ApplyEmbeddedChapterPresentation(
        TrackInfo track,
        IReadOnlyDictionary<EmbeddedChapterKey, EffectiveChapterInfo> embeddedByKey)
    {
        if (!track.IsChapterDetailsExpanded && !track.HasMaterializedEmbeddedChapterRows)
            return;

        foreach (var row in track.EmbeddedChapterRows)
        {
            var key = new EmbeddedChapterKey(track, row.SourceChapter);
            if (embeddedByKey.TryGetValue(key, out var chapter))
            {
                row.ApplyTimelinePresentation(
                    chapter.Title,
                    FormatTimelineStart(chapter.StartTicks));
            }
            else
            {
                row.ApplyTimelinePresentation(string.Empty, string.Empty);
            }
        }
    }

    private static string FormatTimelineStart(long ticks)
    {
        var duration = TimeSpan.FromTicks(Math.Max(0, ticks));
        var totalHours = (int)Math.Floor(duration.TotalHours);
        return totalHours.ToString("00", CultureInfo.InvariantCulture) +
               duration.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private readonly record struct EmbeddedChapterKey(TrackInfo Track, TrackEmbeddedChapterInfo? SourceChapter);
}

internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static ReferenceEqualityComparer<T> Instance { get; } = new();

    public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
