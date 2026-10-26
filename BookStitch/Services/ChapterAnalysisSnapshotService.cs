using System.Collections.ObjectModel;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed class ChapterAnalysisSnapshotService
{
    private readonly ChapterCandidateProjectionService _projectionService;
    private readonly ChapterScopeBuilder _scopeBuilder;
    private readonly ChapterCandidateAnalysisService _analysisService;

    public ChapterAnalysisSnapshotService()
        : this(
            new ChapterCandidateProjectionService(),
            new ChapterScopeBuilder(),
            new ChapterCandidateAnalysisService())
    {
    }

    public ChapterAnalysisSnapshotService(
        ChapterCandidateProjectionService projectionService,
        ChapterScopeBuilder scopeBuilder,
        ChapterCandidateAnalysisService analysisService)
    {
        _projectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
        _scopeBuilder = scopeBuilder ?? throw new ArgumentNullException(nameof(scopeBuilder));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
    }

    public ChapterAnalysisSnapshot Create(
        ChapterCandidateProjectionContext context,
        IReadOnlyList<EffectiveChapterInfo> effectiveChapters,
        IReadOnlyList<SourceMetadataSnapshot> sourceSnapshots,
        ProjectMetadata projectMetadata)
    {
        ArgumentNullException.ThrowIfNull(projectMetadata);
        var canonicalMetadata = ChapterCanonicalMetadataSnapshot.Capture(projectMetadata);
        return Create(context, effectiveChapters, sourceSnapshots, canonicalMetadata);
    }

    public ChapterAnalysisSnapshot Create(
        ChapterCandidateProjectionContext context,
        IReadOnlyList<EffectiveChapterInfo> effectiveChapters,
        IReadOnlyList<SourceMetadataSnapshot> sourceSnapshots,
        ChapterCanonicalMetadataSnapshot canonicalMetadata)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(effectiveChapters);
        ArgumentNullException.ThrowIfNull(sourceSnapshots);
        ArgumentNullException.ThrowIfNull(canonicalMetadata);

        var projection = _projectionService.Project(
            context,
            effectiveChapters,
            sourceSnapshots,
            canonicalMetadata);
        var orderedEffectiveChapters = projection.EffectiveChapters
            .OrderBy(chapter => chapter.ScopeOrder)
            .ThenBy(chapter => chapter.EffectiveChapterId, StringComparer.Ordinal)
            .ToArray();
        EnsureUniqueEffectiveChapterIds(orderedEffectiveChapters);

        var scopes = _scopeBuilder.Build(projection);
        var scopeIdByEffectiveChapter = BuildScopeMap(scopes, orderedEffectiveChapters);
        var scopeResults = scopes
            .Select(_analysisService.Analyze)
            .ToArray();
        var candidatesByEffectiveChapter = BuildCandidateMap(
            orderedEffectiveChapters,
            scopeResults,
            scopeIdByEffectiveChapter);

        return new ChapterAnalysisSnapshot(
            Guid.NewGuid(),
            _analysisService.AnalysisRulesRevision,
            canonicalMetadata.ProjectMetadataRevision,
            Array.AsReadOnly(orderedEffectiveChapters),
            Array.AsReadOnly(scopeResults),
            candidatesByEffectiveChapter,
            scopeIdByEffectiveChapter);
    }

    private static void EnsureUniqueEffectiveChapterIds(
        IReadOnlyList<ChapterCandidateProvenance> effectiveChapters)
    {
        var duplicate = effectiveChapters
            .GroupBy(chapter => chapter.EffectiveChapterId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Effective chapter identity '{duplicate.Key}' occurs more than once in one analysis snapshot.");
        }
    }

    private static IReadOnlyDictionary<string, string> BuildScopeMap(
        IReadOnlyList<ChapterAnalysisScope> scopes,
        IReadOnlyList<ChapterCandidateProvenance> effectiveChapters)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var scope in scopes)
        {
            foreach (var member in scope.Members)
            {
                if (!result.TryAdd(member.EffectiveChapterId, scope.ScopeId))
                {
                    throw new InvalidOperationException(
                        $"Effective chapter '{member.EffectiveChapterId}' belongs to more than one analysis scope.");
                }
            }
        }

        foreach (var chapter in effectiveChapters)
        {
            if (!result.ContainsKey(chapter.EffectiveChapterId))
            {
                throw new InvalidOperationException(
                    $"Effective chapter '{chapter.EffectiveChapterId}' has no analysis scope.");
            }
        }

        return new ReadOnlyDictionary<string, string>(result);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<AnalyzedChapterCandidate>> BuildCandidateMap(
        IReadOnlyList<ChapterCandidateProvenance> effectiveChapters,
        IReadOnlyList<ChapterScopeAnalysisResult> scopeResults,
        IReadOnlyDictionary<string, string> scopeIdByEffectiveChapter)
    {
        var candidates = scopeResults
            .SelectMany(result => result.Candidates)
            .GroupBy(candidate => candidate.Provenance.EffectiveChapterId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var result = new Dictionary<string, IReadOnlyList<AnalyzedChapterCandidate>>(StringComparer.Ordinal);

        foreach (var chapter in effectiveChapters)
        {
            var chapterCandidates = candidates.GetValueOrDefault(chapter.EffectiveChapterId) ?? [];
            var duplicateSource = chapterCandidates
                .GroupBy(candidate => candidate.Source)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateSource is not null)
            {
                throw new InvalidOperationException(
                    $"Effective chapter '{chapter.EffectiveChapterId}' has multiple '{duplicateSource.Key}' candidates.");
            }

            var expectedScopeId = scopeIdByEffectiveChapter[chapter.EffectiveChapterId];
            if (chapterCandidates.Any(candidate =>
                    !string.Equals(candidate.ScopeId, expectedScopeId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Effective chapter '{chapter.EffectiveChapterId}' contains a candidate from another scope.");
            }

            result.Add(
                chapter.EffectiveChapterId,
                Array.AsReadOnly(chapterCandidates
                    .OrderBy(candidate => candidate.Source.ToString(), StringComparer.Ordinal)
                    .ToArray()));
        }

        var unknownChapter = candidates.Keys.FirstOrDefault(key => !result.ContainsKey(key));
        if (unknownChapter is not null)
        {
            throw new InvalidOperationException(
                $"Analyzed candidate references unknown effective chapter '{unknownChapter}'.");
        }

        return new ReadOnlyDictionary<string, IReadOnlyList<AnalyzedChapterCandidate>>(result);
    }
}
