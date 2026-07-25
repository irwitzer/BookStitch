using BookStitch.Models;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class TrackListWarningService
{
    private static readonly Regex LeadingNumberRegex = new(@"^\s*(\d+)", RegexOptions.Compiled);

    public bool Apply(IList<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var changed = false;
        foreach (var track in tracks)
        {
            changed |= SetFileWarning(track, BuildFileWarning(track));
            changed |= SetChapterWarning(track, string.Empty);
        }

        var sequenceNumbers = tracks.ToDictionary(track => track, ExtractSequenceNumber);
        var tracksByDisc = tracks.GroupBy(GetDiscWarningScope);

        foreach (var discTracks in tracksByDisc)
        {
            var discTrackList = discTracks.ToList();
            var activeDiscTracks = discTrackList.Where(track => !track.IsExcluded).ToList();
            var excludedNumbers = discTrackList
                .Where(track => track.IsExcluded)
                .Select(track => sequenceNumbers[track])
                .Where(number => number.HasValue)
                .Select(number => number!.Value)
                .ToHashSet();
            var duplicateNumbers = activeDiscTracks
                .Select(track => sequenceNumbers[track])
                .Where(number => number.HasValue)
                .GroupBy(number => number!.Value)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet();

            int? previousNumber = null;
            foreach (var track in activeDiscTracks)
            {
                var number = sequenceNumbers[track];
                var warning = BuildSequenceWarning(number, previousNumber, duplicateNumbers, excludedNumbers);
                changed |= SetChapterWarning(track, warning);

                if (number.HasValue)
                    previousNumber = number.Value;
            }
        }

        return changed;
    }


    private static int? GetDiscWarningScope(TrackInfo track)
    {
        return track.DiscNumber is > 0 ? track.DiscNumber.Value : null;
    }

    private static string BuildFileWarning(TrackInfo track)
    {
        var warning = track.Warning ?? string.Empty;

        if (Contains(warning, "Keine gültige Audiodatei") ||
            string.Equals(track.Codec, "Ungültig", StringComparison.OrdinalIgnoreCase) ||
            track.AudioValidationPassed == false)
        {
            return "Keine gültige Audiodatei";
        }

        if (Contains(warning, "Quelldatei fehlt"))
            return "Quelldatei fehlt";

        if (Contains(warning, "Quelldatei ist leer") || Contains(warning, "Quelldatei leer"))
            return "Quelldatei leer";

        if (Contains(warning, "AAC-Datei ist leer") || Contains(warning, "AAC-Datei leer"))
            return "AAC-Datei leer";

        if (Contains(warning, "Nicht dekodierbar"))
            return "Nicht dekodierbar";

        return string.Empty;
    }

    private static string BuildSequenceWarning(
        int? currentNumber,
        int? previousNumber,
        ISet<int> duplicateNumbers,
        ISet<int> excludedNumbers)
    {
        if (!currentNumber.HasValue)
            return string.Empty;

        if (duplicateNumbers.Contains(currentNumber.Value))
            return "Kapitel doppelt";

        if (previousNumber.HasValue)
        {
            if (currentNumber.Value == previousNumber.Value + 1)
                return string.Empty;

            if (currentNumber.Value > previousNumber.Value + 1)
                return HasMissingActiveChapter(previousNumber.Value, currentNumber.Value, excludedNumbers)
                    ? "Kapitel fehlt"
                    : string.Empty;

            if (currentNumber.Value <= previousNumber.Value)
                return "Sortierung prüfen";
        }

        return string.Empty;
    }

    private static bool HasMissingActiveChapter(int previousNumber, int currentNumber, ISet<int> excludedNumbers)
    {
        for (var number = previousNumber + 1; number < currentNumber; number++)
        {
            if (!excludedNumbers.Contains(number))
                return true;
        }

        return false;
    }

    private static int? ExtractSequenceNumber(TrackInfo track)
    {
        if (track.TrackNumber is > 0)
            return track.TrackNumber.Value;

        var match = LeadingNumberRegex.Match(track.TagTitle ?? string.Empty);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var tagNumber) && tagNumber > 0)
            return tagNumber;

        match = LeadingNumberRegex.Match(track.FileName ?? string.Empty);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var fileNumber) && fileNumber > 0)
            return fileNumber;

        return null;
    }

    private static bool SetFileWarning(TrackInfo track, string value)
    {
        if (string.Equals(track.FileWarningText, value, StringComparison.Ordinal))
            return false;

        track.FileWarningText = value;
        return true;
    }

    private static bool SetChapterWarning(TrackInfo track, string value)
    {
        if (string.Equals(track.ChapterWarningText, value, StringComparison.Ordinal))
            return false;

        track.ChapterWarningText = value;
        return true;
    }

    private static bool Contains(string value, string marker)
    {
        return value.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }
}
