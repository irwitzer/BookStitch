using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ExportUiStateInput(
    ProjectPipelineState CurrentState,
    bool IsBusy,
    bool IsRunPaused,
    bool IsRunFailed,
    bool IsProjectExtensionRun,
    bool ManualMergeReviewNeedsReconversion,
    bool ManualMergeReviewHasPreparedTracks,
    bool FfmpegAvailable,
    bool LoadedResumeProjectNeedsDiscImport,
    bool LoadedResumeProjectIsMp3Disc,
    bool LoadedResumeProjectIsAudioDisc,
    bool LoadedResumeProjectIsLocal,
    int TrackCount,
    bool HasOutputFolder,
    double ExportProgressPercent,
    bool HasSelectedResumeProject,
    bool CanCancelTrackAnalysis = false,
    bool IsPresetReconversionRun = false,
    bool HasPreparedResumeDiscPreview = false,
    bool PauseRequested = false,
    PipelineUiTransition? UiTransition = null,
    bool IsDiscImporting = false,
    bool IsExporting = false,
    bool ResumeDiscPreviewProcessingStarted = false);
