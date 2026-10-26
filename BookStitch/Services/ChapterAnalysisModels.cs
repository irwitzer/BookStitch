using BookStitch.Models;

namespace BookStitch.Services;

public enum ChapterProjectSourceKind
{
    LocalFolder,
    Mp3Cd,
    AudioCd
}

public enum ChapterScopeKind
{
    FolderTrack,
    Mp3DiscTrack,
    AudioDisc,
    Embedded
}

public enum ChapterCandidateQuality
{
    Generic,
    UniformMeaningful,
    IndividualMeaningful
}

public enum ChapterTechnicalNumberingKind
{
    None,
    LeadingNumber,
    PureNumber,
    LongNumericIdentifier,
    StructuredNumericIdentifier
}

public sealed record ChapterCandidateProvenance(
    ChapterProjectSourceKind ProjectKind,
    string ProjectIdentity,
    Guid SourceId,
    string EffectiveChapterId,
    string ScopeProvenanceIdentity,
    int ScopeOrder,
    int CurrentOutputChapterIndex,
    int? SourceTrackNumber,
    int? SourceDiscNumber,
    int? EmbeddedOrdinal,
    long? EmbeddedStartTicks,
    long? EmbeddedEndTicks,
    bool HasMultipleEmbeddedChapters,
    int SourceOccurrence = 1);

public sealed record ProjectedChapterCandidate(
    ChapterCandidateSource Source,
    ChapterCandidateProvenance Provenance,
    ChapterNormalizationResult Normalization,
    MetadataOrigin MetadataOrigin = MetadataOrigin.None);

public sealed record ChapterCandidateProjectionResult(
    IReadOnlyList<ChapterCandidateProvenance> EffectiveChapters,
    IReadOnlyList<ProjectedChapterCandidate> Candidates);

public sealed record ChapterAnalysisScope(
    string ScopeId,
    ChapterScopeKind Kind,
    string ProvenanceIdentity,
    IReadOnlyList<ChapterCandidateProvenance> Members,
    IReadOnlyList<ProjectedChapterCandidate> Candidates);

public sealed record ChapterTechnicalNumberingEvidence(
    ChapterTechnicalNumberingKind Kind,
    bool FamilyDetected,
    int? RemovedNumber,
    string Format,
    bool OrdinalCorrelated,
    string Reason)
{
    public static ChapterTechnicalNumberingEvidence None { get; } = new(
        ChapterTechnicalNumberingKind.None,
        FamilyDetected: false,
        RemovedNumber: null,
        Format: string.Empty,
        OrdinalCorrelated: false,
        Reason: string.Empty);
}

public sealed record ChapterTechnicalNumberingProjection(
    ProjectedChapterCandidate Candidate,
    string AnalysisText,
    string OutputBaseText,
    bool IsUsable,
    ChapterTechnicalNumberingEvidence Evidence);

public sealed record AnalyzedChapterCandidate(
    ChapterCandidateSource Source,
    ChapterCandidateProvenance Provenance,
    string? RawText,
    string NormalizedText,
    string ComparisonKey,
    string AnalysisText,
    string AnalysisComparisonKey,
    string OutputBaseText,
    ChapterCandidateQuality? Quality,
    bool IsUsable,
    bool IsGeneric,
    string ExactGroupId,
    string AnalysisGroupId,
    double? GroupSupportRatio,
    double? DeviationRatio,
    double? EvidenceStrength,
    bool WasFuzzyAbsorbed,
    int? FuzzyEditDistance,
    ChapterTechnicalNumberingEvidence TechnicalNumberingEvidence,
    string ScopeId,
    MetadataOrigin MetadataOrigin = MetadataOrigin.None);

public sealed record ChapterSourceAnalysisSummary(
    ChapterCandidateSource Source,
    int CandidateCount,
    int UsableCandidateCount,
    int GenericCandidateCount,
    int ValidMeaningfulCount,
    int DominantCount,
    double DeviationRatio);

public sealed record ChapterScopeInputSignature(
    int AnalysisRulesRevision,
    string Hash);

public sealed record ChapterScopeAnalysisResult(
    string ScopeId,
    ChapterScopeKind ScopeKind,
    ChapterScopeInputSignature ScopeInputSignature,
    IReadOnlyList<AnalyzedChapterCandidate> Candidates,
    IReadOnlyList<ChapterSourceAnalysisSummary> SourceSummaries);

public sealed record ChapterAnalysisRules(int Revision = ChapterAnalysisRules.CurrentRevision)
{
    public const int CurrentRevision = 6;

    public static bool HasIndividualSignal(int deviationCount, int validCount) =>
        HasAtLeastPercent(deviationCount, validCount, 40);

    public static bool HasProtectedDominance(int dominantCount, int validCount) =>
        HasMoreThanPercent(dominantCount, validCount, 50);

    public static bool HasFuzzyDominance(int dominantCount, int validCount) =>
        HasAtLeastPercent(dominantCount, validCount, 60);

    public static int GetMaximumFuzzyAbsorptionCount(int validCount)
    {
        if (validCount < 0)
            throw new ArgumentOutOfRangeException(nameof(validCount));

        return (int)((long)validCount * 10 / 100);
    }

    private static bool HasAtLeastPercent(int count, int total, int percent) =>
        total > 0 && count >= 0 && (long)count * 100 >= (long)total * percent;

    private static bool HasMoreThanPercent(int count, int total, int percent) =>
        total > 0 && count >= 0 && (long)count * 100 > (long)total * percent;
}
