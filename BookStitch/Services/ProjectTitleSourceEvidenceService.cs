using System.IO;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ProjectTitleSourceEvidence(
    string Value,
    int SourceCount,
    int CandidateCount = 0,
    ProjectTitleSourceEvidenceAlternative? RunnerUp = null);

public sealed record ProjectTitleSourceEvidenceAlternative(
    string Value,
    int SourceCount);

public sealed record ProjectTitleCandidateGroup(
    string Value,
    int SourceCount,
    int FirstIndex);

public sealed record ProjectTitleSourceEvidenceEvaluation(
    ProjectTitleSourceEvidence? Selected,
    ProjectTitleSourceEvidenceAlternative? StrongestRejected,
    int CandidateCount,
    string? RejectionReason);

public sealed class ProjectTitleSourceEvidenceService
{
    private readonly MetadataNormalizationService _normalization;

    public ProjectTitleSourceEvidenceService()
        : this(new MetadataNormalizationService())
    {
    }

    public ProjectTitleSourceEvidenceService(MetadataNormalizationService normalization)
    {
        _normalization = normalization ?? throw new ArgumentNullException(nameof(normalization));
    }

    public ProjectTitleSourceEvidence? SelectTitle(IReadOnlyList<SourceMetadataSnapshot> snapshots) =>
        EvaluateTitle(snapshots).Selected;

    public ProjectTitleSourceEvidenceEvaluation EvaluateTitle(IReadOnlyList<SourceMetadataSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var values = snapshots
            .Select(snapshot => snapshot.CanonicalFields.Title)
            .ToArray();

        return EvaluateTitleValues(values, snapshots.Count);
    }

    public ProjectTitleSourceEvidenceEvaluation EvaluateFilenameTitle(
        IReadOnlyList<string> sourceFileNames)
    {
        ArgumentNullException.ThrowIfNull(sourceFileNames);

        var values = sourceFileNames
            .Select(NormalizeFilenameTitleCandidate)
            .ToArray();

        return EvaluateTitleValues(values, sourceFileNames.Count);
    }

    public ProjectTitleSourceEvidence? SelectAlbumFallback(IReadOnlyList<SourceMetadataSnapshot> snapshots) =>
        EvaluateSimple(
            snapshots,
            ProjectMetadataField.Album,
            snapshot => snapshot.CanonicalFields.Album).Selected;

    public IReadOnlyList<ProjectTitleCandidateGroup> BuildTitleCandidateGroups(
        IReadOnlyList<SourceMetadataSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        return BuildTitleCandidateGroups(
            snapshots.Select(snapshot => snapshot.CanonicalFields.Title).ToArray(),
            snapshots.Count);
    }

    public IReadOnlyList<ProjectTitleCandidateGroup> BuildFilenameTitleCandidateGroups(
        IReadOnlyList<string> sourceFileNames)
    {
        ArgumentNullException.ThrowIfNull(sourceFileNames);

        return BuildTitleCandidateGroups(
            sourceFileNames.Select(NormalizeFilenameTitleCandidate).ToArray(),
            sourceFileNames.Count);
    }

    private ProjectTitleSourceEvidenceEvaluation EvaluateTitleValues(
        IReadOnlyList<string> values,
        int projectSourceCount)
    {
        var normalizedCandidateCount = values.Count(value =>
            _normalization.NormalizeForComparison(ProjectMetadataField.Title, value).Length > 0);

        var groups = BuildTitleCandidateGroups(values, projectSourceCount);
        return EvaluateGroups(groups, normalizedCandidateCount, projectSourceCount);
    }

    private IReadOnlyList<ProjectTitleCandidateGroup> BuildTitleCandidateGroups(
        IReadOnlyList<string> values,
        int projectSourceCount)
    {
        var normalized = values
            .Select((value, index) => new RawTitleCandidate(
                _normalization.NormalizeText(value),
                index))
            .Where(candidate => candidate.Value.Length > 0)
            .Select(candidate => candidate with
            {
                ComparisonKey = _normalization.NormalizeForComparison(
                    ProjectMetadataField.Title,
                    candidate.Value)
            })
            .Where(candidate => candidate.ComparisonKey.Length > 0)
            .ToArray();

        if (normalized.Length == 0)
            return [];

        var numberedGroups = normalized
            .Select(candidate => TrySplitLeadingSequence(candidate, projectSourceCount))
            .Where(candidate => candidate is not null)
            .Cast<NumberedTitleCandidate>()
            .GroupBy(candidate => candidate.RemainderComparisonKey, StringComparer.Ordinal)
            .Where(group => IsTrackSequenceGroup(group))
            .ToDictionary(group => group.Key, StringComparer.Ordinal);

        var collapsedIndexes = numberedGroups.Values
            .SelectMany(group => group.Select(candidate => candidate.Index))
            .ToHashSet();

        return numberedGroups.Values
            .Select(group => new ProjectTitleCandidateGroup(
                group.OrderBy(candidate => candidate.Index).First().Remainder,
                group.Count(),
                group.Min(candidate => candidate.Index)))
            .Concat(
                normalized
                    .Where(candidate => !collapsedIndexes.Contains(candidate.Index))
                    .GroupBy(candidate => candidate.ComparisonKey, StringComparer.Ordinal)
                    .Select(group => new ProjectTitleCandidateGroup(
                        group.OrderBy(candidate => candidate.Index).First().Value,
                        group.Count(),
                        group.Min(candidate => candidate.Index))))
            .OrderByDescending(group => group.SourceCount)
            .ThenBy(group => group.FirstIndex)
            .ThenBy(group => group.Value, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private ProjectTitleSourceEvidenceEvaluation EvaluateGroups(
        IReadOnlyList<ProjectTitleCandidateGroup> groups,
        int candidateCount,
        int projectSourceCount)
    {
        if (groups.Count == 0 || candidateCount == 0)
            return new ProjectTitleSourceEvidenceEvaluation(null, null, 0, "Keine verwertbare Evidence vorhanden.");

        var strongest = groups[0];

        if (projectSourceCount <= 1)
        {
            return new ProjectTitleSourceEvidenceEvaluation(
                CreateEvidence(strongest, groups, candidateCount),
                null,
                candidateCount,
                null);
        }

        if (candidateCount < 2)
        {
            return new ProjectTitleSourceEvidenceEvaluation(
                null,
                new ProjectTitleSourceEvidenceAlternative(strongest.Value, strongest.SourceCount),
                candidateCount,
                "Nur eine Title-Evidence in einem Mehrdatei-Projekt; keine projektweite Dominanz.");
        }

        if (groups.Count == 1)
        {
            return new ProjectTitleSourceEvidenceEvaluation(
                CreateEvidence(strongest, groups, candidateCount),
                null,
                candidateCount,
                null);
        }

        if (strongest.SourceCount * 3 >= candidateCount * 2)
        {
            return new ProjectTitleSourceEvidenceEvaluation(
                CreateEvidence(strongest, groups, candidateCount),
                null,
                candidateCount,
                null);
        }

        return new ProjectTitleSourceEvidenceEvaluation(
            null,
            new ProjectTitleSourceEvidenceAlternative(strongest.Value, strongest.SourceCount),
            candidateCount,
            "Keine direkte Title-Gruppe erreicht die erforderliche Zwei-Drittel-Dominanz.");
    }

    private ProjectTitleSourceEvidenceEvaluation EvaluateSimple(
        IReadOnlyList<SourceMetadataSnapshot> snapshots,
        ProjectMetadataField field,
        Func<SourceMetadataSnapshot, string> valueSelector)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(valueSelector);

        var candidates = snapshots
            .Select((snapshot, index) => new Candidate(
                _normalization.NormalizeText(valueSelector(snapshot)),
                index))
            .Where(item => item.Value.Length > 0)
            .Select(item => item with
            {
                ComparisonKey = _normalization.NormalizeForComparison(field, item.Value)
            })
            .Where(item => item.ComparisonKey.Length > 0)
            .ToArray();

        if (candidates.Length == 0)
            return new ProjectTitleSourceEvidenceEvaluation(null, null, 0, "Keine verwertbare Evidence vorhanden.");

        var groups = candidates
            .GroupBy(candidate => candidate.ComparisonKey, StringComparer.Ordinal)
            .Select(group => new ProjectTitleCandidateGroup(
                group.OrderBy(candidate => candidate.Index).First().Value,
                group.Count(),
                group.Min(candidate => candidate.Index)))
            .OrderByDescending(group => group.SourceCount)
            .ThenBy(group => group.FirstIndex)
            .ToArray();

        return EvaluateGroups(groups, candidates.Length, snapshots.Count);
    }

    private string NormalizeFilenameTitleCandidate(string? sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
            return string.Empty;

        var fileName = Path.GetFileName(sourceFileName);
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return _normalization.NormalizeFolderCandidate(withoutExtension);
    }

    private NumberedTitleCandidate? TrySplitLeadingSequence(
        RawTitleCandidate candidate,
        int sourceCount)
    {
        var value = candidate.Value;
        var digitCount = 0;
        while (digitCount < value.Length && char.IsDigit(value[digitCount]))
            digitCount++;

        if (digitCount == 0 || digitCount > 6 || digitCount >= value.Length)
            return null;

        var numericToken = value[..digitCount];
        if (!int.TryParse(numericToken, out var number) || number <= 0)
            return null;

        var separatorIndex = digitCount;
        while (separatorIndex < value.Length && IsSequenceSeparator(value[separatorIndex]))
            separatorIndex++;

        var hasSeparator = separatorIndex > digitCount;
        if (separatorIndex >= value.Length)
            return null;

        if (!hasSeparator && numericToken[0] != '0')
            return null;

        var remainder = _normalization.NormalizeText(value[separatorIndex..]);
        var key = _normalization.NormalizeForComparison(ProjectMetadataField.Title, remainder);
        if (remainder.Length == 0 || key.Length == 0)
            return null;

        return new NumberedTitleCandidate(
            candidate.Index,
            number,
            numericToken,
            hasSeparator,
            remainder,
            key,
            sourceCount);
    }

    private static bool IsTrackSequenceGroup(IEnumerable<NumberedTitleCandidate> candidates)
    {
        var items = candidates.ToArray();
        if (items.Length < 2)
            return false;

        var numbers = items
            .Select(item => item.Number)
            .Distinct()
            .OrderBy(number => number)
            .ToArray();

        if (numbers.Length < 2)
            return false;

        var largestGap = numbers
            .Zip(numbers.Skip(1), (left, right) => right - left)
            .DefaultIfEmpty(1)
            .Max();

        if (largestGap > 2)
            return false;

        var firstToken = items.OrderBy(item => item.Index).First().NumericToken;
        var startsLikeOrdinaryTrackSequence = numbers[0] <= 2;
        var isZeroPaddedTechnicalSequence =
            firstToken.Length >= 2 &&
            items.All(item => item.NumericToken.Length == firstToken.Length) &&
            items.All(item => item.NumericToken[0] == '0');

        if (!startsLikeOrdinaryTrackSequence && !isZeroPaddedTechnicalSequence)
            return false;

        if (!isZeroPaddedTechnicalSequence &&
            numbers[^1] > Math.Max(12, items[0].ProjectSourceCount + 10))
        {
            return false;
        }

        return true;
    }

    private static bool IsSequenceSeparator(char value) =>
        char.IsWhiteSpace(value) || value is '.' or '_' or '-' or '–' or '—';

    private static ProjectTitleSourceEvidence CreateEvidence(
        ProjectTitleCandidateGroup strongest,
        IReadOnlyList<ProjectTitleCandidateGroup> groups,
        int candidateCount) =>
        new(
            strongest.Value,
            strongest.SourceCount,
            candidateCount,
            groups.Count > 1
                ? new ProjectTitleSourceEvidenceAlternative(groups[1].Value, groups[1].SourceCount)
                : null);

    private sealed record RawTitleCandidate(
        string Value,
        int Index,
        string ComparisonKey = "");

    private sealed record NumberedTitleCandidate(
        int Index,
        int Number,
        string NumericToken,
        bool HasSeparator,
        string Remainder,
        string RemainderComparisonKey,
        int ProjectSourceCount);

    private sealed record Candidate(
        string Value,
        int Index,
        string ComparisonKey = "");
}
