using BookStitch.Models;

namespace BookStitch.Services;

public sealed class TrackIssueSummaryService
{
    public TrackIssueSummary Create(IEnumerable<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var errorCount = 0;
        foreach (var track in tracks)
        {
            if (track is null)
                continue;

            if (IsError(track))
                errorCount++;
        }

        return new TrackIssueSummary(errorCount, 0);
    }

    private static bool IsError(TrackInfo track)
    {
        return track.AudioValidationPassed == false ||
               track.WarningCodes.HasFlag(TrackWarningCodes.InvalidAudio) ||
               CodecIds.IsInvalid(track.Codec) ||
               ProcessingActionIds.Normalize(track.ProcessingAction) == ProcessingActionIds.Invalid;
    }
}

public readonly record struct TrackIssueSummary(int ErrorCount, int HintCount)
{
    public string ToDisplayText() => LocalizationText.Format("MainWindow.Issues.ErrorCount", ErrorCount);
}
