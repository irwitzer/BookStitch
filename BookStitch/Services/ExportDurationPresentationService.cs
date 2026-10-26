namespace BookStitch.Services;

public static class ExportDurationPresentationService
{
    public static string Create(
        int trackCount,
        IReadOnlyCollection<TimeSpan> knownDurations)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trackCount);
        ArgumentNullException.ThrowIfNull(knownDurations);

        if (knownDurations.Any(duration => duration < TimeSpan.Zero))
            throw new ArgumentOutOfRangeException(nameof(knownDurations));

        if (trackCount == 0)
            return LocalizationText.Format("ExportPreview.Duration", "00:00:00");

        if (knownDurations.Count == 0)
            return LocalizationText.Get("ExportPreview.DurationUnknown");

        var total = TimeSpan.FromSeconds(
            knownDurations.Sum(duration => duration.TotalSeconds));

        var totalText = FormatDuration(total);

        return knownDurations.Count == trackCount
            ? LocalizationText.Format("ExportPreview.Duration", totalText)
            : LocalizationText.Format("ExportPreview.DurationPartial", totalText, knownDurations.Count, trackCount);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var totalHours = (int)duration.TotalHours;
        return $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
