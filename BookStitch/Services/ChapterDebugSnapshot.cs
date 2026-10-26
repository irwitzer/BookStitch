namespace BookStitch.Services;

public sealed record ChapterDebugSnapshot(
    string ActiveMode,
    ResolvedChapterContent? Resolved,
    AnalyzedChapterCandidate? Winner,
    ChapterResolveCandidateSummary? RunnerUp,
    ChapterSourceAnalysisSummary? SourceSummary,
    string DominantGroupText,
    int DominantGroupCount);
