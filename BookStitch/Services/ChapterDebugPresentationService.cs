using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>
/// Read-only projection over the last chapter result. It never runs chapter analysis or resolve.
/// </summary>
public sealed class ChapterDebugPresentationService
{
    public ChapterDebugSnapshot Create(
        ActiveChapterResult? activeResult,
        TrackInfo? selectedTrack)
    {
        var mode = activeResult?.Mode ?? ChapterSettingsService.ModeAuto;
        var engineResult = activeResult?.EngineResult;
        if (engineResult is null)
            return new ChapterDebugSnapshot(mode, null, null, null, null, string.Empty, 0);

        var snapshot = engineResult.AnalysisSnapshot;
        var provenance = SelectChapter(snapshot.EffectiveChapters, selectedTrack?.SourceId);
        if (provenance is null)
            return new ChapterDebugSnapshot(mode, null, null, null, null, string.Empty, 0);

        var resolved = engineResult.ResolveResult.Chapters.Single(
            chapter => string.Equals(
                chapter.EffectiveChapterId,
                provenance.EffectiveChapterId,
                StringComparison.Ordinal));
        var candidates = snapshot.CandidatesByEffectiveChapter[provenance.EffectiveChapterId];
        var winner = resolved.Source is { } winnerSource
            ? candidates.FirstOrDefault(candidate => candidate.Source == winnerSource)
            : null;
        var scope = snapshot.ScopeResults.Single(result =>
            string.Equals(result.ScopeId, resolved.ScopeId, StringComparison.Ordinal));
        var summary = winner is null
            ? null
            : scope.SourceSummaries.FirstOrDefault(item => item.Source == winner.Source);
        var dominantGroup = winner is null
            ? null
            : scope.Candidates
                .Where(candidate =>
                    candidate.Source == winner.Source &&
                    candidate.IsUsable &&
                    !candidate.IsGeneric)
                .GroupBy(candidate => candidate.AnalysisGroupId, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .FirstOrDefault();

        return new ChapterDebugSnapshot(
            mode,
            resolved,
            winner,
            resolved.RunnerUp,
            summary,
            dominantGroup?.First().AnalysisText ?? string.Empty,
            dominantGroup?.Count() ?? 0);
    }

    private static ChapterCandidateProvenance? SelectChapter(
        IReadOnlyList<ChapterCandidateProvenance> chapters,
        Guid? selectedSourceId)
    {
        if (selectedSourceId is { } sourceId && sourceId != Guid.Empty)
        {
            var selected = chapters
                .Where(chapter => chapter.SourceId == sourceId)
                .OrderBy(chapter => chapter.ScopeOrder)
                .FirstOrDefault();
            if (selected is not null)
                return selected;
        }

        return chapters.OrderBy(chapter => chapter.ScopeOrder).FirstOrDefault();
    }
}
