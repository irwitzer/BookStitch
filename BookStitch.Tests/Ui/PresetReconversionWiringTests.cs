using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class PresetReconversionWiringTests
{
    [Fact]
    public void Starting_reconversion_fails_closed_without_a_rollback_baseline()
    {
        var workflow = SourceContract.ExtractBetween(
            LoadMainWindowSource(),
            "private async Task RunCurrentExportPlanAsync",
            "private async Task<bool> PrepareCurrentLocalProjectSourcesAsync");

        SourceContract.AssertInOrder(
            workflow,
            "CapturePresetReconversionPauseContext(presetReconversionProjectFolder)",
            "if (requiresPresetReconversionRollback && pendingPresetReconversionContext is null)",
            "AppDialogService.Error(",
            "return;");
    }

    [Fact]
    public void Capturing_reconversion_uses_the_resolved_previous_preset_for_the_rollback_boundary()
    {
        var capture = SourceContract.ExtractBetween(
            LoadMainWindowSource(),
            "private PresetReconversionRollbackContext? CapturePresetReconversionPauseContext",
            "private string ResolvePresetReconversionPreviousPreset");

        SourceContract.AssertInOrder(
            capture,
            "ResolvePresetReconversionPreviousPreset(projectFolder)",
            "CapturePresetReconversion(",
            "previousPreset,",
            "SelectedExportPreset");
        Assert.DoesNotContain(
            "string.Equals(SelectedExportPreset, previousPreset",
            capture,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Stopping_reconversion_restores_previous_preset_and_state()
    {
        var stop = SourceContract.ExtractBetween(
            LoadMainWindowSource(),
            "private async Task StopPausedPresetReconversionAsync",
            "private void BeginProjectExtensionPauseContext");

        SourceContract.AssertInOrder(
            stop,
            "RollbackPresetReconversion(context);",
            "SetSelectedExportPresetSilently(context.PreviousPreset);",
            "RefreshLoadedProjectTrackStateFromPersistedProject(context.Snapshot.ProjectFolder);",
            "new ProjectReviewStateSnapshot(context.PreviousPhase,",
            "RefreshPartialPresetProgressText();");
        Assert.DoesNotContain("SaveEditableProjectSnapshot(", stop, StringComparison.Ordinal);
        SourceContract.AssertInOrder(stop, "new ProjectReviewStateSnapshot(context.PreviousPhase,", "ClearPresetReconversionPauseContext();");
        Assert.DoesNotContain("PauseBeforeMergeForManualReview(", stop, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_keeps_the_global_parallel_setting_and_then_refreshes_preset_progress()
    {
        var resume = SourceContract.ExtractBetween(
            LoadMainWindowSource(),
            "private async Task LoadResumeProjectIntoWorkspaceAsync",
            "private void PublishLoadedProjectStatus");

        Assert.DoesNotContain("SetParallelJobsInput(", resume, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            resume,
            "PublishLoadedProjectStatus(loadResult);",
            "UpdateManualMergeReviewPresetState(forceRefresh: true, updateStatusText: false);",
            "RefreshPartialPresetProgressText();");
    }

    [Fact]
    public void Reconversion_owner_is_activated_before_any_preparation_and_reused_on_continuation()
    {
        var source = LoadMainWindowSource();
        var run = SourceContract.ExtractMember(source, "private async Task RunCurrentExportPlanAsync(");
        SourceContract.AssertInOrder(run, "CapturePresetReconversionPauseContext(",
            "if (requiresPresetReconversionRollback && pendingPresetReconversionContext is null)",
            "ActivatePresetReconversionPauseContext(pendingPresetReconversionContext);", "await PrepareCurrentLocalProjectSourcesAsync()");
        var capture = SourceContract.ExtractMember(source, "private PresetReconversionRollbackContext? CapturePresetReconversionPauseContext(");
        SourceContract.AssertInOrder(capture, "if (_presetReconversionRollbackContext is not null)", "return _presetReconversionRollbackContext;", "ResolvePresetReconversionPreviousPreset(");
        var export = SourceContract.ExtractMember(source, "private async Task ExportToAacWithWorkFolderAsync(");
        var resultHandling = export[export.LastIndexOf("            switch (result.Status)", StringComparison.Ordinal)..];
        var canceled = SourceContract.ExtractBetween(resultHandling, "case ExportWorkflowResultStatus.Canceled:", "case ExportWorkflowResultStatus.Failed:");
        SourceContract.AssertInOrder(canceled, "EnterPipelinePause(", "if (_isShutdownInProgress) return;", "ClearPresetReconversionPauseContext();");
        Assert.Contains("_presetReconversionRollbackContext is not null", SourceContract.ExtractBetween(source, "private ExportUiStateSnapshot ExportUiState =>", "public bool CanSelectFolder"));
        var internalSource = SourceContract.ExtractMember(source, "private bool IsCurrentLocalProjectSourceInternal()");
        Assert.Contains("LoadedProjectState.IsLocal && Tracks.Count > 0 && Tracks.All", internalSource);
        Assert.Contains("TrackPathService.IsPathInsideFolder(track.FilePath, originalsFolder)", internalSource);
    }

    private static string LoadMainWindowSource() =>
        RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

}
