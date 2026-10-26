using BookStitch.Models;

namespace BookStitch.Services;

public static class ExportPreviewMetricsService
{
    public static int CountPlannedChapters(
        IEnumerable<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var activeTracks = tracks
            .Where(track => !track.IsExcluded)
            .ToList();

        return activeTracks.Count == 0
            ? 0
            : new ChapterTimelineService().BuildTimeline(activeTracks).Count;
    }

    public static IReadOnlyList<TimeSpan> GetKnownDurations(
        IEnumerable<TimeSpan?> durations) =>
        durations
            .Where(duration => duration.HasValue)
            .Select(duration => duration!.Value)
            .ToList();
}
