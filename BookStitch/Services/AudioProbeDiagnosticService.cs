using BookStitch.Models;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

/// <summary>Only unambiguously attributed attached-picture decoder errors are non-audio diagnostics.</summary>
public static class AudioProbeDiagnosticService
{
    private static readonly Regex DecoderPrefix = new(@"^\[(?<codec>[a-zA-Z0-9_]+) @ (?:0x)?[0-9a-fA-F]+\] .+", RegexOptions.CultureInvariant);

    public static bool HasAudioErrors(AudioProbeInfo probe, string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return false;
        if (!probe.Success || probe.HasNonCoverVideo) return true;
        foreach (var line in stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = DecoderPrefix.Match(line);
            if (!match.Success || !probe.AttachedPictureCodecs.Contains(match.Groups["codec"].Value, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
