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
        var codec = (track.Codec ?? "").Trim();
        var extension = (track.Extension ?? "").Trim().TrimStart('.');

        if (IsAlwaysConvertInput(codec, extension))
            return "Konvertieren";

        // Rohe .aac-Dateien sind kein M4A/M4B-Container. Sie werden daher nicht einfach kopiert,
        // sondern sauber neu in den gewählten AAC/M4A-Zielcontainer geschrieben.
        if (IsRawAacExtension(extension))
            return "Konvertieren";

        if (codec.Equals("AAC", StringComparison.OrdinalIgnoreCase))
        {
            if (IsM4aOrM4bExtension(extension))
                return AacMatchesPreset(track.BitrateKbps, track.Channels, preset) ? "Übernehmen" : "Konvertieren";

            return "Prüfen";
        }

        if (IsM4aOrM4bExtension(extension) && string.IsNullOrWhiteSpace(codec))
            return "Prüfen";

        return "Prüfen";
    }

    public static string DetermineProcessingAction(AudioProbeInfo probeInfo, ExportPreset preset)
    {
        if (!probeInfo.Success)
            return "Prüfen";

        var codec = NormalizeCodecName(probeInfo.CodecName);
        var extension = Path.GetExtension(probeInfo.FilePath).TrimStart('.');

        if (IsAlwaysConvertInput(codec, extension))
            return "Konvertieren";

        // Rohe .aac-Dateien bekommen immer einen echten M4A-Zwischenschritt.
        if (IsRawAacExtension(extension))
            return "Konvertieren";

        if (probeInfo.IsAac)
        {
            if (IsM4aOrM4bExtension(extension))
                return AacMatchesPreset(probeInfo.BitrateKbps, probeInfo.Channels, preset) ? "Übernehmen" : "Konvertieren";

            return "Prüfen";
        }

        return "Prüfen";
    }

    public static string BuildProcessingActionSummary(IEnumerable<TrackInfo> tracks)
    {
        var trackList = tracks as IReadOnlyCollection<TrackInfo> ?? tracks.ToList();

        if (trackList.Count == 0)
            return "noch keine Tracks";

        var counts = trackList
            .GroupBy(track => NormalizeProcessingAction(track.ProcessingAction))
            .ToDictionary(group => group.Key, group => group.Count());

        var parts = new List<string>();

        AddActionPart(parts, counts, "Übernehmen", "übernehmen");
        AddActionPart(parts, counts, "Konvertieren", "konvertieren");
        AddActionPart(parts, counts, "Prüfen", "prüfen");
        AddActionPart(parts, counts, "Offen", "offen");

        return parts.Count == 0
            ? "noch nicht bestimmt"
            : string.Join(", ", parts);
    }

    public static string BuildAudioDiscPipelineActionSummary(IEnumerable<TrackInfo> tracks, string? selectedPreset)
    {
        var trackList = tracks as IReadOnlyCollection<TrackInfo> ?? tracks.ToList();
        if (trackList.Count == 0)
            return "noch keine Tracks";

        var ripCount = trackList.Count(track =>
            string.Equals(track.ProcessingAction, "FLAC rippen", StringComparison.OrdinalIgnoreCase));
        var reuseCount = trackList.Count(track => track.HasReusableConvertedFile);
        var conversionCount = trackList.Count - ripCount - reuseCount;
        var parts = new List<string>();

        if (ripCount > 0)
            parts.Add($"{ripCount} FLAC rippen");

        if (conversionCount > 0)
        {
            var preset = string.IsNullOrWhiteSpace(selectedPreset)
                ? "AAC konvertieren"
                : selectedPreset.Trim();
            parts.Add($"{conversionCount} zu {preset} konvertieren");
        }

        if (reuseCount > 0)
            parts.Add($"{reuseCount} AAC wiederverwenden");

        return string.Join(", ", parts);
    }

    public static string NormalizeProcessingAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return "Offen";

        var normalized = action.Trim();

        if (normalized.Equals("Übernehmen", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Uebernehmen", StringComparison.OrdinalIgnoreCase))
        {
            return "Übernehmen";
        }

        if (normalized.Equals("Konvertieren", StringComparison.OrdinalIgnoreCase))
            return "Konvertieren";

        if (normalized.Equals("Prüfen", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Pruefen", StringComparison.OrdinalIgnoreCase))
        {
            return "Prüfen";
        }

        return normalized;
    }

    public static string FormatChannelLayout(int? channels)
    {
        return channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            null => "",
            _ => $"{channels} Kanäle"
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
               IsPcmCodec(normalizedCodec) ||
               normalizedExtension.Equals("MP3", StringComparison.OrdinalIgnoreCase) ||
               normalizedExtension.Equals("WAV", StringComparison.OrdinalIgnoreCase) ||
               normalizedExtension.Equals("FLAC", StringComparison.OrdinalIgnoreCase);
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

    private static bool AacMatchesPreset(int? bitrateKbps, int? channels, ExportPreset preset)
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
