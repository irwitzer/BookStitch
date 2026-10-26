using BookStitch.Services;

namespace BookStitch.Models;

public sealed record ExportWorkflowRequest(
    ExportPlan Plan,
    string CurrentFolderPath,
    string FfmpegPath,
    ProjectSnapshotUiState Snapshot,
    FinalAudioTagData FinalTags,
    ChapterSettings? ChapterSettings = null,
    bool LegacyUseLeadingZerosInChapterSuggestions = true,
    string FfprobePath = "",
    Func<TimeSpan?, int?, FinalAudioTagData>? CreateFinalTags = null,
    ChapterCanonicalMetadataSnapshot? CanonicalChapterMetadata = null,
    // One invocation only: set exclusively by an explicit Merge click, never by loading/resuming.
    bool ManualMergeRequested = false);
