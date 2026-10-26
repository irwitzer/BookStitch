using BookStitch.Models;

namespace BookStitch.Services;

public sealed record PipelineExecutionState(
    bool IsBusy,
    bool IsPaused,
    bool IsFailed,
    bool IsProjectExtensionRun);

public sealed record ExportUiStateSource(
    ProjectPipelineState CurrentState,
    PipelineExecutionState ExecutionState,
    bool ManualMergeReviewNeedsReconversion,
    bool ManualMergeReviewHasPreparedTracks,
    bool FfmpegAvailable,
    PipelineProjectState ProjectState,
    int TrackCount,
    string? OutputFolder,
    double ExportProgressPercent,
    object? SelectedResumeProject,
    bool CanCancelTrackAnalysis = false,
    bool IsPresetReconversionRun = false,
    bool HasPreparedResumeDiscPreview = false,
    bool PauseRequested = false,
    PipelineUiTransition? UiTransition = null,
    bool IsDiscImporting = false,
    bool IsExporting = false,
    bool ResumeDiscPreviewProcessingStarted = false);

public static class ExportUiStateInputService
{
    public static ExportUiStateInput Create(
        ExportUiStateSource source) =>
        new(
            source.CurrentState,
            source.ExecutionState.IsBusy,
            source.ExecutionState.IsPaused,
            source.ExecutionState.IsFailed,
            source.ExecutionState.IsProjectExtensionRun,
            source.ManualMergeReviewNeedsReconversion,
            source.ManualMergeReviewHasPreparedTracks,
            source.FfmpegAvailable,
            source.ProjectState.NeedsDiscImport,
            source.ProjectState.IsMp3Disc,
            source.ProjectState.IsAudioDisc,
            source.ProjectState.IsLocal,
            source.TrackCount,
            !string.IsNullOrWhiteSpace(source.OutputFolder),
            source.ExportProgressPercent,
            source.SelectedResumeProject is not null,
            source.CanCancelTrackAnalysis,
            source.IsPresetReconversionRun,
            source.HasPreparedResumeDiscPreview,
            source.PauseRequested,
            source.UiTransition,
            source.IsDiscImporting,
            source.IsExporting,
            source.ResumeDiscPreviewProcessingStarted);
}
