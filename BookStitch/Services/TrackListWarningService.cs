using BookStitch.Models;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class TrackListWarningService
{
    private static readonly Regex LeadingNumberRegex = new(@"^\s*(\d+)", RegexOptions.Compiled);

    // Warning-only evidence; do not widen the scanner, ordering or chapter parsers.
    private static readonly Regex PaddedFilenameSequenceRegex = new(
        @"^(?<number>0[0-9]{4,9})(?<tail>[\s._\-]+.+)$", RegexOptions.Compiled);

    public bool Apply(IList<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        // Only a proven observed inventory can resolve a tag conflict. Equality
        // on one row is not enough, and this never changes the working numbers.
        var resolvedFileNumbers = tracks.GroupBy(GetDiscWarningScope)
            .Where(HasResolvedFilenameInventory)
            .SelectMany(scope => scope)
            .ToHashSet();
        var changed = false;
        foreach (var track in tracks)
        {
            // Keep the legacy code readable, but retire it from current warning state.
            if (track.WarningCodes.HasFlag(TrackWarningCodes.MissingTagTitle))
            {
                track.WarningCodes &= ~TrackWarningCodes.MissingTagTitle;
                changed = true;
            }

            if (CodecIds.IsInvalid(track.Codec) || track.AudioValidationPassed == false)
            {
                changed |= !track.WarningCodes.HasFlag(TrackWarningCodes.InvalidAudio);
                track.WarningCodes |= TrackWarningCodes.InvalidAudio;
            }
            changed |= SetFileWarning(track, BuildFileWarning(track));
            changed |= SetChapterWarning(track, BuildInitialChapterWarning(track, resolvedFileNumbers.Contains(track)));
        }

        var sequenceNumbers = tracks.ToDictionary(track => track, ExtractSequenceNumber);
        // A complete consecutive filename inventory is independent evidence of the
        // sequence. A mistyped tag must not invent gaps or move an existing track.
        foreach (var scope in tracks.GroupBy(GetDiscWarningScope))
        {
            var rows = scope.ToList();
            var names = rows.Select(t => LeadingNumberRegex.Match(t.FileName ?? string.Empty))
                .Select(m => m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 0 ? (int?)n : null).ToList();
            var ordered = names.Where(n => n.HasValue).Select(n => n!.Value).Order().ToList();
            if (rows.Count >= 3 && ordered.Count == rows.Count &&
                rows.Select((row, i) => row.TrackNumber == names[i]).Count(matches => matches) >= rows.Count - 1 &&
                ordered.Zip(ordered.Skip(1), (a, b) => (long)b - a).All(delta => delta == 1))
                for (var i = 0; i < rows.Count; i++) sequenceNumbers[rows[i]] = names[i];
            else if (TryGetRepeatedTagFilenameSequence(rows, out var fileNumbers, out var sourceNumber))
            {
                for (var i = 0; i < rows.Count; i++) sequenceNumbers[rows[i]] = fileNumbers[i];

                // One representative metadata conflict per family, not one false
                // duplicate chapter per file. Keep independent warning codes intact.
                var representative = rows.Select((row, i) => (Row: row, Number: fileNumbers[i]))
                    .Where(item => !item.Row.IsExcluded && item.Number != sourceNumber)
                    .OrderBy(item => item.Number).FirstOrDefault();
                if (representative.Row is not null)
                    changed |= AddChapterWarning(representative.Row,
                        LocalizationText.Format("WarningTrackTagFileNumberMismatch", sourceNumber, representative.Number));
            }
        }
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

            var sourceSupportsMissingNumberWarning = SupportsMissingNumberWarning(
                discTrackList
                    .Select(track => sequenceNumbers[track])
                    .Where(number => number.HasValue)
                    .Select(number => number!.Value));
            int? previousNumber = null;
            foreach (var track in activeDiscTracks)
            {
                var number = sequenceNumbers[track];
                var warning = BuildSequenceWarning(
                    number,
                    previousNumber,
                    duplicateNumbers,
                    excludedNumbers,
                    sourceSupportsMissingNumberWarning);
                changed |= AddChapterWarning(track, warning);

                if (number.HasValue)
                    previousNumber = number.Value;
            }
        }

        changed |= ApplyInventoryGapWarnings(tracks, sequenceNumbers);
        return changed;
    }


    private static bool TryGetRepeatedTagFilenameSequence(
        IReadOnlyList<TrackInfo> rows, out int[] fileNumbers, out int sourceNumber)
    {
        fileNumbers = [];
        sourceNumber = 0;
        if (rows.Count < 3)
            return false;

        var source = rows[0].SourceTrackNumber ?? rows[0].SourceMetadataFields?.SourceTrackNumber;
        if (source is not > 0 || rows.Any(row => row.TrackNumber != source ||
            (row.SourceTrackNumber ?? row.SourceMetadataFields?.SourceTrackNumber) != source ||
            !string.Equals(row.RelativeFolder, rows[0].RelativeFolder, StringComparison.OrdinalIgnoreCase)))
            return false;

        var matches = rows.Select(row => PaddedFilenameSequenceRegex.Match(
            System.IO.Path.GetFileNameWithoutExtension(row.FileName ?? string.Empty))).ToArray();
        if (matches.Any(match => !match.Success ||
            match.Groups["number"].Length != matches[0].Groups["number"].Length ||
            !string.Equals(match.Groups["tail"].Value, matches[0].Groups["tail"].Value, StringComparison.OrdinalIgnoreCase)))
            return false;

        var numbers = new int[rows.Count];
        for (var i = 0; i < matches.Length; i++)
            if (!int.TryParse(matches[i].Groups["number"].Value, out numbers[i]) || numbers[i] <= 0)
                return false;

        var ordered = numbers.Order().ToArray();
        if (!ordered.Zip(ordered.Skip(1), (left, right) => (long)right - left).All(delta => delta == 1))
            return false;

        // 02001 can be CD 02 / track 001, but an ordinary year family must
        // not become sequence evidence. Require the known disc and a 001 start.
        if (numbers.Any(number => number is >= 1900 and <= 2199) &&
            !(rows[0].DiscNumber is int knownDisc && knownDisc > 0 && ordered[0] == (long)knownDisc * 1000 + 1 &&
              matches.All(match => match.Groups["number"].Length == 5 &&
                  match.Groups["number"].Value.StartsWith(knownDisc.ToString("00", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))))
            return false;

        fileNumbers = numbers;
        sourceNumber = source.Value;
        return true;
    }

    private static int? GetDiscWarningScope(TrackInfo track)
    {
        return track.DiscNumber is > 0 ? track.DiscNumber.Value : null;
    }

    private static string BuildFileWarning(TrackInfo track)
    {
        var codes = track.WarningCodes;
        var messages = new List<string>();

        if (codes.HasFlag(TrackWarningCodes.InvalidAudio) ||
            CodecIds.IsInvalid(track.Codec) ||
            track.AudioValidationPassed == false)
        {
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.InvalidAudio));
        }

        if (codes.HasFlag(TrackWarningCodes.MissingSource))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.MissingSource));

        if (codes.HasFlag(TrackWarningCodes.EmptySource))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.EmptySource));

        if (codes.HasFlag(TrackWarningCodes.EmptyAac))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.EmptyAac));

        if (codes.HasFlag(TrackWarningCodes.Undecodable))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.Undecodable));

        return JoinWarningMessages(messages);
    }

    private static bool HasResolvedFilenameInventory(IEnumerable<TrackInfo> scope)
    {
        var rows = scope.ToArray();
        if (rows.Length < 3)
            return false;

        var numbers = rows.Select(ExtractFilenameNumber).ToArray();
        if (numbers.Any(number => number is not > 0 || number is >= 1900 and <= 2199) ||
            rows.Where((row, index) => row.TrackNumber != numbers[index] ||
                !string.Equals(row.RelativeFolder, rows[0].RelativeFolder, StringComparison.OrdinalIgnoreCase)).Any())
            return false;

        var ordered = numbers.Select(number => number!.Value).Order().ToArray();
        return ordered.Zip(ordered.Skip(1), (left, right) => (long)right - left).All(delta => delta == 1);
    }

    private static int? ExtractFilenameNumber(TrackInfo track) => FolderScanner.TryExtractTrackNumber(
        System.IO.Path.GetFileNameWithoutExtension(track.FileName ?? string.Empty));

    private static string BuildInitialChapterWarning(TrackInfo track, bool filenameConflictResolved)
    {
        var codes = track.WarningCodes;
        var messages = new List<string>();

        if (codes.HasFlag(TrackWarningCodes.DuplicateChapterName))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.DuplicateChapterName));

        if (codes.HasFlag(TrackWarningCodes.MissingChapter))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.MissingChapter));

        if (codes.HasFlag(TrackWarningCodes.DuplicateChapter))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.DuplicateChapter));

        if (codes.HasFlag(TrackWarningCodes.CheckOrder))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder));

        if (codes.HasFlag(TrackWarningCodes.UnreadableTags))
            AddWarningMessage(messages, TrackWarningPresentation.Format(TrackWarningCodes.UnreadableTags));

        // An unresolved conflict still describes the immutable source tag, never
        // the working number, list position or generated chapter proposal.
        var sourceNumber = track.SourceTrackNumber ?? track.SourceMetadataFields?.SourceTrackNumber;
        var fileNumber = ExtractFilenameNumber(track);
        if (!filenameConflictResolved && sourceNumber is > 0 && fileNumber is > 0 && sourceNumber != fileNumber)
            AddWarningMessage(messages, LocalizationText.Format("WarningTrackTagFileNumberMismatch", sourceNumber.Value, fileNumber.Value));

        return JoinWarningMessages(messages);
    }

    private static string BuildSequenceWarning(
        int? currentNumber,
        int? previousNumber,
        ISet<int> duplicateNumbers,
        ISet<int> excludedNumbers,
        bool sourceSupportsMissingNumberWarning)
    {
        if (!currentNumber.HasValue)
            return string.Empty;

        if (duplicateNumbers.Contains(currentNumber.Value))
            return TrackWarningPresentation.Format(TrackWarningCodes.DuplicateChapter);

        if (previousNumber.HasValue)
        {
            var nextNumber = (long)previousNumber.Value + 1;
            if (currentNumber.Value == nextNumber)
                return string.Empty;

            if (sourceSupportsMissingNumberWarning &&
                currentNumber.Value > nextNumber)
            {
                return HasMissingActiveChapter(previousNumber.Value, currentNumber.Value, excludedNumbers)
                    ? TrackWarningPresentation.Format(TrackWarningCodes.MissingChapter)
                    : string.Empty;
            }

            if (currentNumber.Value <= previousNumber.Value)
                return TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder);
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

    private static bool ApplyInventoryGapWarnings(
        IList<TrackInfo> tracks,
        IReadOnlyDictionary<TrackInfo, int?> sequenceNumbers)
    {
        var changed = false;
        changed |= ApplyDiscInventoryGapWarnings(tracks);

        foreach (var discTracks in tracks.GroupBy(GetDiscWarningScope))
        {
            var discTrackList = discTracks.ToList();
            var numbers = discTrackList
                .Select(track => sequenceNumbers[track])
                .Where(number => number.HasValue)
                .Select(number => number!.Value)
                .Distinct()
                .Order()
                .ToList();

            if (!SupportsMissingNumberWarning(numbers))
                continue;

            foreach (var gap in FindInternalGaps(numbers))
            {
                var target = FindFirstActiveTrackAfterGap(discTrackList, sequenceNumbers, gap.End);
                if (target is not null)
                    changed |= AddChapterWarning(target, TrackWarningPresentation.Format(TrackWarningCodes.MissingChapter));
            }
        }

        return changed;
    }

    private static bool ApplyDiscInventoryGapWarnings(IList<TrackInfo> tracks)
    {
        var discNumbers = tracks
            .Select(track => track.DiscNumber)
            .Where(number => number is > 0)
            .Select(number => number!.Value)
            .Distinct()
            .Order()
            .ToList();

        if (discNumbers.Count < 2)
            return false;

        var changed = false;
        foreach (var gap in FindInternalGaps(discNumbers))
        {
            var target = tracks
                .Where(track => track.DiscNumber.HasValue && track.DiscNumber.Value > gap.End)
                .OrderBy(track => track.AutomaticOrderPosition ?? int.MaxValue)
                .ThenBy(track => track.Index)
                .FirstOrDefault(track => !track.IsExcluded)
                ?? tracks
                    .Where(track => track.DiscNumber.HasValue && track.DiscNumber.Value > gap.End)
                    .OrderBy(track => track.AutomaticOrderPosition ?? int.MaxValue)
                    .ThenBy(track => track.Index)
                    .FirstOrDefault();

            if (target is not null)
                changed |= AddChapterWarning(target, FormatDiscGapWarning(gap));
        }

        return changed;
    }

    private static TrackInfo? FindFirstActiveTrackAfterGap(
        IEnumerable<TrackInfo> tracks,
        IReadOnlyDictionary<TrackInfo, int?> sequenceNumbers,
        int gapEnd)
    {
        return tracks
            .Where(track => !track.IsExcluded &&
                            sequenceNumbers.TryGetValue(track, out var number) &&
                            number.HasValue &&
                            number.Value > gapEnd)
            .OrderBy(track => track.AutomaticOrderPosition ?? int.MaxValue)
            .ThenBy(track => track.Index)
            .FirstOrDefault();
    }

    private static bool SupportsMissingNumberWarning(IEnumerable<int> numbers)
    {
        var ordered = numbers.Distinct().Order().ToList();
        if (ordered.Zip(ordered.Skip(1), (left, right) => right - left).Any(delta => delta == 1))
            return true;

        return ordered.Count >= 2 &&
               ordered[0] == 1 &&
               ordered[^1] <= ordered.Count * 2;
    }

    private static IReadOnlyList<NumberGap> FindInternalGaps(IReadOnlyList<int> orderedDistinctNumbers)
    {
        var gaps = new List<NumberGap>();
        for (var index = 1; index < orderedDistinctNumbers.Count; index++)
        {
            var previous = orderedDistinctNumbers[index - 1];
            var current = orderedDistinctNumbers[index];
            if (current > previous + 1)
                gaps.Add(new NumberGap(previous + 1, current - 1));
        }

        return gaps;
    }

    private static string FormatDiscGapWarning(NumberGap gap)
    {
        return gap.Start == gap.End
            ? string.Format(LocalizationText.Get("WarningSingleDiscGap"), gap.Start)
            : string.Format(LocalizationText.Get("WarningDiscGapRange"), gap.Start, gap.End);
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

    private static bool AddChapterWarning(TrackInfo track, string value)
    {
        return SetChapterWarning(
            track,
            TrackInfo.AppendWarningMessage(track.ChapterWarningText, value));
    }

    private static string JoinWarningMessages(IEnumerable<string> messages)
    {
        var normalized = new List<string>();
        foreach (var message in messages)
            AddWarningMessage(normalized, message);

        return string.Join(TrackInfo.WarningMessageSeparator, normalized);
    }

    private static void AddWarningMessage(ICollection<string> messages, string? message)
    {
        var normalized = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            messages.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        messages.Add(normalized);
    }

    private sealed record NumberGap(int Start, int End);
}
