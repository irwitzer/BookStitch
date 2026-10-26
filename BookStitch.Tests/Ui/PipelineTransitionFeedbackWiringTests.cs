using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class PipelineTransitionFeedbackWiringTests
{
    private static string Source => RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Fact]
    public void Accepted_pause_projects_feedback_and_yields_to_renderer_before_cancellation()
    {
        var method = SourceContract.ExtractMember(Source, "private async Task RequestPipelinePauseAsync()");
        SourceContract.AssertInOrder(method, "if (!plan.ShouldPause)", "BeginPipelineUiTransition",
            "ApplyPipelineTransition(pauseRequestTransition)", "await Dispatcher.Yield(DispatcherPriority.Background)", "CancelAsync()");
        Assert.Contains("ReferenceEquals(discCancellation, _discImportCancellation)", method);
        Assert.Contains("ReferenceEquals(exportCancellation, _exportCancellation)", method);
        Assert.Contains("EndPipelineUiTransition(transitionOwner)", method);
        Assert.DoesNotContain("CreateCompletedPause", method);
    }

    [Theory]
    [InlineData("private async Task StopPausedPresetReconversionAsync()", "RollbackPresetReconversion")]
    [InlineData("private async Task StopPausedProjectExtensionAsync(", "stopAction?.Invoke()")]
    [InlineData("private async Task StopAndSaveDiscProjectAsync()", "RequestActiveOperationCancellation()")]
    [InlineData("private async Task<bool> DeleteNewProjectAsync(", "RequestActiveOperationCancellation()")]
    public void Existing_stop_owners_publish_and_render_before_cleanup_and_release_even_on_error(string signature, string cleanup)
    {
        var method = SourceContract.ExtractMember(Source, signature);
        SourceContract.AssertInOrder(method, "BeginPipelineUiTransition", "await Dispatcher.Yield(DispatcherPriority.Background)", cleanup);
        SourceContract.AssertInOrder(method, "finally", "EndPipelineUiTransition(transitionOwner)");
    }

    [Fact]
    public void Disc_stop_save_visibility_is_independent_of_enabled_state_during_its_transition()
    {
        var visibility = SourceContract.ExtractBetween(Source, "public Visibility StopAndSaveDiscProjectVisibility", "public string StopAndSaveDiscProjectButtonText");
        Assert.Contains("PipelineUiTransitionTarget.DiscStopSave", visibility);
        var button = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]).Descendants()
            .Single(e => e.Name.LocalName == "Button" && (string?)e.Attribute("Click") == "StopAndSaveDiscProject_Click");
        Assert.Equal("{Binding CanStopAndSaveDiscProject}", (string?)button.Attribute("IsEnabled"));
        Assert.Equal("{Binding StopAndSaveDiscProjectVisibility}", (string?)button.Attribute("Visibility"));
        Assert.Equal("{Binding StopAndSaveDiscProjectButtonText}", (string?)button.Attribute("Content"));
    }

    [Fact]
    public void Shutdown_feedback_begins_only_after_confirmation_and_before_project_cleanup()
    {
        var method = SourceContract.ExtractMember(Source, "private async void MainWindow_Closing");
        SourceContract.AssertInOrder(method, "if (result != AppDialogResult.Yes)", "_isShutdownInProgress = true",
            "BeginPipelineUiTransition", "await Dispatcher.Yield(DispatcherPriority.Background)",
            "CloseProjectFromWindowAsync(confirmIntent: false, cancelActiveWork: true)");
    }

    [Fact]
    public void Final_track_summary_and_posted_mp3_progress_cannot_replace_a_completed_pause()
    {
        var summary = SourceContract.ExtractMember(Source, "private void UpdateFinalStatus(int trackCount)");
        SourceContract.AssertInOrder(summary, "if (_presentationState.IsPipelinePaused)", "return;", "StatusText = LocalizationText.Format");
        var progress = SourceContract.ExtractMember(Source, "private void ReportMp3DiscPresetPreparationProgress(");
        SourceContract.AssertInOrder(progress, "var operationId = _workflowStatusOperationId", "Dispatcher.BeginInvoke", "_presentationState.IsPipelinePaused", "operationId != _workflowStatusOperationId", "ApplyWorkflowStatusViewState");
    }

    [Fact]
    public void Track_refresh_preserves_underlying_status_instead_of_persisting_a_transient_ui_message()
    {
        foreach (var signature in new[] { "private void RefreshLoadedProjectTrackStateFromPersistedProject(",
            "private async Task RefreshMp3DiscTrackListAfterDiscImportAsync(" })
        {
            var member = SourceContract.ExtractMember(Source, signature);
            Assert.Contains("var statusText = _statusText;", member);
            Assert.DoesNotContain("var statusText = StatusText;", member);
        }
    }

    [Fact]
    public void Editable_checkpoint_cannot_write_beside_active_producers_even_while_phase_is_still_completed()
    {
        var checkpoint = SourceContract.ExtractMember(Source, "private bool TryCheckpointEditableProject()");
        SourceContract.AssertInOrder(checkpoint, "_exportCancellation is not null", "_discImportCancellation is not null",
            "_trackAnalysisCancellation is not null", "return true;", "ProjectReviewState.Phase", "SaveEditableProjectSnapshot");
        var close = SourceContract.ExtractMember(Source, "private async Task<bool> CloseProjectFromWindowAsync(");
        SourceContract.AssertInOrder(close, "await WaitForProjectProducersAsync()", "TryCheckpointEditableProject()");
    }

    [Fact]
    public void Posted_status_is_rechecked_on_dispatcher_and_ui_notifications_do_not_change_pause_semantics()
    {
        var method = SourceContract.ExtractMember(Source, "private void ApplyWorkflowStatusViewState(");
        SourceContract.AssertInOrder(method, "void Apply()", "expectedOperationId != _workflowStatusOperationId", "StatusText = viewState.TeletextText");
        var notify = SourceContract.ExtractMember(Source, "private void NotifyExportUiStateChanged()");
        Assert.DoesNotContain("PauseRequested =", notify);
        Assert.Contains("_exportCancellation is null", notify);
        Assert.Contains("_trackAnalysisCancellation is null", notify);
    }
}
