using BookStitch.Models;
using System.Windows;

namespace BookStitch.Services;

public sealed record ExportUiStateInput(
    ProjectPipelineState CurrentState,
    bool IsBusy,
    bool IsRunPaused,
    bool IsRunFailed,
    bool IsProjectExtensionRun,
    bool ManualMergeReviewNeedsReconversion,
    bool FfmpegAvailable,
    bool LoadedResumeProjectNeedsDiscImport,
    bool LoadedResumeProjectIsMp3Disc,
    bool LoadedResumeProjectIsAudioDisc,
    bool LoadedResumeProjectIsLocal,
    int TrackCount,
    bool HasOutputFolder,
    double ExportProgressPercent,
    bool HasSelectedResumeProject);

public sealed record ExportUiStateSnapshot(
    ProjectPipelineState Phase,
    bool CanSelectFolder,
    bool CanConfigureFfmpeg,
    bool CanEditTrackOrder,
    bool CanStartExport,
    string ExportButtonText,
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
    public ExportUiStateSnapshot Create(ExportUiStateInput input)
    {
        var phase = input.CurrentState;
        var isPreparing = phase == ProjectPipelineState.Preparing;
        var isReviewing = phase == ProjectPipelineState.ReviewBeforeMerge;
        var isCompleted = phase == ProjectPipelineState.Completed;
        var isPausablePhase = phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting;
        var isRunningPausablePhase = input.IsBusy && isPausablePhase;
        var isActivelyProcessing = isPausablePhase || phase == ProjectPipelineState.Merging;
        var isFailed = input.IsRunFailed && !input.IsBusy;
        var isPaused = input.IsRunPaused && isPausablePhase && !input.IsBusy && !isFailed;
        var isRestingProject = isPreparing || isReviewing || isCompleted;
        var canInteract = !input.IsBusy && isRestingProject && !isFailed;

        var hasExportableContent = input.LoadedResumeProjectNeedsDiscImport ||
            input.LoadedResumeProjectIsMp3Disc ||
            (input.TrackCount > 0 && input.HasOutputFolder);

        var canAddProjectSources = canInteract &&
            !input.ManualMergeReviewNeedsReconversion &&
            phase is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed &&
            (input.LoadedResumeProjectIsMp3Disc ||
             input.LoadedResumeProjectIsAudioDisc ||
             input.LoadedResumeProjectIsLocal);
        var addProjectSourcesButtonText = input.LoadedResumeProjectIsLocal
            ? "Quellen hinzufügen"
            : "Weitere CDs hinzufügen";

        var exportButtonText = phase switch
        {
            _ when isRunningPausablePhase => "Pause",
            _ when isPaused => "Weiter",
            _ when isPausablePhase => "Weiter",
            ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed =>
                input.ManualMergeReviewNeedsReconversion ? "Neu konvertieren" : "Zusammenfügen",
            ProjectPipelineState.Merging => "Zusammenfügen",
            _ when input.LoadedResumeProjectNeedsDiscImport => "Import fortsetzen",
            _ => "Projekt starten"
        };

        return new ExportUiStateSnapshot(
            Phase: phase,
            CanSelectFolder: !input.IsBusy && (isPreparing || isCompleted),
            CanConfigureFfmpeg: !input.IsBusy && (isPreparing || isCompleted),
            CanEditTrackOrder: canInteract && input.TrackCount > 0,
            CanStartExport: !isFailed && (isRunningPausablePhase || ((canInteract || isPaused) && input.FfmpegAvailable && hasExportableContent)),
            ExportButtonText: exportButtonText,
            SecondaryButtonText: isFailed
                ? "Projekt abbrechen"
                : isPaused
                    ? input.IsProjectExtensionRun ? "Stoppen" : "Projekt abbrechen"
                    : isReviewing || isCompleted
                        ? "Projekt schließen"
                        : "Abbrechen",
            SecondaryButtonVisibility: input.IsBusy && phase != ProjectPipelineState.Merging ? Visibility.Collapsed : Visibility.Visible,
            CanCancelExport: isFailed || (isActivelyProcessing && !isRunningPausablePhase) || isPaused || isReviewing || isCompleted,
            CanAddProjectSources: canAddProjectSources,
            AddProjectSourcesVisibility: canAddProjectSources ? Visibility.Visible : Visibility.Collapsed,
            AddProjectSourcesButtonText: addProjectSourcesButtonText,
            CanChooseOutputFolder: !isFailed && (canInteract || isPaused),
            CanChangeExportOptions: !isFailed && (canInteract || isPaused),
            CanChangeExportPreset: canInteract,
            CanOpenSettings: canInteract,
            CanChangeBookMetadata: !isFailed && (canInteract || isPaused),
            CanRefreshResumeProjects: !input.IsBusy && (isPreparing || isCompleted),
            CanInspectSelectedResumeProject: !input.IsBusy && (isPreparing || isCompleted) && input.HasSelectedResumeProject,
            ExportProgressVisibility: isActivelyProcessing || input.ExportProgressPercent > 0
                ? Visibility.Visible
                : Visibility.Collapsed);
    }
}
