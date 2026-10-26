namespace BookStitch.Services;

public sealed record TrackWarningMetric(
    string? FileWarning,
    string? ChapterWarning);

public sealed record TrackWarningMetrics(
    int FileWarningCount,
    int ChapterWarningCount);

public static class TrackWarningMetricsService
{
    public static TrackWarningMetrics Create(
        IEnumerable<TrackWarningMetric> tracks) =>
        new(
            tracks.Count(track =>
                !string.IsNullOrWhiteSpace(track.FileWarning)),
            tracks.Count(track =>
                !string.IsNullOrWhiteSpace(track.ChapterWarning)));
}