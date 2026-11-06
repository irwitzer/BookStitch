using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>Source-bitrate evidence for the existing advisory; never grants preparation or merge permission.</summary>
public static class PresetBitrateWarningService
{
    public const decimal WarningToleranceBitsPerSecond = 15000m;

    public static int? GetMaxSourceBitrateKbps(IEnumerable<TrackInfo> tracks)
    {
        // Preserve the existing conservative comparison against the highest known source bitrate.
        // Prepared/display bitrates are deliberately not source evidence.
        return tracks.Where(track => !track.IsExcluded).Select(track => track.BitrateKbps).Where(value => value is > 0)
            .DefaultIfEmpty(null).Max();
    }

    public static bool ExceedsTolerance(decimal? sourceBitsPerSecond, int targetKbps) =>
        sourceBitsPerSecond is > 0 && targetKbps * 1000m - sourceBitsPerSecond.Value > WarningToleranceBitsPerSecond;

    public static bool ShouldWarnAfterLoad(IEnumerable<TrackInfo> tracks, string preset, bool selectedPresetFullyPrepared)
    {
        if (selectedPresetFullyPrepared) return false;
        var sourceBitrate = tracks.Where(track => !track.IsExcluded)
            .Select(track => track.AudioBitrateBitsPerSecond is > 0 ? track.AudioBitrateBitsPerSecond : track.BitrateKbps * 1000m)
            .Where(value => value is > 0).DefaultIfEmpty(null).Max();
        return ExceedsTolerance(sourceBitrate, ExportPreset.Parse(preset).BitrateKbps);
    }
}
