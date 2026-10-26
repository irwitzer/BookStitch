using BookStitch.Models;

namespace BookStitch.Services;

public sealed class ScalarCandidatePresentationService
{
    private readonly MetadataNormalizationService _normalization;

    public ScalarCandidatePresentationService()
        : this(new MetadataNormalizationService())
    {
    }

    public ScalarCandidatePresentationService(MetadataNormalizationService normalization)
    {
        _normalization = normalization ?? throw new ArgumentNullException(nameof(normalization));
    }

    public string ResolveAutomaticWinnerValue(
        ProjectMetadataField field,
        MetadataValue<string> canonical,
        IReadOnlyList<MetadataFieldDecisionDiagnostic> decisions,
        IEnumerable<ScalarSourceMetadataAlternative> candidates,
        string? rememberedValue = null)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(candidates);

        var remembered = NormalizeValue(rememberedValue);
        if (remembered.Length > 0)
            return remembered;

        var decision = decisions.FirstOrDefault(item => item.Field == field);
        if (decision is not null)
        {
            if (decision.Origin == MetadataOrigin.User)
            {
                var runnerUp = decision.RunnerUp;
                if (runnerUp is not null && IsAutomaticEvidence(runnerUp.EvidenceType))
                {
                    var value = NormalizeValue(runnerUp.Value);
                    if (value.Length > 0)
                        return value;
                }
            }
            else if (IsAutomaticOrigin(decision.Origin))
            {
                var value = NormalizeValue(decision.CanonicalValue);
                if (value.Length > 0)
                    return value;
            }
            else if (decision.Winner is not null && IsAutomaticEvidence(decision.Winner.EvidenceType))
            {
                var value = NormalizeValue(decision.Winner.Value);
                if (value.Length > 0)
                    return value;
            }
        }

        if (IsAutomaticOrigin(canonical.Origin))
        {
            var value = NormalizeValue(canonical.Value);
            if (value.Length > 0)
                return value;
        }

        return NormalizeValue(candidates.FirstOrDefault(candidate => candidate.IsAutomaticWinner)?.DisplayValue);
    }

    public IReadOnlyList<ScalarSourceMetadataAlternative> ApplyAutomaticWinnerAndSort(
        ProjectMetadataField field,
        MetadataValue<string> canonical,
        IEnumerable<ScalarSourceMetadataAlternative> candidates,
        string? automaticWinnerValue)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(candidates);

        var automaticKey = _normalization.NormalizeForComparison(field, automaticWinnerValue);
        var currentUserKey = canonical.Origin == MetadataOrigin.User
            ? _normalization.NormalizeForComparison(field, canonical.Value)
            : string.Empty;

        return candidates
            .Select((candidate, index) =>
            {
                var key = _normalization.NormalizeForComparison(field, candidate.DisplayValue);
                var normalizedCandidate = candidate with
                {
                    IsAutomaticWinner = automaticKey.Length > 0 &&
                                        string.Equals(key, automaticKey, StringComparison.Ordinal)
                };

                var rank = currentUserKey.Length > 0 && string.Equals(key, currentUserKey, StringComparison.Ordinal)
                    ? 0
                    : normalizedCandidate.IsAutomaticWinner
                        ? 1
                        : 2;

                return new RankedCandidate(normalizedCandidate, rank, index);
            })
            .OrderBy(item => item.Rank)
            .ThenByDescending(item => item.Candidate.SourceCount)
            .ThenBy(item => item.Index)
            .Select(item => item.Candidate)
            .ToArray();
    }

    private string NormalizeValue(string? value) => _normalization.NormalizeText(value);

    private static bool IsAutomaticOrigin(MetadataOrigin origin) => origin is
        MetadataOrigin.SourceAggregation or
        MetadataOrigin.FolderStructure or
        MetadataOrigin.SingleFileName or
        MetadataOrigin.Derived;

    private static bool IsAutomaticEvidence(MetadataDecisionEvidenceType evidenceType) => evidenceType is not
        MetadataDecisionEvidenceType.UserOverride and not
        MetadataDecisionEvidenceType.ExistingCanonical;

    private sealed record RankedCandidate(
        ScalarSourceMetadataAlternative Candidate,
        int Rank,
        int Index);
}
