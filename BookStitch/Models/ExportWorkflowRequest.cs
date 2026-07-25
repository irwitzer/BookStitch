using BookStitch.Services;

namespace BookStitch.Models;

public sealed record ExportWorkflowRequest(
    ExportPlan Plan,
    string CurrentFolderPath,
    string FfmpegPath,
    ProjectSnapshotUiState Snapshot,
    FinalAudioTagData FinalTags,
    bool PauseBeforeMerge);
