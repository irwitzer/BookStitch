using BookStitch.Models;
using System.Windows;

namespace BookStitch.Services;

public sealed record ExportUiStateSnapshot(
    ProjectPipelineState Phase,
    bool CanSelectFolder,
    bool CanConfigureFfmpeg,
    bool CanEditTrackOrder,
    bool CanStartExport,
    string ExportButtonText,
    Visibility ExportButtonVisibility,
    string SecondaryButtonText,
    Visibility SecondaryButtonVisibility,
    bool CanCancelExport,
    bool CanAddProjectSources,
    Visibility AddProjectSourcesVisibility,
    string AddProjectSourcesButtonText,
    bool CanChooseOutputFolder,
    bool CanChangeExportOptions,
    bool CanChangeExportPreset,
    bool CanOpenSettings,
    bool CanChangeBookMetadata,
    bool CanRefreshResumeProjects,
    bool CanInspectSelectedResumeProject,
    Visibility ExportProgressVisibility);

public sealed class ExportUiStateService
{
    internal static bool IsPreparationProcessing(ProjectPipelineState phase, bool isExporting,
        bool isDiscImporting, bool isPaused, bool isFailed) =>
        (phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting) &&
        (isExporting || isDiscImporting) && !isPaused && !isFailed;

    public ExportUiStateSnapshot Create(ExportUiStateInput input)
    {
        var phase = input.CurrentState;
        var isPreparing = phase == ProjectPipelineState.Preparing;
        var isReviewing = phase == ProjectPipelineState.ReviewBeforeMerge;
        var isCompleted = phase == ProjectPipelineState.Completed;
        var isPausablePhase = phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting or ProjectPipelineState.Merging;
        var isActivelyProcessing = isPausablePhase || phase == ProjectPipelineState.Merging;
        var isRestingProject = isPreparing || isReviewing || isCompleted;
        var isFailed = input.IsRunFailed && !input.IsBusy && !isRestingProject;
        var isPaused = input.IsRunPaused && isPausablePhase && !input.IsBusy && !isFailed;
        var isRunningPausablePhase = isPausablePhase && (input.IsDiscImporting || input.IsExporting) && !isPaused && !isFailed;
        var canInteract = !input.IsBusy && isRestingProject && !isFailed;
        var canEditResumePreview = isPaused && phase == ProjectPipelineState.AcquiringSources &&
            input.HasPreparedResumeDiscPreview &&
            (input.LoadedResumeProjectIsMp3Disc || input.LoadedResumeProjectIsAudioDisc);

        var canConfigureResumePreview = canEditResumePreview && input.TrackCount > 0 &&
            !input.IsDiscImporting && !input.IsExporting && !input.CanCancelTrackAnalysis &&
            !input.IsProjectExtensionRun && !input.IsPresetReconversionRun &&
            !input.ResumeDiscPreviewProcessingStarted;

        var hasExportableContent = input.LoadedResumeProjectNeedsDiscImport ||
            input.LoadedResumeProjectIsMp3Disc ||
            (input.TrackCount > 0 && input.HasOutputFolder);

        var canAddProjectSources = canInteract &&
            (isReviewing || isCompleted) &&
            (input.LoadedResumeProjectIsMp3Disc ||
             input.LoadedResumeProjectIsAudioDisc ||
             input.LoadedResumeProjectIsLocal);
        var addProjectSourcesButtonText = input.LoadedResumeProjectIsLocal
            ? LocalizationText.Get("MainWindow.Action.AddSources")
            : LocalizationText.Get("MainWindow.Action.AddDiscs");

        var exportButtonText = phase switch
        {
            _ when isRunningPausablePhase => LocalizationText.Get("MainWindow.Action.Pause"),
            _ when isPaused => LocalizationText.Get("MainWindow.Action.Continue"),
            _ when isPausablePhase => LocalizationText.Get("MainWindow.Action.Continue"),
            ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed =>
                input.ManualMergeReviewNeedsReconversion
                    ? input.ManualMergeReviewHasPreparedTracks ? LocalizationText.Get("MainWindow.Action.Convert") : LocalizationText.Get("MainWindow.Action.Reconvert")
                    : LocalizationText.Get("MainWindow.Action.Merge"),
            ProjectPipelineState.Merging => LocalizationText.Get("MainWindow.Action.Merge"),
            _ when input.LoadedResumeProjectNeedsDiscImport => LocalizationText.Get("MainWindow.Action.ContinueImport"),
            _ when phase == ProjectPipelineState.Preparing && input.TrackCount > 0 => LocalizationText.Get("MainWindow.Action.PrepareProject"),
            _ => LocalizationText.Get("MainWindow.Action.StartProject")
        };
        var canStartExport = !isFailed && (isRunningPausablePhase || ((canInteract || isPaused || (isPausablePhase && !input.IsBusy)) && input.FfmpegAvailable && hasExportableContent));
        var canStopPausedRun = isPaused && (input.IsProjectExtensionRun || input.IsPresetReconversionRun);
        var canCancelExport = input.CanCancelTrackAnalysis || isFailed ||
            (phase == ProjectPipelineState.Merging && isPaused) || canStopPausedRun;

        var snapshot = new ExportUiStateSnapshot(
            Phase: phase,
            CanSelectFolder: !input.IsBusy && isRestingProject,
            CanConfigureFfmpeg: !input.IsBusy && (isPreparing || isCompleted),
            CanEditTrackOrder: (canInteract || canEditResumePreview) && input.TrackCount > 0,
            CanStartExport: canStartExport,
            ExportButtonText: exportButtonText,
            ExportButtonVisibility: !input.CanCancelTrackAnalysis && (isRunningPausablePhase || isPaused || hasExportableContent)
                ? Visibility.Visible
                : Visibility.Collapsed,
            SecondaryButtonText: isFailed
                ? LocalizationText.Get(input.IsProjectExtensionRun || input.IsPresetReconversionRun
                    ? "MainWindow.Action.Discard" : "MainWindow.Action.CloseProject")
                : isPaused
                    ? LocalizationText.Get(canStopPausedRun || phase == ProjectPipelineState.Merging ? "MainWindow.Action.Discard" : "MainWindow.Action.Save")
                    : isReviewing || isCompleted
                        ? LocalizationText.Get("MainWindow.Action.CloseProject")
                        : LocalizationText.Get("MainWindow.Action.Cancel"),
            SecondaryButtonVisibility: canCancelExport ? Visibility.Visible : Visibility.Collapsed,
            CanCancelExport: canCancelExport,
            CanAddProjectSources: canAddProjectSources,
            AddProjectSourcesVisibility: canAddProjectSources ? Visibility.Visible : Visibility.Collapsed,
            AddProjectSourcesButtonText: addProjectSourcesButtonText,
            CanChooseOutputFolder: !isFailed && (canInteract || isPaused),
            CanChangeExportOptions: !isFailed && (canInteract || isPaused),
            CanChangeExportPreset: canInteract,
            CanOpenSettings: canInteract || canConfigureResumePreview,
            CanChangeBookMetadata: canInteract || canConfigureResumePreview,
            CanRefreshResumeProjects: !input.IsBusy && isRestingProject,
            CanInspectSelectedResumeProject: !input.IsBusy && isRestingProject && input.HasSelectedResumeProject,
            ExportProgressVisibility: isActivelyProcessing || input.ExportProgressPercent > 0
                ? Visibility.Visible
                : Visibility.Collapsed);

        var transition = input.UiTransition ?? (input.PauseRequested
            ? new PipelineUiTransition(PipelineUiTransitionKind.Pausing, PipelineUiTransitionTarget.Primary) : null);
        if (transition is null) return snapshot;
        return snapshot with
        {
            CanSelectFolder = false, CanConfigureFfmpeg = false, CanEditTrackOrder = false,
            CanStartExport = false, CanCancelExport = false, CanAddProjectSources = false,
            CanChooseOutputFolder = false, CanChangeExportOptions = false, CanChangeExportPreset = false,
            CanOpenSettings = false, CanChangeBookMetadata = false, CanRefreshResumeProjects = false,
            CanInspectSelectedResumeProject = false,
            ExportButtonText = transition.Target == PipelineUiTransitionTarget.Primary ? transition.ButtonText : snapshot.ExportButtonText,
            ExportButtonVisibility = transition.Target == PipelineUiTransitionTarget.Primary ? Visibility.Visible : snapshot.ExportButtonVisibility,
            SecondaryButtonText = transition.Target == PipelineUiTransitionTarget.Secondary ? transition.ButtonText : snapshot.SecondaryButtonText,
            SecondaryButtonVisibility = transition.Target == PipelineUiTransitionTarget.Secondary ? Visibility.Visible : snapshot.SecondaryButtonVisibility
        };
    }
}
