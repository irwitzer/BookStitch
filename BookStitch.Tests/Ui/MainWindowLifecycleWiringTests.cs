using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class MainWindowLifecycleWiringTests
{
    [Fact]
    public void Mp3_session_pause_keeps_rows_after_drain_before_any_stop_save_or_extension_rollback()
    {
        var import = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> ImportMp3DiscProjectAsync");
        var cancellation = SourceContract.ExtractMember(import, "async Task<bool> HandleImportCancellationAsync");
        SourceContract.AssertInOrder(cancellation, "await preparedLifecycle.DrainAsync();",
            "if (_presentationState.PauseRequested)", "EnterPipelinePause(", "return true;",
            "if (extensionSnapshot is null)", "CancelMp3DiscImportAndShowResumeDialogAsync(",
            "ProjectExtensionRollbackService.Rollback(");
        var pause = SourceContract.ExtractBetween(cancellation,
            "if (_presentationState.PauseRequested)", "if (extensionSnapshot is null)");
        Assert.DoesNotContain("Tracks.ReplaceAll", pause);
        Assert.DoesNotContain("BuildFromProjectFolder", pause);
        SourceContract.AssertInOrder(import, "CreatePreviewTracksFromKnownFiles(", "AppendDiscPreviewTracks(",
            "projectFolder, sourceFolder, totalDiscs, publishStatus: false", "UpdateIndexes();");
    }

    [Fact]
    public void Loaded_preset_advisory_runs_once_after_successful_analysis_and_reuses_the_existing_choices()
    {
        var folder = SourceContract.ExtractMember(MainWindowSource, "private async Task LoadFolderAsync(");
        SourceContract.AssertInOrder(folder, "var loadSucceeded = false;", "await EnrichTracksWithFfprobeAsync(",
            "localImportPerformanceScope.Complete();", "loadSucceeded = true;", "IsBusy = false;",
            "if (loadSucceeded && analysisCancellationScope == TrackAnalysisCancellationScope.LocalFolderImport)",
            "ConfirmLoadedPresetBitrateIfNeeded();");
        Assert.Equal(1, folder.Split("ConfirmLoadedPresetBitrateIfNeeded();").Length - 1);

        var resume = SourceContract.ExtractMember(MainWindowSource, "private async Task LoadResumeProjectIntoWorkspaceAsync(");
        SourceContract.AssertInOrder(resume, "await EnrichResumeTracksWithFfprobeAsync(",
            "UpdateManualMergeReviewPresetState(forceRefresh: true", "loadSucceeded = true;", "IsBusy = false;",
            "if (loadSucceeded)", "ConfirmLoadedPresetBitrateIfNeeded();", "await ShowCurrentProjectSetupDialogAsync();");
        Assert.Equal(1, resume.Split("ConfirmLoadedPresetBitrateIfNeeded();").Length - 1);
        foreach (var signature in new[] { "private async Task StartMp3DiscProjectAsync(", "private async Task<bool> TryShowAudioDiscContentsAsync(" })
        {
            var source = SourceContract.ExtractMember(MainWindowSource, signature);
            SourceContract.AssertInOrder(source, "UpdateFinalStatus(Tracks.Count);", "ConfirmLoadedPresetBitrateIfNeeded();",
                "CreateProjectSetupDialog(new ProjectSetupDialogRequest(");
            Assert.Equal(1, source.Split("ConfirmLoadedPresetBitrateIfNeeded();").Length - 1);
        }
        var confirm = SourceContract.ExtractMember(MainWindowSource, "private void ConfirmLoadedPresetBitrateIfNeeded()");
        Assert.Contains("!string.IsNullOrWhiteSpace(_lockedDiscResumePreset)", confirm);
        SourceContract.AssertInOrder(confirm, "GetPreparationProgress(included, requestedPreset)", "preparation.PendingCount == 0",
            "ShouldWarnAfterLoad(Tracks, requestedPreset, fullyPrepared)", "ConfirmPresetBitrateIfNeeded(requestedPreset)",
            "ApplyExportPresetSelectionToUi(confirmedPreset)");
        Assert.DoesNotContain("RunCurrentExportPlanAsync", confirm);
        Assert.DoesNotContain("ManualMergeRequested", confirm);
        Assert.DoesNotContain("MarkConversionCompleted", confirm);
    }

    [Fact]
    public void Drive_circle_is_visible_for_pending_mp3_and_audio_discs_before_project_start()
    {
        var visibility = SourceContract.ExtractMember(MainWindowSource, "public Visibility DriveCircleVisibility");
        Assert.Contains("LoadedProjectState.IsMp3Disc", visibility);
        Assert.Contains("LoadedProjectState.IsAudioDisc", visibility);
        Assert.Contains("_pendingAudioDisc is not null", visibility);
        Assert.Contains("!string.IsNullOrWhiteSpace(_pendingDiscProjectSourceFolder)", visibility);
        Assert.Contains("? Visibility.Visible : Visibility.Collapsed", visibility);
        Assert.Contains("_pendingDiscProjectSourceFolder = sourceFolder;\n            NotifyExportUiStateChanged();", MainWindowSource.Replace("\r\n", "\n"));
        var notify = SourceContract.ExtractMember(MainWindowSource, "private void NotifyExportUiStateChanged()");
        Assert.Contains("OnPropertyChanged(nameof(DriveCircleVisibility))", notify);
    }

    [Fact]
    public void Local_extension_does_not_claim_all_presets_prepared_after_only_new_sources_finish()
    {
        var local = SourceContract.ExtractMember(MainWindowSource, "private async Task AddMoreLocalSourcesAsync()");
        Assert.Contains("MarkConversionPreparationStarted(manifest)", local);
        Assert.DoesNotContain("MarkConversionCompleted(manifest)", local);
        SourceContract.AssertInOrder(local, "CaptureAdditionalSourcesBaseline(projectFolder)",
            "WorkManifestService.LoadOrCreate(", "TrackPreparedStateRefreshService.RefreshForCurrentPreset(");
    }

    [Fact]
    public void Drive_circle_controls_share_global_owner_and_main_settings_lock_without_folder_row()
    {
        var main = RepositorySource.Read(["BookStitch", "MainWindow.xaml"]);
        Assert.Contains("IsEnabled=\"{Binding CanOpenSettings}\"", main);
        Assert.Contains("Visibility=\"{Binding DriveCircleVisibility}\"", main);
        Assert.Contains("IsChecked=\"{Binding DriveCircleEnabled, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", main);
        var property = SourceContract.ExtractMember(MainWindowSource, "public bool DriveCircleEnabled");
        Assert.Contains("GlobalSettingsManager.DriveCircleEnabled = value", property);
        Assert.Contains("DriveCircleChanged += GlobalSettingsManager_DriveCircleChanged", MainWindowSource);
        Assert.Contains("DriveCircleChanged -= GlobalSettingsManager_DriveCircleChanged", MainWindowSource);
        var dialog = RepositorySource.Read(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml.cs"]);
        Assert.Contains("request.SourceKind == ProjectSetupSourceKind.Folder ? Visibility.Collapsed : Visibility.Visible", dialog);
        Assert.Contains("_settingsManager.DriveCircleEnabled = DriveCircleCheckBox.IsChecked == true", dialog);
        Assert.Contains("DriveCircleChanged += GlobalSettingsManager_DriveCircleChanged", dialog);
        Assert.Contains("DriveCircleChanged -= GlobalSettingsManager_DriveCircleChanged", dialog);
        var settings = RepositorySource.Read(["BookStitch", "Controls", "SettingsDriveControl.xaml.cs"]);
        Assert.Contains("_settingsManager!.DriveCircleEnabled =", settings);
        Assert.Contains("DriveCircleChanged += GlobalSettingsManager_DriveCircleChanged", settings);
        Assert.Contains("DriveCircleChanged -= GlobalSettingsManager_DriveCircleChanged", settings);
        var add = RepositorySource.Read(["BookStitch", "Dialog", "AddMoreDiscsDialog.xaml.cs"]);
        Assert.DoesNotContain("ShowError(LocalizationText.Get(\"Disc.AddMore.SelectDrive\"))", add);
        Assert.Contains("DriveList.SelectedItem is not DiscDriveSelectionPresentation", add);
    }

    [Fact]
    public void Merge_pause_and_abort_keep_the_merge_phase_and_use_existing_review_boundary()
    {
        var pause = SourceContract.ExtractMember(MainWindowSource, "private async Task RequestPipelinePauseAsync(");
        Assert.DoesNotContain("? ProjectPipelineState.Converting", pause);
        var cancel = SourceContract.ExtractMember(MainWindowSource, "private async void CancelExport_Click(");
        SourceContract.AssertInOrder(cancel, "IsPipelinePaused && ProjectReviewState.Phase == ProjectPipelineState.Merging",
            "DiscardPausedMerge()", "return;");
        var discard = SourceContract.ExtractMember(MainWindowSource, "private bool DiscardPausedMerge()");
        SourceContract.AssertInOrder(discard, "Rollback(baseline)", "ConvertedFileCleanupService.DeletePartFiles(",
            "MarkConversionCompleted(manifest)", "ReturnToReviewAfterInterruptedExport()");
        Assert.DoesNotContain("ConvertedFileCleanupService.TryDeleteFile(", discard);
        Assert.Contains("return false;", discard);
        var close = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> CloseProjectFromWindowAsync(");
        Assert.Contains("!DiscardPausedMerge()) return false", close);
        var review = SourceContract.ExtractMember(MainWindowSource, "private void ReturnToReviewAfterInterruptedExport()");
        SourceContract.AssertInOrder(review, "SetPipelineState(ProjectPipelineState.ReviewBeforeMerge)",
            "WithManualMergeReview(true)", "UpdateManualMergeReviewPresetState()", "NotifyExportUiStateChanged()");
        var continuation = SourceContract.ExtractMember(MainWindowSource, "private async Task ContinuePausedPipelineAsync(");
        SourceContract.AssertInOrder(continuation, "if (resumeMerge)", "RunCurrentExportPlanAsync(", "manualMergeRequested: true", "return;");
    }

    [Fact]
    public void Drive_circle_activity_is_scoped_to_wait_presentation_and_cannot_replace_polling()
    {
        foreach (var method in new[] { "private async Task<DiscWaitDialogOutcome> WaitForNextMp3DiscWithDriveRoundDialogAsync(",
            "private async Task<AudioDiscPollingResult?> WaitForRequiredAudioDiscAsync(",
            "private async Task<AudioDiscProjectManifestDisc?> WaitForNextAudioDiscAsync(" })
            Assert.Contains("using var statusAnimation = BeginDriveCircleStatusAnimation(token);", SourceContract.ExtractMember(MainWindowSource, method));
        var begin = SourceContract.ExtractMember(MainWindowSource, "private IDisposable? BeginDriveCircleStatusAnimation(");
        Assert.Contains("!_settings.ExperimentalDriveRoundEnabled || token.IsCancellationRequested", begin);
        Assert.Contains("_driveCircleStatusAnimation?.Dispose()", SourceContract.ExtractMember(MainWindowSource, "private void CompleteWindowCloseCleanup()"));
        var animation = RepositorySource.Read(["BookStitch", "Services", "DriveCircleStatusAnimation.cs"]);
        Assert.DoesNotContain("DiscDriveService", animation);
        Assert.DoesNotContain("PollingService", animation);
        var dialogService = RepositorySource.Read(["BookStitch", "Services", "BoxedDiscWaitDialogService.cs"]);
        Assert.DoesNotContain("waitingAnimation", dialogService);
        Assert.Contains("Interval = TimeSpan.FromSeconds(2)", dialogService);
        Assert.Contains("await checkDiscAsync(", dialogService);
    }

    [Fact]
    public void Confirmed_session_hook_precedes_startup_project_access_and_query_has_no_worker_side_effects()
    {
        var app = RepositorySource.Read(["BookStitch", "App.xaml.cs"]);
        SourceContract.AssertInOrder(SourceContract.ExtractMember(app, "protected override void OnStartup"),
            "SingleInstanceService.TryAcquire()", "new WindowsSessionEndMonitor(", "InitializeDiagnostics(",
            ".ProcessStartup();", "base.OnStartup(e);");
        var query = SourceContract.ExtractMember(app, "protected override void OnSessionEnding");
        Assert.DoesNotContain("PrepareForConfirmedSessionEnding", query);
        Assert.DoesNotContain("RequestActiveOperationCancellation", query);
        Assert.Contains("e.Cancel = true;", query);
        var confirmed = SourceContract.ExtractMember(MainWindowSource, "internal void PrepareForConfirmedSessionEnding(");
        Assert.DoesNotContain("WaitForProjectProducersAsync", confirmed);
        Assert.DoesNotContain("TrySaveActiveProjectSnapshot", confirmed);
        Assert.DoesNotContain("CommitStoppedDiscProjectAsync", confirmed);
        Assert.Contains("CanDeferCompletion", confirmed);
    }

    [Fact]
    public void Disc_shutdown_commits_before_detaching_and_returns_before_generic_snapshots()
    {
        var close = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> CloseProjectFromWindowAsync(");
        SourceContract.AssertInOrder(close, "await WaitForProjectProducersAsync();", "if (saveDiscBoundary)",
            "await CommitStoppedDiscProjectAsync(", "ClearCurrentProjectWorkspace();", "return true;",
            "SaveEditableProjectSnapshot(", "TrySaveActiveProjectSnapshot();");
        var clear = SourceContract.ExtractMember(MainWindowSource, "private void ClearCurrentProjectWorkspace()");
        SourceContract.AssertInOrder(clear, "_activeMp3DiscManifest = null;", "ResetLoadedResumeProjectState();");
        var reset = SourceContract.ExtractMember(MainWindowSource, "private void ResetLoadedResumeProjectState()");
        SourceContract.AssertInOrder(reset, "_activeAudioDiscManifest = null;", "SetPipelineState(ProjectPipelineState.Preparing);");
        var stop = SourceContract.ExtractMember(MainWindowSource, "private async Task StopAndSaveDiscProjectAsync()");
        SourceContract.AssertInOrder(stop, "await WaitForProjectProducersAsync();", "await CommitStoppedDiscProjectAsync(", "ClearCurrentProjectWorkspace();");
    }

    [Fact]
    public void Audio_initialization_has_no_async_gap_between_folder_and_valid_manifest()
    {
        var prepare = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> TryShowAudioDiscContentsAsync(");
        SourceContract.AssertInOrder(prepare, "await _services.Discs.DiscDriveService.GetDriveDiagnosticsForPathAsync(",
            "sourceVersion != _workspaceReplacementVersion) return true;",
            "AudioDiscProjectService.CreateProjectFolder(", "AudioDiscProjectService.Save(manifest);",
            "AttachCanonicalMetadataSessionToNewProject(projectFolder);");
        var initialization = SourceContract.ExtractBetween(prepare, "AudioDiscProjectService.CreateProjectFolder(", "projectPrepared = true;");
        Assert.DoesNotContain("await ", initialization);
        Assert.DoesNotContain("Delete", prepare);
    }

    [Fact]
    public void Disc_stop_save_does_not_probe_an_mp3_manifest_for_an_audio_project()
    {
        var stopSave = SourceContract.ExtractMember(MainWindowSource, "private async Task CommitStoppedDiscProjectAsync(");
        var checkpoint = SourceContract.ExtractBetween(stopSave,
            "if (!service.HasPendingCommit(projectFolder))", "await Task.Run");
        var mp3Guard = checkpoint.IndexOf("if (LoadedProjectState.IsMp3Disc)", StringComparison.Ordinal);
        Assert.True(mp3Guard >= 0);
        Assert.DoesNotContain("TryLoadCurrentMp3DiscProjectManifest", checkpoint[..mp3Guard]);
        Assert.Contains("if (LoadedProjectState.IsMp3Disc)\n            {\n                var mp3 = TryLoadCurrentMp3DiscProjectManifest",
            checkpoint.Replace("\r\n", "\n"));
        SourceContract.AssertInOrder(checkpoint, "SaveCurrentAudioDiscProjectSnapshot",
            "if (LoadedProjectState.IsMp3Disc)", "TryLoadCurrentMp3DiscProjectManifest",
            "SaveCurrentMp3DiscProjectSnapshot", "TrackListStateService.Save");
    }

    [Fact]
    public void New_project_deletion_drains_workers_releases_workspace_guard_and_returns_to_idle()
    {
        var delete = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> DeleteNewProjectAsync(");
        SourceContract.AssertInOrder(delete, "IsNewProjectPreparation", "WaitForProjectProducersAsync();",
            "_currentProjectWorkFolder = string.Empty;", "ProjectIndexService.DeleteProject(", "if (!deletion.Deleted)",
            "_currentProjectWorkFolder = projectFolder;", "ClearCurrentProjectWorkspace();");
        var clear = SourceContract.ExtractMember(MainWindowSource, "private void ClearCurrentProjectWorkspace()");
        Assert.Contains("Tracks.Clear();", clear);
        Assert.Contains("_sessionProject.Clear();", clear);
        Assert.Contains("ProjectPipelineState.Preparing", clear);
    }

    [Fact]
    public void Only_new_project_creators_grant_delete_permission_loaded_workspace_does_not()
    {
        Assert.Contains("_sessionProject.Register(projectFolder, ProjectManifestTypes.Mp3DiscProject, startAccepted: true)",
            SourceContract.ExtractMember(MainWindowSource, "private async Task StartConfirmedMp3DiscProjectAsync("));
        Assert.Contains("_sessionProject.Register(projectFolder, ProjectManifestTypes.FolderProject, startAccepted: true)",
            SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> PrepareCurrentLocalProjectSourcesAsync()"));
        Assert.Contains("_sessionProject.RegisterAudioPreparation(GetWorkingRootFolder(), manifest, startProject)",
            SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> TryShowAudioDiscContentsAsync("));
        SourceContract.AssertInOrder(SourceContract.ExtractMember(MainWindowSource, "private async Task RunPreparedAudioDiscRipAsync("),
            "_sessionProject.AcceptStart(_currentProjectWorkFolder)", "await EnsureResumeDiscPreviewAsync()");
        var load = SourceContract.ExtractMember(MainWindowSource, "private async Task LoadResumeProjectIntoWorkspaceAsync(");
        SourceContract.AssertInOrder(load, "_sessionProject.Clear();", "_currentProjectWorkFolder = plan.ProjectFolder;");
        var phase = SourceContract.ExtractMember(MainWindowSource, "private void SetPipelineState(");
        Assert.Contains("state is ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed", phase);
        Assert.Contains("_sessionProject.Clear();", phase);
    }

    [Fact]
    public void Window_cross_uses_shared_close_intent_and_waits_before_rollback_or_save()
    {
        var close = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> CloseProjectFromWindowAsync(");
        SourceContract.AssertInOrder(close, "ResolveProjectCloseIntent", "ConfirmProjectCloseIntent(intent)",
            "WaitForProjectProducersAsync();", "StopPausedPresetReconversionAsync();",
            "StopPausedProjectExtensionAsync(confirm: false);", "TryCheckpointEditableProject()");
        Assert.Contains("DeleteNewProjectAsync(confirm: false)", close);
        Assert.Contains("SaveEditableProjectSnapshot(", close);
        Assert.Contains("return false;", close);
    }

    [Fact]
    public void Active_window_close_precedes_loaded_project_close_and_confirmation_reuses_the_close_intent()
    {
        var closing = SourceContract.ExtractMember(MainWindowSource, "private async void MainWindow_Closing");
        SourceContract.AssertInOrder(closing, "GetActiveActivity(", "if (activity == ApplicationActivity.None)", "await CloseProjectFromWindowAsync()");
        SourceContract.AssertInOrder(closing, "if (result != AppDialogResult.Yes)", "CancelApplicationRestartRequest();", "return;",
            "_isShutdownInProgress = true;", "CloseProjectFromWindowAsync(confirmIntent: false, cancelActiveWork: true)");
        var close = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> CloseProjectFromWindowAsync(");
        Assert.Contains("if (confirmIntent && (intent != ProjectCloseIntent.Save || saveDiscBoundary)", close);
        Assert.Contains("if (!cancelActiveWork && !_presentationState.IsPipelinePaused)", close);
        SourceContract.AssertInOrder(close, "RequestActiveOperationCancellation();", "await WaitForProjectProducersAsync();", "StopPausedPresetReconversionAsync();");
        Assert.Contains("(_isShutdownInProgress && !_allowWindowClose)", closing);
        Assert.DoesNotContain("_allowWindowClose = true;", SourceContract.ExtractBetween(closing, "catch (Exception ex)", "NotifyExportUiStateChanged();"));
    }

    [Fact]
    public void Export_finally_always_releases_producer_owner_even_if_track_refresh_fails()
    {
        var export = SourceContract.ExtractMember(MainWindowSource, "private async Task ExportToAacWithWorkFolderAsync(");
        SourceContract.AssertInOrder(export, "CompleteExportPerformanceOperation(result);", "switch (result.Status)");
        var cleanup = export[export.LastIndexOf("        finally", StringComparison.Ordinal)..];
        SourceContract.AssertInOrder(cleanup, "_exportCancellation?.Dispose();", "_exportCancellation = null;", "IsExporting = false;", "IsBusy = false;");
        var wait = SourceContract.ExtractMember(MainWindowSource, "private async Task WaitForProjectProducersAsync()");
        Assert.Contains("_exportCancellation is not null", wait);
    }

    [Fact]
    public void Mp3_pause_keeps_accepted_source_until_commit_and_scans_preview_off_dispatcher()
    {
        var import = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> ImportMp3DiscProjectAsync");
        SourceContract.AssertInOrder(import,
            "acceptedSource =>", "_resumeDiscStartDriveRoot = acceptedSource;",
            "FolderScanner.ScanAsync(", "token.ThrowIfCancellationRequested();",
            "ImportDiscAsync(", "_resumeDiscStartDriveRoot = null;");
        var resume = SourceContract.ExtractMember(MainWindowSource, "private async Task ContinuePausedMp3DiscProjectAsync");
        Assert.DoesNotContain("_resumeDiscStartDriveRoot = null;", resume, StringComparison.Ordinal);
        Assert.Contains("startWithSelectedDrive: selectedStartDrive is not null", resume, StringComparison.Ordinal);
        var pause = SourceContract.ExtractMember(MainWindowSource, "private async Task CancelMp3DiscImportAndShowResumeDialogAsync");
        Assert.Contains("await Task.Run", pause, StringComparison.Ordinal);
    }

    [Fact]
    public void Editable_project_checkpoint_precedes_close_session_end_and_export()
    {
        var close = SourceContract.ExtractMember(MainWindowSource, "private void CloseCurrentProject()");
        SourceContract.AssertInOrder(close, "if (!TryCheckpointEditableProject()) return;", "ResetMetadataForNewFolderProject();");
        Assert.DoesNotContain("PrepareForSessionEnding()", MainWindowSource);
        var confirmed = SourceContract.ExtractMember(MainWindowSource, "internal void PrepareForConfirmedSessionEnding(");
        Assert.DoesNotContain("TryCheckpointEditableProject", confirmed);
        var export = SourceContract.ExtractMember(MainWindowSource, "private async Task RunCurrentExportPlanAsync(");
        SourceContract.AssertInOrder(export, "if (!TryCheckpointEditableProject()) return;", "ApplyPipelineTransition(exportStartTransition)");
        var closing = SourceContract.ExtractMember(MainWindowSource, "private async void MainWindow_Closing");
        Assert.Contains("if (!TryCheckpointEditableProject())", closing);
        var checkpoint = SourceContract.ExtractMember(MainWindowSource, "private bool TryCheckpointEditableProject()");
        Assert.Contains("ProjectPipelineState.ReviewBeforeMerge or ProjectPipelineState.Completed", checkpoint);
        Assert.Contains("SaveEditableProjectSnapshot", checkpoint);
        Assert.Contains("return false;", checkpoint);
    }
    [Theory]
    [InlineData("private async void SelectFolder_Click", "dialog.ShowDialog(this)", "ResetProjectSourceModeStateForNewSource();")]
    [InlineData("private async void SelectDiscSource_Click", "ResolveSelectedDiscSourceAsync(selectedPath, updateWorkspace: false)", "SelectedFolder = selectedPath;")]
    [InlineData("private async Task LoadResumeProjectIntoWorkspaceAsync", "{", "IsBusy = true;")]
    public void Accepted_workspace_replacement_requires_successful_full_checkpoint(
        string member, string acceptance, string mutation)
    {
        var source = SourceContract.ExtractMember(MainWindowSource, member);
        SourceContract.AssertInOrder(source, acceptance,
            "if (!TryCheckpointWorkspaceReplacement(replacementVersion)) return;", mutation);
        var checkpoint = SourceContract.ExtractMember(MainWindowSource, "private bool TryCheckpointWorkspaceReplacement(");
        SourceContract.AssertInOrder(checkpoint, "expectedVersion != _workspaceReplacementVersion",
            "TryDiscardAudioPreparation()", "TryCheckpointEditableProject()", "_workspaceReplacementVersion++");
    }

    [Fact]
    public void Draft_discard_is_silent_and_failure_prevents_workspace_detachment()
    {
        var discard = SourceContract.ExtractMember(MainWindowSource, "private bool TryDiscardAudioPreparation()");
        SourceContract.AssertInOrder(discard, "_isWorkspaceReplacementInProgress = true;",
            "ValidateAudioDiscard(",
            "_currentProjectWorkFolder = string.Empty;", "ProjectIndexService.DeleteProject(",
            "if (!deletion.Deleted)", "ClearCurrentProjectWorkspace();");
        Assert.Contains("_currentProjectWorkFolder = folder;", discard);
        Assert.Contains("_discImportCancellation is not null", discard);
        Assert.DoesNotContain("await ", discard);
        Assert.DoesNotContain("ConfirmDiscard", discard);
        Assert.Contains("WriteError(\"Unstarted audio preparation cleanup failed\"", discard);
        var closing = SourceContract.ExtractMember(MainWindowSource, "private async void MainWindow_Closing");
        SourceContract.AssertInOrder(closing, "_sessionProject.IsUnstartedAudio(", "TryDiscardAudioPreparation()",
            "TryCheckpointMetadataBeforeClose()", "CreatePrompt(activity)");
        var close = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> CloseProjectFromWindowAsync(");
        Assert.Contains("_sessionProject.DeleteOnWindowClose", close);
        Assert.Contains("_sessionProject.StartAccepted", close);
    }

    [Fact]
    public void New_disc_creation_invalidates_late_or_duplicate_start_responses_before_allocating_a_folder()
    {
        var mp3 = SourceContract.ExtractMember(MainWindowSource, "private async Task StartConfirmedMp3DiscProjectAsync(");
        SourceContract.AssertInOrder(mp3, "GetDriveDiagnosticsForPathAsync(",
            "sourceVersion != _workspaceReplacementVersion) return;", "_workspaceReplacementVersion++;",
            "Mp3DiscImportService.CreateDiscProjectFolder(", "Mp3DiscProjectService.LoadOrCreate(");
        var audio = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> TryShowAudioDiscContentsAsync(");
        SourceContract.AssertInOrder(audio, "GetDriveDiagnosticsForPathAsync(",
            "sourceVersion != _workspaceReplacementVersion) return true;", "_workspaceReplacementVersion++;",
            "AudioDiscProjectService.CreateProjectFolder(");
    }

    [Fact]
    public void Mp3_extension_enters_shared_polling_on_selected_drive_and_keeps_import_errors_separate()
    {
        var entry = SourceContract.ExtractMember(MainWindowSource, "private async void AddProjectSources_Click(");
        Assert.Contains("firstDiscAlreadyReady: false", entry);
        Assert.Contains("startWithSelectedDrive: true", entry);
        Assert.DoesNotContain("firstDiscAlreadyReady: true", entry);
        var import = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> ImportMp3DiscProjectAsync(");
        SourceContract.AssertInOrder(import, "BuildCompletedDiscSignatureSet(projectManifest)",
            "discNumber != startDiscNumber || !firstDiscAlreadyReady", "await WaitForNextDiscAsync(",
            "token.ThrowIfCancellationRequested();", "DiscWaitDialogOutcome.ImportCompleted",
            "CompleteDiscImportEarly(projectManifest)", "acceptDuplicateForCurrentDisc =", "FolderScanner.ScanAsync(",
            "Mp3DiscImportWorkflowService.ImportDiscAsync(");
        Assert.Contains("MarkMp3DiscImportFailed(projectManifest, projectFolder, ex)", import);
        var wait = SourceContract.ExtractMember(MainWindowSource, "private async Task<DiscWaitDialogOutcome> WaitForNextDiscAsync(");
        SourceContract.AssertInOrder(wait, "startWithSelectedDrive && !_settings.ExperimentalDriveRoundEnabled",
            "Mp3DiscPollingService.CheckDiscSourceForNextImportAsync(", "if (initialCheck.CanImport)",
            "TrySelectNextMp3DiscFromDriveRoundAsync(", "WaitForNextMp3DiscWithDriveRoundDialogAsync(",
            "Mp3DiscWaitDialogService.WaitForNextDiscAsync(");
        Assert.Contains("sourceFolder, discNumber, totalDiscs, importedDiscSignatures, token", wait);
        var roundCheck = SourceContract.ExtractMember(MainWindowSource, "private async Task<Mp3DriveRoundWaitResult> CheckMp3DriveRoundForWaitDialogAsync(");
        Assert.DoesNotContain("SelectedFolder =", roundCheck);
        var roundWait = SourceContract.ExtractMember(MainWindowSource, "private async Task<DiscWaitDialogOutcome> WaitForNextMp3DiscWithDriveRoundDialogAsync(");
        SourceContract.AssertInOrder(roundWait, "var accepted = await", "if (accepted is DiscWaitDialogOutcome.Ready or DiscWaitDialogOutcome.ReadyWithDuplicateOverride",
            "SelectedFolder = acceptedSourceFolder", "acceptedSourceFolderChanged?.Invoke(acceptedSourceFolder)");
    }

    [Fact]
    public void Mp3_early_completion_finishes_preparation_and_uses_live_export_boundary_without_manual_release()
    {
        var import = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> ImportMp3DiscProjectAsync(");
        SourceContract.AssertInOrder(import, "DiscWaitDialogOutcome.ImportCompleted", "DiscardChangedResumeDiscPreview(discNumber)",
            "CompleteDiscImportEarly(projectManifest)", "totalDiscs = projectManifest.TotalDiscs", "break;",
            "WaitForLiveMp3DiscPreparationAsync(", "CompleteMp3ImportLifecycle();",
            "ApplyLoadedProjectState(LoadedProjectState.WithDiscImportRequirement(false))",
            "if (autoExportWhenComplete)", "RunFinalMp3DiscExportAsync(projectFolder)");
        var export = SourceContract.ExtractMember(MainWindowSource, "private async Task RunFinalMp3DiscExportAsync(");
        Assert.Contains("RunCurrentExportPlanAsync(", export);
        Assert.Contains("manualMergeRequested: false", export);
        // Pause keeps the extension; only an actual non-pause cancellation rolls it back.
        SourceContract.AssertInOrder(import, "if (_presentationState.PauseRequested)", "EnterPipelinePause(",
            "ProjectExtensionRollbackService.Rollback(extensionSnapshot, generatedConvertedFiles)");
        var stop = SourceContract.ExtractMember(MainWindowSource, "private async Task StopPausedProjectExtensionAsync(");
        Assert.DoesNotContain("StopAndSaveDiscProjectAsync", stop);
        Assert.Contains("stopAction?.Invoke()", stop);
    }

    [Fact]
    public void Accepted_mp3_pause_retains_disc_attempt_and_cannot_fall_through_to_eject_or_next_disc()
    {
        var import = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> ImportMp3DiscProjectAsync(");
        SourceContract.AssertInOrder(import, "CanResumeMp3Import(discNumber, current.Signature)",
            "shouldWaitForDisc = false", "acceptDuplicateForCurrentDisc = _resumeDiscPreview.Mp3DuplicateAccepted",
            "if (shouldWaitForDisc)", "WaitForNextDiscAsync(");
        SourceContract.AssertInOrder(import, "_resumeDiscPreview.Set(new Mp3DiscPendingChapterDisc(",
            "_resumeDiscPreview.MarkProcessingStarted()", "if (importResult.WasCanceled)",
            "return await HandleImportCancellationAsync(", "_resumeDiscPreview.Clear()", "WarnIfMp3DiscEjectFailedAsync(");
        var extension = SourceContract.ExtractMember(MainWindowSource, "private async void AddProjectSources_Click(");
        Assert.Contains("_resumeDiscStartDriveRoot ?? sourceFolder", extension);
        var pause = SourceContract.ExtractMember(MainWindowSource, "private bool IsPipelinePauseCommandActive()");
        Assert.Contains("(IsDiscImporting || IsExporting)", pause);
        var load = SourceContract.ExtractMember(MainWindowSource, "private async Task LoadResumeProjectIntoWorkspaceAsync(");
        Assert.Contains("loadSucceeded && LoadedProjectState.IsMp3Disc && !plan.IsStoppedDiscProject", load);
        Assert.Contains("CreateCompletedPause()", load);
        var clear = SourceContract.ExtractMember(MainWindowSource, "private void ClearProjectExtensionPauseContext()");
        SourceContract.AssertInOrder(clear, "_resumeDiscPreview.Mp3Disc is not null", "_resumeDiscPreview.Clear()", "_resumeDiscStartDriveRoot = null");
    }

    private static readonly string MainWindowSource =
        RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Fact]
    public void Paused_mp3_continuation_rechecks_disc_even_without_a_selected_start_drive()
    {
        var continuation = SourceContract.ExtractMember(MainWindowSource, "private async Task ContinuePausedMp3DiscProjectAsync()");
        Assert.Contains("firstDiscAlreadyReady: false", continuation);
        Assert.DoesNotContain("firstDiscAlreadyReady: selectedStartDrive is null", continuation);
        Assert.Contains("startWithSelectedDrive: selectedStartDrive is not null", continuation);
    }

    [Fact]
    public void Live_mp3_completion_uses_durable_shared_checkpoint_and_drains_before_review()
    {
        var register = SourceContract.ExtractMember(MainWindowSource, "private void RegisterPreparedMp3Track(");
        Assert.Contains("lifecycle.Complete(", register);
        var lifecycle = RepositorySource.Read(["BookStitch", "Services", "PreparedTrackLifecycleSession.cs"]);
        SourceContract.AssertInOrder(SourceContract.ExtractMember(lifecycle, "public ExportWorkManifestTrack Complete("),
            "_store.UpdateTrack(", "SaveCriticalCheckpoint();", "QueueTiming(");
        Assert.Contains("_store.Save(_manifestPath, _manifest)",
            SourceContract.ExtractMember(lifecycle, "private void SaveCriticalCheckpoint()"));
        var import = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> ImportMp3DiscProjectAsync(");
        SourceContract.AssertInOrder(import, "await WaitForLiveMp3DiscPreparationAsync(",
            "await preparedLifecycle.DrainAsync();", "PrepareCompletedMp3DiscProjectForCurrentPresetAsync(");
    }

    [Fact]
    public void Local_manifest_initialization_and_checkpoint_are_inside_the_controlled_error_boundary()
    {
        var preparation = SourceContract.ExtractMember(MainWindowSource, "private async Task<bool> PrepareCurrentLocalProjectSourcesAsync()");
        SourceContract.AssertInOrder(preparation, "try", "WorkManifestService.LoadOrCreate(",
            "WorkManifestService.Save(", "await using var preparedLifecycle", "catch (Exception ex)", "ShowHardWorkflowErrorDialog(");
    }

    [Fact]
    public void Single_instance_gate_is_acquired_before_mutable_startup_and_released_before_restart()
    {
        var app = RepositorySource.Read(["BookStitch", "App.xaml.cs"]);
        SourceContract.AssertInOrder(SourceContract.ExtractMember(app, "protected override void OnStartup"),
            "SingleInstanceService.TryAcquire()", "if (_singleInstance is null)", "Shutdown();", "InitializeDiagnostics(");
        SourceContract.AssertInOrder(SourceContract.ExtractMember(app, "protected override void OnExit"),
            "_singleInstance?.Dispose();", "_applicationRestartService.TryRelaunchOnExit();");
        Assert.Contains("e.Cancel = true;", SourceContract.ExtractMember(app, "protected override void OnSessionEnding"));
    }

    [Fact]
    public void Failed_metadata_checkpoint_cancels_normal_close_and_prevents_project_or_session_close()
    {
        var closing = SourceContract.ExtractMember(MainWindowSource, "private async void MainWindow_Closing");
        SourceContract.AssertInOrder(closing, "if (!TryCheckpointMetadataBeforeClose())", "e.Cancel = true;", "if (_allowWindowClose)");
        var guard = SourceContract.ExtractMember(MainWindowSource, "private bool TryCheckpointMetadataBeforeClose()");
        Assert.Contains("TryFlushCanonicalMetadataBeforeExtensionSnapshot", guard);
        Assert.Contains("_allowWindowClose = false;", guard);
        Assert.Contains("_isShutdownInProgress = false;", guard);
        Assert.Contains("return false;", guard);
        SourceContract.AssertInOrder(SourceContract.ExtractMember(MainWindowSource, "private void CloseCurrentProject()"),
            "if (!TryCheckpointMetadataBeforeClose()) return;", "ResetMetadataForNewFolderProject();");
        var confirmed = SourceContract.ExtractMember(MainWindowSource, "internal void PrepareForConfirmedSessionEnding(");
        Assert.DoesNotContain("TryCheckpointMetadataBeforeClose", confirmed);
        SourceContract.AssertInOrder(confirmed, "RecordConfirmed(", "RequestActiveOperationCancellation();");
    }

    [Fact]
    public void Shutdown_event_boundary_logs_failures_without_rethrowing_and_keeps_cancellation_distinct()
    {
        var closing = SourceContract.ExtractMember(MainWindowSource, "private async void MainWindow_Closing");
        var cleanup = SourceContract.ExtractMember(MainWindowSource, "private void CompleteWindowCloseCleanup()");
        var step = SourceContract.ExtractMember(MainWindowSource, "private void RunCloseCleanupStep");
        Assert.DoesNotContain("throw;", closing, StringComparison.Ordinal);
        Assert.DoesNotContain("throw;", cleanup, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException ex)", closing, StringComparison.Ordinal);
        Assert.Contains("shutdownWaitPerformancePhase.Cancel();", closing, StringComparison.Ordinal);
        Assert.Contains("shutdownWaitPerformancePhase.Fail(ex);", closing, StringComparison.Ordinal);
        Assert.Contains("stoppedCleanly = false;", closing, StringComparison.Ordinal);
        Assert.Contains("DiagnosticLogService.WriteError", closing, StringComparison.Ordinal);
        Assert.Contains("DiagnosticLogService.WriteError", cleanup, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", step, StringComparison.Ordinal);
        Assert.Contains("DiagnosticLogService.WriteError", step, StringComparison.Ordinal);
        Assert.Contains("SetActiveProjectFolderProvider(() => _currentProjectWorkFolder)", MainWindowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_workspace_transitions_restore_the_shared_idle_status()
    {
        Assert.Contains(
            "private string _statusText = NoProjectStatusText;",
            MainWindowSource,
            StringComparison.Ordinal);

        foreach (var signature in new[]
                 {
                     "private void UpdateFinalStatus(int trackCount)",
                     "private void CloseCurrentProject()"
                 })
        {
            Assert.Contains(
                "StatusText = NoProjectStatusText;",
                SourceContract.ExtractMember(MainWindowSource, signature),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Confirmed_active_shutdown_uses_a_danger_action_and_schedules_the_final_close()
    {
        var closing = SourceContract.ExtractMember(MainWindowSource, "private async void MainWindow_Closing");

        Assert.Contains(
            "new AppDialogButton(LocalizationText.Get(discardAttempt ? \"Project.Close.DiscardAndClose\" : \"Shutdown.AbortAndClose\"), AppDialogResult.Yes, IsDanger: true)",
            closing,
            StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            closing,
            "_allowWindowClose = true;",
            "Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));");
    }

    [Fact]
    public void Final_close_cleanup_runs_after_owned_windows_have_closed_and_releases_owned_runtime_resources()
    {
        var closing = SourceContract.ExtractMember(MainWindowSource, "private async void MainWindow_Closing");
        var closed = SourceContract.ExtractMember(MainWindowSource, "private void MainWindow_Closed");
        var cleanup = SourceContract.ExtractMember(MainWindowSource, "private void CompleteWindowCloseCleanup()");

        Assert.DoesNotContain("CompleteWindowCloseCleanup();", closing, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            closed,
            "CompleteWindowCloseCleanup();",
            "ConfirmApplicationRestartAfterCloseCleanup();");
        SourceContract.AssertInOrder(
            cleanup,
            "if (_windowCloseCleanupCompleted)",
            "_windowCloseCleanupCompleted = true;");
        Assert.Contains("_chapterMetadataRefreshTimer.Stop();", cleanup, StringComparison.Ordinal);
        Assert.Contains("_trackStateUpdateQueueService.Dispose();", cleanup, StringComparison.Ordinal);
        Assert.Contains("_services.Foundation.GlobalSettingsManager.Dispose();", cleanup, StringComparison.Ordinal);
        Assert.Contains("_runtimeServices.NotificationService.Dispose();", cleanup, StringComparison.Ordinal);
        Assert.Contains("_services.Foundation.SystemSleepPreventionService.Dispose();", cleanup, StringComparison.Ordinal);
    }
    [Fact]
    public void Shutdown_only_cancels_maintenance_and_manual_numeric_cleanup_remains_available()
    {
        var cleanup = SourceContract.ExtractMember(MainWindowSource, "private void CompleteWindowCloseCleanup()");
        Assert.DoesNotContain("DeleteOldProjects(", cleanup);
        Assert.DoesNotContain("DeleteOldUnreferencedCoverFiles(", cleanup);
        Assert.Contains("_maintenanceCoordinator?.StopAsync()", cleanup);
        var numeric = SourceContract.ExtractMember(MainWindowSource, "private ProjectCleanupResult DeleteOldProjects(int olderThanDays, bool showUserMessage)");
        Assert.Contains("ProjectIndexService.DeleteProjectsOlderThan", numeric);
    }

    [Fact]
    public void Closing_existing_attempt_pauses_only_when_running_and_offers_discard_or_back()
    {
        var closing = SourceContract.ExtractMember(MainWindowSource, "private async void MainWindow_Closing");
        Assert.Contains("if (discardAttempt && !_presentationState.IsPipelinePaused)", closing);
        SourceContract.AssertInOrder(closing, "await RequestPipelinePauseAsync();", "await WaitForProjectProducersAsync();", "Project.Close.DiscardAndClose");
        Assert.Contains("Project.Close.Back", closing);
    }

    [Fact]
    public void Confirmed_shutdown_leaves_existing_attempts_to_durable_recovery_without_interaction()
    {
        var ending = SourceContract.ExtractMember(MainWindowSource, "internal void PrepareForConfirmedSessionEnding");
        SourceContract.AssertInOrder(ending,
            "_presentationState.IsProjectExtensionRun || _presetReconversionRollbackContext is not null",
            "ProjectReviewState.Phase == ProjectPipelineState.Merging) return;",
            ".RecordConfirmed(", "RequestActiveOperationCancellation();");
        Assert.DoesNotContain("ShowMessage", ending);
        Assert.DoesNotContain("await ", ending);
    }

    [Fact]
    public void Restart_intent_uses_normal_close_and_is_confirmed_only_after_cleanup()
    {
        var request = SourceContract.ExtractMember(MainWindowSource, "private void RequestApplicationRestart");
        var closing = SourceContract.ExtractMember(MainWindowSource, "private async void MainWindow_Closing");
        var closed = SourceContract.ExtractMember(MainWindowSource, "private void MainWindow_Closed");

        SourceContract.AssertInOrder(
            request,
            "app.ApplicationRestartService.RequestRestart();",
            "Close();");
        Assert.Contains("CancelApplicationRestartRequest();", closing, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            closed,
            "CompleteWindowCloseCleanup();",
            "ConfirmApplicationRestartAfterCloseCleanup();");
    }

}
