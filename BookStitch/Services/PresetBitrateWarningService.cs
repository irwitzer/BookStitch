using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>Source-bitrate evidence for the existing advisory; never grants preparation or merge permission.</summary>
public static class PresetBitrateWarningService
{
    public static int? GetMaxSourceBitrateKbps(IEnumerable<TrackInfo> tracks)
    {
        // Preserve the existing conservative comparison against the highest known source bitrate.
        // Prepared/display bitrates are deliberately not source evidence.
        return tracks.Select(track => track.BitrateKbps).Where(value => value is > 0)
            .DefaultIfEmpty(null).Max();
    }

    public static bool ShouldWarnAfterLoad(IEnumerable<TrackInfo> tracks, string preset, bool selectedPresetFullyPrepared)
    {
        if (selectedPresetFullyPrepared) return false;
        var sourceBitrate = GetMaxSourceBitrateKbps(tracks);
        return sourceBitrate is > 0 && ExportPreset.Parse(preset).BitrateKbps > sourceBitrate.Value;
    }
}
