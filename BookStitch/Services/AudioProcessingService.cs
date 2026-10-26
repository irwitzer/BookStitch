using BookStitch.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BookStitch.Services;

public static class AudioProcessingService
{
    public static string DetermineProcessingAction(TrackInfo track, ExportPreset preset)
    {
        if (!track.AllowAacReuse) return ProcessingActionIds.Convert;
        var codec = (track.Codec ?? "").Trim();
        var extension = (track.Extension ?? "").Trim().TrimStart('.');

        if (IsAlwaysConvertInput(codec, extension))
            return ProcessingActionIds.Convert;

        // Rohe .aac-Dateien sind kein M4A/M4B-Container. Sie werden daher nicht einfach kopiert,
        // sondern sauber neu in den gewählten AAC/M4A-Zielcontainer geschrieben.
        if (IsRawAacExtension(extension))
            return ProcessingActionIds.Convert;

        if (codec.Equals("AAC", StringComparison.OrdinalIgnoreCase))
        {
            if (IsM4aOrM4bExtension(extension))
                return ((track.SampleRateHz is null || (FolderSampleRateService.CanVote(track.SampleRateHz) && track.SampleRateHz == track.TargetSampleRateHz)) && AacMatchesPreset(track.BitrateKbps, track.Channels, preset)) ? ProcessingActionIds.Reuse : ProcessingActionIds.Convert;

            return ProcessingActionIds.Inspect;
        }

        if (IsM4aOrM4bExtension(extension) && string.IsNullOrWhiteSpace(codec))
            return ProcessingActionIds.Inspect;

        if (IsM4aOrM4bExtension(extension) && !string.IsNullOrWhiteSpace(codec))
            return ProcessingActionIds.Convert;

        return ProcessingActionIds.Inspect;
    }

    public static string DetermineProcessingAction(AudioProbeInfo probeInfo, ExportPreset preset, int targetSampleRateHz = 44100)
    {
        if (!probeInfo.Success)
            return ProcessingActionIds.Inspect;

        var codec = NormalizeCodecName(probeInfo.CodecName);
        var extension = Path.GetExtension(probeInfo.FilePath).TrimStart('.');

        if (IsAlwaysConvertInput(codec, extension))
            return ProcessingActionIds.Convert;

        // Rohe .aac-Dateien bekommen immer einen echten M4A-Zwischenschritt.
        if (IsRawAacExtension(extension))
            return ProcessingActionIds.Convert;

        if (probeInfo.IsAac)
        {
            if (IsM4aOrM4bExtension(extension))
                return (FolderSampleRateService.CanVote(probeInfo.SampleRateHz) && probeInfo.SampleRateHz == targetSampleRateHz && AacMatchesPreset(probeInfo.BitrateKbps, probeInfo.Channels, preset)) ? ProcessingActionIds.Reuse : ProcessingActionIds.Convert;

            return ProcessingActionIds.Inspect;
        }

        if (IsM4aOrM4bExtension(extension) && !string.IsNullOrWhiteSpace(codec))
            return ProcessingActionIds.Convert;

        return ProcessingActionIds.Inspect;
    }

    public static string BuildProcessingActionSummary(IEnumerable<TrackInfo> tracks)
    {
        var trackList = tracks as IReadOnlyCollection<TrackInfo> ?? tracks.ToList();

        if (trackList.Count == 0)
            return LocalizationText.Get("SummaryNoTracks");

        var counts = trackList
            .GroupBy(track => NormalizeProcessingAction(track.ProcessingAction))
            .ToDictionary(group => group.Key, group => group.Count());

        var parts = new List<string>();

        AddActionPart(parts, counts, ProcessingActionIds.Reuse, LocalizationText.Get("SummaryReuse"));
        AddActionPart(parts, counts, ProcessingActionIds.Convert, LocalizationText.Get("SummaryConvert"));
        AddActionPart(parts, counts, ProcessingActionIds.Inspect, LocalizationText.Get("SummaryInspect"));
        AddActionPart(parts, counts, ProcessingActionIds.Pending, LocalizationText.Get("SummaryPending"));

        return parts.Count == 0
            ? LocalizationText.Get("SummaryUndetermined")
            : string.Join(", ", parts);
    }

    public static string BuildAudioDiscPipelineActionSummary(IEnumerable<TrackInfo> tracks, string? selectedPreset)
    {
        var trackList = tracks as IReadOnlyCollection<TrackInfo> ?? tracks.ToList();
        if (trackList.Count == 0)
            return LocalizationText.Get("SummaryNoTracks");

        var ripCount = trackList.Count(track =>
            ProcessingActionIds.Normalize(track.ProcessingAction) == ProcessingActionIds.RipFlac);
        var reuseCount = trackList.Count(track => track.HasReusableConvertedFile);
        var conversionCount = trackList.Count - ripCount - reuseCount;
        var parts = new List<string>();

        if (ripCount > 0)
            parts.Add(string.Format(LocalizationText.Get("SummaryRipFlac"), ripCount));

        if (conversionCount > 0)
        {
            var preset = string.IsNullOrWhiteSpace(selectedPreset)
                ? LocalizationText.Get("SummaryDefaultPreset")
                : selectedPreset.Trim();
            parts.Add(string.Format(LocalizationText.Get("SummaryConvertToPreset"), conversionCount, preset));
        }

        if (reuseCount > 0)
            parts.Add(string.Format(LocalizationText.Get("SummaryReuseAac"), reuseCount));

        return string.Join(", ", parts);
    }

    public static string NormalizeProcessingAction(string? action) => ProcessingActionIds.Normalize(action);

    public static string FormatChannelLayout(int? channels)
    {
        return channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            null => "",
            _ => LocalizationText.Format("Audio.Channels", channels)
        };
    }

    public static string NormalizeCodecName(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName))
            return "";

        return codecName.Trim().ToUpperInvariant();
    }

    private static bool IsAlwaysConvertInput(string? codec, string? extension)
    {
        var normalizedCodec = (codec ?? "").Trim();
        var normalizedExtension = (extension ?? "").Trim().TrimStart('.');

        return normalizedCodec.Equals("MP3", StringComparison.OrdinalIgnoreCase) ||
               normalizedCodec.Equals("FLAC", StringComparison.OrdinalIgnoreCase) ||
               normalizedCodec.Equals("WMA", StringComparison.OrdinalIgnoreCase) ||
               normalizedCodec.Equals("VORBIS", StringComparison.OrdinalIgnoreCase) ||
               normalizedCodec.Equals("OPUS", StringComparison.OrdinalIgnoreCase) ||
               IsPcmCodec(normalizedCodec) ||
               normalizedExtension.Equals("MP3", StringComparison.OrdinalIgnoreCase) ||
               normalizedExtension.Equals("WAV", StringComparison.OrdinalIgnoreCase) ||
               normalizedExtension.Equals("FLAC", StringComparison.OrdinalIgnoreCase) ||
               normalizedExtension.Equals("WMA", StringComparison.OrdinalIgnoreCase) ||
               normalizedExtension.Equals("OGG", StringComparison.OrdinalIgnoreCase) ||
               normalizedExtension.Equals("OGA", StringComparison.OrdinalIgnoreCase) ||
               normalizedExtension.Equals("OPUS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsM4aOrM4bExtension(string? extension)
    {
        var normalizedExtension = (extension ?? "").Trim().TrimStart('.');

        return normalizedExtension.Equals("M4A", StringComparison.OrdinalIgnoreCase) ||
               normalizedExtension.Equals("M4B", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRawAacExtension(string? extension)
    {
        var normalizedExtension = (extension ?? "").Trim().TrimStart('.');
        return normalizedExtension.Equals("AAC", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPcmCodec(string? codec)
    {
        var normalized = (codec ?? "").Trim();

        return normalized.StartsWith("PCM", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("PCM_", StringComparison.OrdinalIgnoreCase);
    }

    public static bool AacMatchesPreset(int? bitrateKbps, int? channels, ExportPreset preset)
    {
        if (channels is null || channels.Value != preset.Channels)
            return false;

        if (bitrateKbps is null || bitrateKbps.Value <= 0)
            return false;

        const int toleranceKbps = 10;
        return Math.Abs(bitrateKbps.Value - preset.BitrateKbps) <= toleranceKbps;
    }

    private static void AddActionPart(List<string> parts, IReadOnlyDictionary<string, int> counts, string key, string label)
    {
        if (!counts.TryGetValue(key, out var count) || count <= 0)
            return;

        parts.Add($"{count} {label}");
    }
}
