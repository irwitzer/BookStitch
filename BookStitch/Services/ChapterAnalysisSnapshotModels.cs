using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ChapterCanonicalMetadataSnapshot(
    long ProjectMetadataRevision,
    string? TitleValue,
    MetadataOrigin TitleOrigin,
    string? AlbumValue,
    MetadataOrigin AlbumOrigin)
{
    public bool CustomAlbumUsesTitle { get; init; }
    public string? CustomAlbumValue => CustomAlbumUsesTitle ? TitleValue : AlbumValue;

    public static ChapterCanonicalMetadataSnapshot Capture(ProjectMetadata metadata,
        IReadOnlyDictionary<string, bool>? bindingSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var revision = metadata.Revision;
            var title = metadata.GetValue<string>(ProjectMetadataField.Title);
            var album = metadata.GetValue<string>(ProjectMetadataField.Album);

            if (metadata.Revision == revision)
            {
                return new ChapterCanonicalMetadataSnapshot(
                    revision,
                    title.Value,
                    title.Origin,
                    album.Value,
                    album.Origin)
                {
                    CustomAlbumUsesTitle = bindingSnapshot is not null &&
                        MetadataFieldBindingPreferencesService.ResolveIsEnabled(bindingSnapshot,
                            ProjectMetadataFieldBindingCatalog.TitleToAlbum)
                };
            }
        }

        throw new InvalidOperationException(
            "Canonical chapter metadata changed while its analysis snapshot was being captured.");
    }
}

public sealed record ChapterAnalysisSnapshot(
    Guid SnapshotId,
    int AnalysisRulesRevision,
    long ProjectMetadataRevision,
    IReadOnlyList<ChapterCandidateProvenance> EffectiveChapters,
    IReadOnlyList<ChapterScopeAnalysisResult> ScopeResults,
    IReadOnlyDictionary<string, IReadOnlyList<AnalyzedChapterCandidate>> CandidatesByEffectiveChapter,
    IReadOnlyDictionary<string, string> ScopeIdByEffectiveChapter);

public enum ChapterResolveDecisionReason
{
    HighestQuality,
    HigherIndividualEvidence,
    UserConfirmedCanonicalPreference,
    SourceAggregatedCanonicalPreference,
    SourcePriority,
    DeterministicTieBreak,
    Fallback
}

public sealed record ChapterResolveCandidateSummary(
    ChapterCandidateSource Source,
    string Text,
    ChapterCandidateQuality Quality,
    double? EvidenceStrength,
    string ScopeId,
    ChapterCandidateProvenance Provenance,
    MetadataOrigin MetadataOrigin = MetadataOrigin.None);

public sealed record ResolvedChapterContent(
    Guid SnapshotId,
    string EffectiveChapterId,
    string Text,
    ChapterCandidateSource? Source,
    ChapterCandidateQuality Quality,
    double? EvidenceStrength,
    string ScopeId,
    ChapterCandidateProvenance? Provenance,
    bool IsFallback,
    ChapterResolveDecisionReason DecisionReason,
    ChapterResolveCandidateSummary? RunnerUp,
    MetadataOrigin MetadataOrigin = MetadataOrigin.None);

public sealed record ChapterResolveResult(
    Guid SnapshotId,
    IReadOnlyList<ResolvedChapterContent> Chapters);
