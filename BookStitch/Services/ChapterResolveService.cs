using System.Globalization;
using System.Text;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed class ChapterResolveService
{
    private readonly ChapterSourcePolicy _sourcePolicy;
    private readonly Func<CultureInfo, string> _fallbackLabelProvider;

    public ChapterResolveService()
        : this(new ChapterSourcePolicy(), ChapterFallbackLabelProvider.GetLabel)
    {
    }

    public ChapterResolveService(
        ChapterSourcePolicy sourcePolicy,
        Func<CultureInfo, string> fallbackLabelProvider)
    {
        _sourcePolicy = sourcePolicy ?? throw new ArgumentNullException(nameof(sourcePolicy));
        _fallbackLabelProvider = fallbackLabelProvider ?? throw new ArgumentNullException(nameof(fallbackLabelProvider));
    }

    public ChapterResolveResult Resolve(
        ChapterAnalysisSnapshot snapshot,
        CultureInfo? uiCulture = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);

        var culture = uiCulture ?? CultureInfo.CurrentUICulture;
        var chapters = snapshot.EffectiveChapters
            .OrderBy(chapter => chapter.ScopeOrder)
            .ThenBy(chapter => chapter.EffectiveChapterId, StringComparer.Ordinal)
            .Select(chapter => ResolveChapter(snapshot, chapter, culture))
            .ToArray();

        return new ChapterResolveResult(snapshot.SnapshotId, Array.AsReadOnly(chapters));
    }

    private ResolvedChapterContent ResolveChapter(
        ChapterAnalysisSnapshot snapshot,
        ChapterCandidateProvenance chapter,
        CultureInfo culture)
    {
        var candidates = snapshot.CandidatesByEffectiveChapter[chapter.EffectiveChapterId];
        foreach (var candidate in candidates.Where(candidate => candidate.IsUsable))
            ValidateUsableCandidate(candidate);

        var ranked = candidates
            .Where(candidate => candidate.IsUsable)
            .OrderByDescending(candidate => GetQualityRank(candidate.Quality!.Value))
            .ThenByDescending(candidate => candidate.Quality == ChapterCandidateQuality.IndividualMeaningful
                ? candidate.EvidenceStrength!.Value
                : 0d)
            .ThenByDescending(GetUniformCanonicalAuthorityRank)
            .ThenBy(candidate => _sourcePolicy.GetTieBreakPriority(candidate.Source))
            .ThenBy(BuildStableCandidateIdentity, StringComparer.Ordinal)
            .ToArray();

        if (ranked.Length == 0)
        {
            var fallback = _fallbackLabelProvider(culture);
            if (string.IsNullOrWhiteSpace(fallback))
                throw new InvalidOperationException("The generic chapter fallback must not be empty.");

            return new ResolvedChapterContent(
                snapshot.SnapshotId,
                chapter.EffectiveChapterId,
                fallback,
                Source: null,
                ChapterCandidateQuality.Generic,
                EvidenceStrength: null,
                snapshot.ScopeIdByEffectiveChapter[chapter.EffectiveChapterId],
                Provenance: null,
                IsFallback: true,
                ChapterResolveDecisionReason.Fallback,
                RunnerUp: null,
                MetadataOrigin.None);
        }

        var winner = ranked[0];
        var runnerUp = ranked.Length > 1 ? ranked[1] : null;
        return new ResolvedChapterContent(
            snapshot.SnapshotId,
            chapter.EffectiveChapterId,
            winner.OutputBaseText,
            winner.Source,
            winner.Quality!.Value,
            winner.EvidenceStrength,
            winner.ScopeId,
            winner.Provenance,
            IsFallback: false,
            ResolveDecisionReason(winner, runnerUp),
            runnerUp is null ? null : ToSummary(runnerUp),
            winner.MetadataOrigin);
    }

    private void ValidateSnapshot(ChapterAnalysisSnapshot snapshot)
    {
        if (snapshot.SnapshotId == Guid.Empty)
            throw new InvalidOperationException("A chapter analysis snapshot requires a SnapshotId.");

        if (snapshot.AnalysisRulesRevision < 1)
            throw new InvalidOperationException("A chapter analysis snapshot requires a valid rules revision.");

        var effectiveIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chapter in snapshot.EffectiveChapters)
        {
            if (chapter is null || string.IsNullOrWhiteSpace(chapter.EffectiveChapterId))
                throw new InvalidOperationException("A snapshot contains an invalid effective chapter provenance.");

            if (!effectiveIds.Add(chapter.EffectiveChapterId))
                throw new InvalidOperationException($"Effective chapter '{chapter.EffectiveChapterId}' occurs more than once.");

            if (!snapshot.CandidatesByEffectiveChapter.ContainsKey(chapter.EffectiveChapterId) ||
                !snapshot.ScopeIdByEffectiveChapter.TryGetValue(chapter.EffectiveChapterId, out var scopeId) ||
                string.IsNullOrWhiteSpace(scopeId))
            {
                throw new InvalidOperationException(
                    $"Effective chapter '{chapter.EffectiveChapterId}' has incomplete snapshot mappings.");
            }
        }

        if (snapshot.CandidatesByEffectiveChapter.Keys.Any(key => !effectiveIds.Contains(key)) ||
            snapshot.ScopeIdByEffectiveChapter.Keys.Any(key => !effectiveIds.Contains(key)))
        {
            throw new InvalidOperationException("A snapshot mapping references an unknown effective chapter.");
        }

        var scopeCandidates = snapshot.ScopeResults.SelectMany(result => result.Candidates).ToArray();
        var scopeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scopeResult in snapshot.ScopeResults)
        {
            if (string.IsNullOrWhiteSpace(scopeResult.ScopeId) || !scopeIds.Add(scopeResult.ScopeId))
                throw new InvalidOperationException("A snapshot contains an invalid or duplicate scope result.");

            if (scopeResult.ScopeInputSignature.AnalysisRulesRevision != snapshot.AnalysisRulesRevision)
                throw new InvalidOperationException("A scope result uses a different analysis rules revision.");

            if (scopeResult.Candidates.Any(candidate =>
                    !string.Equals(candidate.ScopeId, scopeResult.ScopeId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Scope result '{scopeResult.ScopeId}' contains a candidate from another scope.");
            }
        }

        var mappedCandidates = snapshot.CandidatesByEffectiveChapter.Values.SelectMany(value => value).ToArray();
        if (scopeCandidates.Length != mappedCandidates.Length ||
            mappedCandidates.Any(candidate => !scopeCandidates.Contains(candidate)))
        {
            throw new InvalidOperationException("Resolve candidates do not match the analyzed scope results of this snapshot.");
        }

        foreach (var entry in snapshot.CandidatesByEffectiveChapter)
        {
            var duplicateSource = entry.Value
                .GroupBy(candidate => candidate.Source)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateSource is not null)
            {
                throw new InvalidOperationException(
                    $"Effective chapter '{entry.Key}' has multiple '{duplicateSource.Key}' candidates.");
            }

            var expectedScopeId = snapshot.ScopeIdByEffectiveChapter[entry.Key];
            foreach (var candidate in entry.Value)
            {
                if (candidate.Provenance is null ||
                    !string.Equals(candidate.Provenance.EffectiveChapterId, entry.Key, StringComparison.Ordinal) ||
                    !string.Equals(candidate.ScopeId, expectedScopeId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Effective chapter '{entry.Key}' contains incoherent candidate provenance.");
                }
            }
        }
    }

    private void ValidateUsableCandidate(AnalyzedChapterCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.OutputBaseText))
            throw new InvalidOperationException("A usable chapter candidate requires non-empty OutputBaseText.");

        if (candidate.Quality is null || !Enum.IsDefined(candidate.Quality.Value))
            throw new InvalidOperationException("A usable chapter candidate requires a valid quality.");

        if (!Enum.IsDefined(candidate.Source) || !_sourcePolicy.IsSelectable(candidate.Source))
            throw new InvalidOperationException($"Chapter source '{candidate.Source}' cannot participate in resolve.");

        if (!Enum.IsDefined(candidate.MetadataOrigin))
            throw new InvalidOperationException("A chapter candidate requires a valid metadata origin.");

        var provenance = candidate.Provenance;
        if (provenance is null ||
            !Enum.IsDefined(provenance.ProjectKind) ||
            provenance.SourceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(provenance.ProjectIdentity) ||
            string.IsNullOrWhiteSpace(provenance.EffectiveChapterId) ||
            string.IsNullOrWhiteSpace(provenance.ScopeProvenanceIdentity) ||
            provenance.ScopeOrder < 1 ||
            string.IsNullOrWhiteSpace(candidate.ScopeId))
        {
            throw new InvalidOperationException("A usable chapter candidate requires valid source provenance.");
        }

        if (candidate.Quality == ChapterCandidateQuality.IndividualMeaningful)
        {
            if (candidate.EvidenceStrength is not { } evidence ||
                !double.IsFinite(evidence) || evidence is < 0d or > 1d)
            {
                throw new InvalidOperationException(
                    "An IndividualMeaningful candidate requires finite EvidenceStrength between zero and one.");
            }
        }
        else if (candidate.EvidenceStrength is not null)
        {
            throw new InvalidOperationException(
                "EvidenceStrength is not applicable to UniformMeaningful or Generic candidates.");
        }
    }

    private ChapterResolveDecisionReason ResolveDecisionReason(
        AnalyzedChapterCandidate winner,
        AnalyzedChapterCandidate? runnerUp)
    {
        if (runnerUp is null || winner.Quality != runnerUp.Quality)
            return ChapterResolveDecisionReason.HighestQuality;

        if (winner.Quality == ChapterCandidateQuality.IndividualMeaningful &&
            winner.EvidenceStrength != runnerUp.EvidenceStrength)
        {
            return ChapterResolveDecisionReason.HigherIndividualEvidence;
        }

        if (winner.Quality == ChapterCandidateQuality.UniformMeaningful &&
            GetUniformCanonicalAuthorityRank(winner) != GetUniformCanonicalAuthorityRank(runnerUp))
        {
            return winner.MetadataOrigin == MetadataOrigin.User
                ? ChapterResolveDecisionReason.UserConfirmedCanonicalPreference
                : ChapterResolveDecisionReason.SourceAggregatedCanonicalPreference;
        }

        return _sourcePolicy.GetTieBreakPriority(winner.Source) !=
               _sourcePolicy.GetTieBreakPriority(runnerUp.Source)
            ? ChapterResolveDecisionReason.SourcePriority
            : ChapterResolveDecisionReason.DeterministicTieBreak;
    }

    private static int GetQualityRank(ChapterCandidateQuality quality) => quality switch
    {
        ChapterCandidateQuality.IndividualMeaningful => 3,
        ChapterCandidateQuality.UniformMeaningful => 2,
        ChapterCandidateQuality.Generic => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, null)
    };

    private static ChapterResolveCandidateSummary ToSummary(AnalyzedChapterCandidate candidate) => new(
        candidate.Source,
        candidate.OutputBaseText,
        candidate.Quality!.Value,
        candidate.EvidenceStrength,
        candidate.ScopeId,
        candidate.Provenance,
        candidate.MetadataOrigin);

    private static int GetUniformCanonicalAuthorityRank(AnalyzedChapterCandidate candidate)
    {
        if (candidate.Quality != ChapterCandidateQuality.UniformMeaningful ||
            candidate.Source is not (ChapterCandidateSource.CanonicalTitle or ChapterCandidateSource.CanonicalAlbum))
        {
            return 0;
        }

        return candidate.MetadataOrigin switch
        {
            MetadataOrigin.User => 2,
            MetadataOrigin.SourceAggregation => 1,
            _ => 0
        };
    }

    private static string BuildStableCandidateIdentity(AnalyzedChapterCandidate candidate)
    {
        var provenance = candidate.Provenance;
        var builder = new StringBuilder();
        builder.Append(candidate.Source.ToString());
        builder.Append('|');
        builder.Append(provenance.ProjectKind.ToString());
        builder.Append('|');
        builder.Append(provenance.ProjectIdentity);
        builder.Append('|');
        builder.Append(provenance.SourceId.ToString("N"));
        builder.Append('|');
        builder.Append(provenance.EffectiveChapterId);
        builder.Append('|');
        builder.Append(provenance.ScopeProvenanceIdentity);
        builder.Append('|');
        builder.Append(candidate.ScopeId);
        builder.Append('|');
        builder.Append(candidate.AnalysisComparisonKey);
        builder.Append('|');
        builder.Append(candidate.OutputBaseText);
        return builder.ToString();
    }
}
