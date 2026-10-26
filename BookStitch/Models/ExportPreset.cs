using System.Text.RegularExpressions;

namespace BookStitch.Models;

public sealed record ExportPreset(string DisplayName, int BitrateKbps, int Channels)
{
    public static IReadOnlyList<string> AvailableDisplayNames { get; } = Array.AsReadOnly(new[]
    {
        "AAC Mono 64 kbps", "AAC Mono 96 kbps", "AAC Stereo 96 kbps",
        "AAC Stereo 128 kbps", "AAC Stereo 160 kbps", "AAC Stereo 192 kbps", "AAC Stereo 256 kbps"
    });

    public const string DefaultDisplayName = "AAC Stereo 128 kbps";

    public static ExportPreset Parse(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value)
            ? DefaultDisplayName
            : value.Trim();

        var channels = text.Contains("Mono", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

        var bitrateMatch = Regex.Match(text, @"(\d+)\s*kbps", RegexOptions.IgnoreCase);
        var bitrate = bitrateMatch.Success && int.TryParse(bitrateMatch.Groups[1].Value, out var parsedBitrate)
            ? parsedBitrate
            : 128;

        bitrate = Math.Clamp(bitrate, 32, 320);

        return new ExportPreset(text, bitrate, channels);
    }

    public string GetFolderName()
    {
        var channelText = Channels == 1 ? "mono" : "stereo";
        return $"aac_{channelText}_{BitrateKbps}k";
    }
}
