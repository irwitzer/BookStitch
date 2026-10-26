using System.Security.Cryptography;
using System.Text;

namespace BookStitch.Services;

public sealed class ChapterCandidateAnalysisService
{
    private readonly ChapterTechnicalNumberingAnalyzer _technicalNumberingAnalyzer;
    private readonly ChapterComparisonKeyBuilder _comparisonKeyBuilder;
    private readonly ChapterGenericClassifier _genericClassifier;
    private readonly ChapterNearDuplicateAnalyzer _nearDuplicateAnalyzer;
    private readonly ChapterScopeInputSignatureBuilder _signatureBuilder;
    private readonly ChapterAnalysisRules _rules;

    public int AnalysisRulesRevision => _rules.Revision;

    public ChapterCandidateAnalysisService()
        : this(
            new ChapterTechnicalNumberingAnalyzer(),
            new ChapterComparisonKeyBuilder(),
            new ChapterGenericClassifier(),
            new ChapterNearDuplicateAnalyzer(),
            new ChapterScopeInputSignatureBuilder(),
            new ChapterAnalysisRules())
    {
    }

    public ChapterCandidateAnalysisService(
        ChapterTechnicalNumberingAnalyzer technicalNumberingAnalyzer,
        ChapterComparisonKeyBuilder comparisonKeyBuilder,
        ChapterGenericClassifier genericClassifier,
        ChapterNearDuplicateAnalyzer nearDuplicateAnalyzer,
        ChapterScopeInputSignatureBuilder signatureBuilder,
        ChapterAnalysisRules rules)
    {
        _technicalNumberingAnalyzer = technicalNumberingAnalyzer ?? throw new ArgumentNullException(nameof(technicalNumberingAnalyzer));
        _comparisonKeyBuilder = comparisonKeyBuilder ?? throw new ArgumentNullException(nameof(comparisonKeyBuilder));
        _genericClassifier = genericClassifier ?? throw new ArgumentNullException(nameof(genericClassifier));
        _nearDuplicateAnalyzer = nearDuplicateAnalyzer ?? throw new ArgumentNullException(nameof(nearDuplicateAnalyzer));
        _signatureBuilder = signatureBuilder ?? throw new ArgumentNullException(nameof(signatureBuilder));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    public ChapterScopeAnalysisResult Analyze(ChapterAnalysisScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var technicalProjections = _technicalNumberingAnalyzer.Analyze(scope);
        var working = technicalProjections
            .Select(projection => new WorkingCandidate(
                projection,
                _comparisonKeyBuilder.Build(projection.AnalysisText)))
            .ToArray();

        foreach (var item in working)
        {
            item.IsGeneric = item.Projection.IsUsable &&
                             _genericClassifier.IsGeneric(item.Projection.AnalysisText, item.AnalysisComparisonKey);
        }

        var summaries = new List<ChapterSourceAnalysisSummary>();
        foreach (var sourceGroup in working.GroupBy(item => item.Projection.Candidate.Source))
            AnalyzeSource(sourceGroup.ToArray(), summaries);

        var analyzed = working.Select(item => item.ToResult(scope.ScopeId)).ToArray();
        var signature = _signatureBuilder.Build(scope, _rules.Revision);

        return new ChapterScopeAnalysisResult(
            scope.ScopeId,
            scope.Kind,
            signature,
            Array.AsReadOnly(analyzed),
            Array.AsReadOnly(summaries.OrderBy(summary => summary.Source).ToArray()));
    }

    private void AnalyzeSource(
        IReadOnlyList<WorkingCandidate> candidates,
        ICollection<ChapterSourceAnalysisSummary> summaries)
    {
        foreach (var item in candidates.Where(item => item.Projection.IsUsable))
        {
            item.ExactGroupId = BuildGroupId(
                item.Projection.Candidate.Source,
                item.AnalysisComparisonKey,
                "exact");
            item.AnalysisGroupId = item.ExactGroupId;
        }

        var meaningful = candidates
            .Where(item => item.Projection.IsUsable && !item.IsGeneric && item.AnalysisComparisonKey.Length > 0)
            .ToArray();
        var validCount = meaningful.Length;

        var exactGroups = meaningful
            .GroupBy(item => item.ExactGroupId, StringComparer.Ordinal)
            .Select(group => new ChapterNearDuplicateGroup(
                group.Key,
                group.First().AnalysisComparisonKey,
                group.Count()))
            .OrderBy(group => group.ExactGroupId, StringComparer.Ordinal)
            .ToArray();
        var fuzzyAssignments = _nearDuplicateAnalyzer.Analyze(exactGroups, validCount);

        foreach (var item in meaningful)
        {
            if (!fuzzyAssignments.TryGetValue(item.ExactGroupId, out var assignment))
                continue;

            item.AnalysisGroupId = assignment.AnalysisGroupId;
            item.WasFuzzyAbsorbed = assignment.WasAbsorbed;
            item.FuzzyEditDistance = assignment.EditDistance;
        }

        var analysisGroups = meaningful
            .GroupBy(item => item.AnalysisGroupId, StringComparer.Ordinal)
            .Select(group => new AnalysisGroup(group.Key, group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Id, StringComparer.Ordinal)
            .ToArray();
        var dominantCount = analysisGroups.FirstOrDefault()?.Count ?? 0;
        var deviationRatio = validCount == 0
            ? 0d
            : (double)(validCount - dominantCount) / validCount;
        var uniqueDominant = analysisGroups.Length == 1 ||
                             (analysisGroups.Length > 1 && analysisGroups[0].Count > analysisGroups[1].Count);
        var protectedDominantId = uniqueDominant &&
                                  ChapterAnalysisRules.HasProtectedDominance(dominantCount, validCount)
            ? analysisGroups[0].Id
            : null;
        var hasIndividualSignal = ChapterAnalysisRules.HasIndividualSignal(
            validCount - dominantCount,
            validCount);
        var countsByGroup = analysisGroups.ToDictionary(group => group.Id, group => group.Count, StringComparer.Ordinal);

        foreach (var item in candidates)
        {
            if (!item.Projection.IsUsable)
                continue;

            if (item.IsGeneric)
            {
                item.Quality = ChapterCandidateQuality.Generic;
                continue;
            }

            item.GroupSupportRatio = validCount == 0
                ? null
                : (double)countsByGroup[item.AnalysisGroupId] / validCount;
            item.DeviationRatio = deviationRatio;

            var isIndividual = hasIndividualSignal &&
                               (protectedDominantId is null ||
                                !string.Equals(item.AnalysisGroupId, protectedDominantId, StringComparison.Ordinal));
            item.Quality = isIndividual
                ? ChapterCandidateQuality.IndividualMeaningful
                : ChapterCandidateQuality.UniformMeaningful;
            item.EvidenceStrength = isIndividual ? deviationRatio : null;
        }

        summaries.Add(new ChapterSourceAnalysisSummary(
            candidates[0].Projection.Candidate.Source,
            candidates.Count,
            candidates.Count(item => item.Projection.IsUsable),
            candidates.Count(item => item.IsGeneric),
            validCount,
            dominantCount,
            deviationRatio));
    }

    private static string BuildGroupId(
        ChapterCandidateSource source,
        string comparisonKey,
        string prefix)
    {
        var input = source + "\n" + comparisonKey;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        return prefix + ":" + hash;
    }

    private sealed class WorkingCandidate
    {
        public WorkingCandidate(
            ChapterTechnicalNumberingProjection projection,
            string analysisComparisonKey)
        {
            Projection = projection;
            AnalysisComparisonKey = analysisComparisonKey;
        }

        public ChapterTechnicalNumberingProjection Projection { get; }
        public string AnalysisComparisonKey { get; }
        public bool IsGeneric { get; set; }
        public string ExactGroupId { get; set; } = string.Empty;
        public string AnalysisGroupId { get; set; } = string.Empty;
        public double? GroupSupportRatio { get; set; }
        public double? DeviationRatio { get; set; }
        public double? EvidenceStrength { get; set; }
        public ChapterCandidateQuality? Quality { get; set; }
        public bool WasFuzzyAbsorbed { get; set; }
        public int? FuzzyEditDistance { get; set; }

        public AnalyzedChapterCandidate ToResult(string scopeId) => new(
            Projection.Candidate.Source,
            Projection.Candidate.Provenance,
            Projection.Candidate.Normalization.RawText,
            Projection.Candidate.Normalization.NormalizedText,
            Projection.Candidate.Normalization.ComparisonKey,
            Projection.AnalysisText,
            AnalysisComparisonKey,
            Projection.OutputBaseText,
            Quality,
            Projection.IsUsable,
            IsGeneric,
            ExactGroupId,
            AnalysisGroupId,
            GroupSupportRatio,
            DeviationRatio,
            EvidenceStrength,
            WasFuzzyAbsorbed,
            FuzzyEditDistance,
            Projection.Evidence,
            scopeId,
            Projection.Candidate.MetadataOrigin);
    }

    private sealed record AnalysisGroup(string Id, int Count);
}
