using BookStitch.Models;

namespace BookStitch.Services;

public sealed class TrackIssueSummaryService
{
    public TrackIssueSummary Create(IEnumerable<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var errorCount = 0;
        var hintCount = 0;

        foreach (var track in tracks)
        {
            if (track is null)
                continue;

            if (IsError(track))
            {
                errorCount++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(track.Warning))
                hintCount++;
        }

        return new TrackIssueSummary(errorCount, hintCount);
    }

    private static bool IsError(TrackInfo track)
    {
        return track.AudioValidationPassed == false ||
               string.Equals(track.Codec, "Ungültig", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(track.ProcessingAction, "Ungültig", StringComparison.OrdinalIgnoreCase);
    }
}

public readonly record struct TrackIssueSummary(int ErrorCount, int HintCount)
{
    public string ToDisplayText() => $"Fehler: {ErrorCount} | Hinweise: {HintCount}";
}
