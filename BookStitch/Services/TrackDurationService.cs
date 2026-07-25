using BookStitch.Models;
using System.Globalization;

namespace BookStitch.Services;

public static class TrackDurationService
{
    public static TimeSpan? GetPreciseDuration(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (track.DurationTicks is > 0)
            return TimeSpan.FromTicks(track.DurationTicks.Value);

        return TryParseDuration(track.Duration);
    }

    public static long GetEffectiveDurationTicks(TrackInfo track)
    {
        var duration = GetPreciseDuration(track) ?? TimeSpan.FromSeconds(1);
        return Math.Max(duration.Ticks, TimeSpan.FromSeconds(1).Ticks);
    }

    private static TimeSpan? TryParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }

        if (parts.Length == 3 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }

        return null;
    }
}
