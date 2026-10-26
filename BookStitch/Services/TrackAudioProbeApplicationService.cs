using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class TrackAudioProbeApplicationService
{
    public void ApplySuccessfulProbesBySourceId(
        IEnumerable<TrackInfo> tracks,
        IReadOnlyDictionary<Guid, AudioProbeInfo> probes,
        ExportPreset preset)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(probes);
        foreach (var track in tracks)
        {
            if (track.SourceId != Guid.Empty &&
                probes.TryGetValue(track.SourceId, out var probe) &&
                probe.HasPlausibleAudioProperties)
            {
                ApplySuccessfulProbe(track, probe, preset, markAudioValidationPassed: true);
            }
        }
    }

    public void ApplySuccessfulProbe(
        TrackInfo track,
        AudioProbeInfo probeInfo,
        ExportPreset preset,
        bool markAudioValidationPassed)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(probeInfo);

        if (markAudioValidationPassed)
            track.AudioValidationPassed = true;

        ApplyDuration(track, probeInfo.Duration);
        track.EmbeddedChapters = probeInfo.Chapters.ToList();
        track.BitrateKbps = probeInfo.BitrateKbps;
        track.Channels = probeInfo.Channels;
        track.SampleRateHz = probeInfo.SampleRateHz;
        track.AacConfigurationId = probeInfo.AacConfigurationId;
        track.AacOptimizationEligible = markAudioValidationPassed && probeInfo.SupportsAacStreamCopy &&
            (Path.GetExtension(probeInfo.FilePath).Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(probeInfo.FilePath).Equals(".m4b", StringComparison.OrdinalIgnoreCase));
        track.ChannelLayout = AudioProcessingService.FormatChannelLayout(probeInfo.Channels);
        track.Codec = AudioProcessingService.NormalizeCodecName(probeInfo.CodecName);
        track.ProcessingAction = AudioProcessingService.DetermineProcessingAction(probeInfo, preset, track.TargetSampleRateHz);

        if (track.WarningCodes == TrackWarningCodes.InvalidAudio &&
            (LegacyTrackWarningReader.IsSingleInvalidAudioWarning(track.Warning) ||
             string.Equals(track.Warning, LocalizationText.Get("WarningInvalidAudioSentence"), StringComparison.Ordinal)))
            track.Warning = string.Empty;
    }

    private static void ApplyDuration(TrackInfo track, TimeSpan? duration)
    {
        if (duration is null)
            return;

        track.Duration = FormatDuration(duration.Value);
        track.DurationTicks = duration.Value.Ticks;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");
    }
}
