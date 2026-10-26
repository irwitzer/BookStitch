using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class PerformanceDiagnosticsWiringTests
{
    [Fact]
    public void Disc_diagnostics_use_the_live_pause_request_and_capture_scan_evidence_before_copy_returns()
    {
        var main = LoadText("BookStitch", "MainWindow.xaml.cs");
        var mp3 = SourceContract.ExtractMember(main, "private async Task<bool> ImportMp3DiscProjectAsync");
        var cancel = SourceContract.ExtractMember(mp3, "void CancelActiveMp3PerformanceOperation");
        Assert.Contains("activeMp3DiscPerformancePhase.Cancel(_presentationState.PauseRequested)", cancel);
        Assert.Contains("activeMp3DiscPerformanceScope.Cancel(_presentationState.PauseRequested)", cancel);
        var scanEvidence = mp3.IndexOf("precheck.CreatePerformanceWorkload(previewTracks.Count)", StringComparison.Ordinal);
        Assert.True(scanEvidence > 0);
        Assert.True(scanEvidence < mp3.IndexOf("if (importResult.WasCanceled)", StringComparison.Ordinal));
        var audio = SourceContract.ExtractMember(main, "private async Task RunPreparedAudioDiscRipAsync");
        var rip = SourceContract.ExtractMember(audio, "async Task<AudioDiscRipResult> RipAudioDiscWithPerformanceAsync");
        Assert.Contains("ripPhase.Cancel(_presentationState.PauseRequested)", rip);
        Assert.Contains("operationScope.Cancel(_presentationState.PauseRequested)", rip);
    }

    [Fact]
    public void Stop_save_diagnostics_cover_producers_checkpoint_commit_and_workspace_with_terminal_outcomes()
    {
        var method = SourceContract.ExtractMember(LoadText("BookStitch", "MainWindow.xaml.cs"),
            "private async Task StopAndSaveDiscProjectAsync");
        Assert.Contains("\"disc.stop-save\"", method);
        foreach (var name in new[] { "stop-save.wait-for-producers", "stop-save.checkpoint", "stop-save.commit-cleanup", "stop-save.workspace-catalog" })
            Assert.Contains(name, method);
        Assert.Contains("operation.Complete()", method);
        Assert.Contains("ex is OperationCanceledException", method);
        Assert.Contains("phase.Cancel(); operation.Cancel();", method);
        Assert.Contains("phase.Fail(ex); operation.Fail(ex);", method);
    }

    [Fact]
    public void Hidden_developer_access_gates_startup_and_disables_active_diagnostics()
    {
        var main = LoadText("BookStitch", "MainWindow.xaml.cs");
        var app = LoadText("BookStitch", "App.xaml.cs");
        var startup = SourceContract.ExtractMember(app, "private AppSettings? InitializeDiagnostics");
        Assert.Contains("DeveloperToolsAccessState.IsEnabledFor(settings, environmentContext.Environment)", startup);
        var setter = SourceContract.ExtractMember(app, "internal void SetPerformanceDiagnosticsEnabled");
        Assert.Contains("DeveloperToolsAccessState.IsEnabledFor(settings, ActiveEnvironmentContext.Environment)", setter);
        var hidden = SourceContract.ExtractMember(main, "private void DeveloperAccessState_PropertyChanged");
        Assert.Contains("DeveloperToolsCommands.AccessState.IsEnabled", hidden);
        Assert.Contains("SetPerformanceDiagnosticsEnabled(false)", hidden);
        Assert.Contains("_developerToolsWindow?.Close()", hidden);
        Assert.Contains("AccessState.PropertyChanged += DeveloperAccessState_PropertyChanged", main);
        Assert.Contains("AccessState.PropertyChanged -= DeveloperAccessState_PropertyChanged", main);
    }

    public static TheoryData<string, string[]> DiagnosticSchemaByOwner => new()
    {
        {
            "BookStitch/MainWindow.xaml.cs",
            [
                "project.resume", "resume.prepare", "resume.source-metadata-backfill",
                "resume.workspace-state", "resume.metadata", "resume.cover", "resume.track-replace",
                "resume.update-indexes.initial", "resume.ffprobe", "resume.processing-actions",
                "resume.update-indexes.final", "resume.final-ui", "resume.refresh",
                "app.shutdown", "shutdown.wait-for-idle", "shutdown.cleanup",
                "project.local.import", "project.scan", "track.reconcile", "project.prepare", "audio.probe",
                "disc.mp3.import", "disc.scan", "file.copy", "preset.prepare",
                "disc.audio.rip", "disc.rip", "disc.audio.convert", "audio.rip.flac",
                "export.run"
            ]
        },
        { "BookStitch/Services/ProjectCatalog.cs", ["project.catalog.refresh", "project.catalog.index", "project.catalog.damage"] },
        { "BookStitch/Services/ProjectCoverThumbnailCache.cs", ["project.catalog.cover-preview"] },
        { "BookStitch/Services/ProjectMaintenanceCoordinator.cs", ["maintenance.total", "maintenance.project-cleanup", "maintenance.cover-cleanup"] },
        { "BookStitch/Services/AudioDiscRipService.cs", ["audio.rip.flac"] },
        {
            "BookStitch/Services/ExportWorkflowService.cs",
            [
                "export.prepare", "export.convert", "export.merge", "export.tag",
                "export.output", "export.cleanup", "export.finalize"
            ]
        }
    };

    [Fact]
    public void Developer_tools_toggle_forwards_the_persisted_setting_to_the_application_session()
    {
        var document = LoadXaml("BookStitch", "Windows", "DeveloperToolsWindow.xaml");
        var toggle = document.Descendants()
            .Single(element =>
                element.Name.LocalName == "CheckBox" &&
                Attr(element, "x:Name") == "PerformanceDiagnosticsEnabledCheckBox");

        Assert.Equal("PerformanceDiagnosticsEnabledCheckBox_Changed", Attr(toggle, "Checked"));
        Assert.Equal("PerformanceDiagnosticsEnabledCheckBox_Changed", Attr(toggle, "Unchecked"));

        var debugWindowHandler = SourceContract.ExtractBetween(
            LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml.cs"),
            "private void PerformanceDiagnosticsEnabledCheckBox_Changed",
            "private void ForceShowFfmpegSetupButtonCheckBox_Changed");
        Assert.Contains("_debugSettingsService.Save(_debugSettings);", debugWindowHandler, StringComparison.Ordinal);
        Assert.Contains("_setPerformanceDiagnosticsEnabled(_debugSettings.PerformanceDiagnosticsEnabled);", debugWindowHandler, StringComparison.Ordinal);

        var mainWindowSetter = SourceContract.ExtractBetween(
            LoadText("BookStitch", "MainWindow.xaml.cs"),
            "private void SetPerformanceDiagnosticsEnabled",
            "private void SetForceShowFfmpegSetupButton");
        Assert.Contains("DebugSettingsService.Save(_debugSettings);", mainWindowSetter, StringComparison.Ordinal);
        Assert.Contains("app.SetPerformanceDiagnosticsEnabled(enabled, _settings);", mainWindowSetter, StringComparison.Ordinal);

        var appSetter = SourceContract.ExtractBetween(
            LoadText("BookStitch", "App.xaml.cs"),
            "internal void SetPerformanceDiagnosticsEnabled",
            "internal PerformanceOperationScope StartPerformanceOperation");
        Assert.Contains("PerformanceSessionStartReason.UserEnabled", appSetter, StringComparison.Ordinal);
        Assert.Contains("traceService.SetEnabled(false);", appSetter, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DiagnosticSchemaByOwner))]
    public void Diagnostic_schema_is_declared_by_its_owning_workflow(
        string relativePath,
        string[] schemaNames)
    {
        var owner = LoadText(relativePath.Split('/'));

        foreach (var schemaName in schemaNames)
        {
            Assert.True(
                owner.Contains($"\"{schemaName}\"", StringComparison.Ordinal),
                $"{relativePath} no longer declares the diagnostic schema name '{schemaName}'.");
        }
    }

    [Fact]
    public void Export_external_processes_are_attributed_to_their_workflow_phase()
    {
        var workflow = LoadText("BookStitch", "Services", "ExportWorkflowService.cs");

        foreach (var phaseName in new[] { "export.convert", "export.merge" })
        {
            Assert.True(
                workflow.Contains(
                    $"CreatePerformanceExternalProcessContext(\"{phaseName}\")",
                    StringComparison.Ordinal),
                $"External processes for '{phaseName}' are no longer attributed to that phase.");
        }
    }

    [Fact]
    public void Mp3_disc_measurement_starts_after_the_user_disc_change_wait()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var importWorkflow = SourceContract.ExtractBetween(
            mainWindow,
            "private async Task<bool> ImportMp3DiscProjectAsync",
            "private async Task WaitForLiveMp3DiscPreparationAsync");

        SourceContract.AssertInOrder(
            importWorkflow,
            "var waitOutcome = await WaitForNextDiscAsync(",
            "StartMp3DiscPerformanceOperation(sourceFolder);");
    }

    [Fact]
    public void Audio_disc_workflow_routes_ripping_through_the_instrumented_boundary()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var workflow = SourceContract.ExtractBetween(
            mainWindow,
            "private async Task RunPreparedAudioDiscRipAsync",
            "private async Task<bool> ConfirmRequiredAudioDiscStillAvailableAsync");

        Assert.Contains(
            "RipAudioDiscWithPerformanceAsync(disc, progress, token)",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Export_measurement_completes_before_result_specific_ui_handling()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var exportWorkflow = SourceContract.ExtractBetween(
            mainWindow,
            "private async Task ExportToAacWithWorkFolderAsync",
            "private void ReturnToReviewAfterInterruptedExport");

        SourceContract.AssertInOrder(
            exportWorkflow,
            "var result = await _services.Workflows.ExportWorkflowService.RunAsync(",
            "CompleteExportPerformanceOperation(result);",
            "switch (result.Status)");
    }

    [Fact]
    public void Completed_session_applies_statistics_and_persists_report_and_journal()
    {
        var app = LoadText("BookStitch", "App.xaml.cs");
        var completion = SourceContract.ExtractBetween(
            app,
            "private void CompletePerformanceDiagnosticsSession",
            "private PerformanceTraceService CreatePerformanceTraceService");

        Assert.Contains("new PerformanceStatisticsService().Apply(report)", completion, StringComparison.Ordinal);
        Assert.Contains("PersistPerformanceReport(report);", completion, StringComparison.Ordinal);
        Assert.Contains("CompletePerformanceJournalBestEffort();", completion, StringComparison.Ordinal);

        var reportPersistence = SourceContract.ExtractBetween(
            app,
            "private void PersistPerformanceReport",
            "private void CompletePerformanceJournalBestEffort");
        Assert.Contains("archive.WriteReportsAsync(storage.SessionFolder, report)", reportPersistence, StringComparison.Ordinal);

        var journalPersistence = SourceContract.ExtractBetween(
            app,
            "private void CompletePerformanceJournalBestEffort",
            "private static string GetBuildConfiguration");
        Assert.Contains("storage.CompleteJournalAsync()", journalPersistence, StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_loading_feedback_covers_actual_loading_and_cleans_up_all_exit_paths()
    {
        var main = LoadText("BookStitch", "MainWindow.xaml.cs");
        var resume = SourceContract.ExtractMember(main, "private async Task LoadResumeProjectIntoWorkspaceAsync(ProjectResumePlan plan)");
        SourceContract.AssertInOrder(resume,
            "TryCheckpointWorkspaceReplacement(replacementVersion)", "_isResumeLoading = true;",
            "StatusText = loadingText;", "ExportProgressText = loadingText;",
            "IsProgressIndeterminate = true;", "DispatcherPriority.Render",
            "ProjectResumeLoadWorkflowService.Prepare(plan)");
        SourceContract.AssertInOrder(resume,
            "MeasureResumePhase(\"resume.final-ui\"", "_isResumeLoading = false;",
            "IsProgressIndeterminate = false;", "PublishLoadedProjectStatus(loadResult);");
        var cleanup = resume[resume.LastIndexOf("finally", StringComparison.Ordinal)..];
        SourceContract.AssertInOrder(cleanup, "_isResumeLoading = false;", "IsProgressIndeterminate = false;", "IsBusy = false;");
        Assert.Contains("if (!loadSucceeded && ExportProgressText == loadingText)", cleanup);
        Assert.Contains("StatusText = previousStatusText;", cleanup);
        Assert.Contains("ExportProgressText = previousProgressText;", cleanup);
        Assert.Contains("ResetWorkspaceAfterCanceledTrackAnalysis(TrackAnalysisCancellationScope.ResumeLoad)", resume);
        Assert.Contains("_isResumeLoading || _isProgressIndeterminate", main);
        Assert.Contains("_isResumeLoading ? Visibility.Visible", main);
    }

    [Fact]
    public void Resume_measurement_isolates_collection_replacement_and_guards_late_probe_feedback()
    {
        var main = LoadText("BookStitch", "MainWindow.xaml.cs");
        Assert.Contains("MeasureResumePhase(\"resume.track-replace\", () => Tracks.ReplaceAll(loadResult.Tracks))", main);
        var probe = SourceContract.ExtractMember(main, "private async Task<bool> EnrichResumeTracksWithFfprobeAsync(");
        Assert.Contains("if (_isResumeLoading && !token.IsCancellationRequested)", probe);
    }

    private static XDocument LoadXaml(params string[] relativePath) =>
        RepositorySource.LoadXaml(relativePath);

    private static string LoadText(params string[] relativePath) =>
        RepositorySource.Read(relativePath);

    private static string Attr(XElement element, string attributeName)
    {
        XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        return attributeName.StartsWith("x:", StringComparison.Ordinal)
            ? element.Attribute(xamlNamespace + attributeName[2..])?.Value ?? string.Empty
            : element.Attribute(attributeName)?.Value ?? string.Empty;
    }
}
