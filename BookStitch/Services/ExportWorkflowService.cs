using BookStitch.Models;
using System.IO;
using System.Text;

namespace BookStitch.Services;

public sealed record ExportPerformanceContext(
    Func<string, PerformancePhaseScope> StartPhase,
    Func<Guid?, string?, PerformanceExternalProcessContext?> CreateExternalProcessContext,
    Func<bool>? IsPauseRequested = null)
{
    public bool TryIsPauseRequested()
    {
        try { return IsPauseRequested?.Invoke() == true; }
        catch { return false; }
    }

    public PerformancePhaseScope TryStartPhase(string phaseName)
    {
        try
        {
            return StartPhase?.Invoke(phaseName) ?? default;
        }
        catch
        {
            return default;
        }
    }

    public PerformanceExternalProcessContext? TryCreateExternalProcessContext(
        PerformancePhaseScope phase,
        string purpose)
    {
        if (!phase.IsActive)
            return null;

        try
        {
            return CreateExternalProcessContext?.Invoke(phase.PhaseId, purpose);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class ExportWorkflowService
{
    private static readonly TimeSpan ProjectSnapshotInterval = TimeSpan.FromSeconds(30);

    private readonly WorkManifestService _workManifestService;
    private readonly ProjectSnapshotService _projectSnapshotService;
    private readonly IAudioExportProcessingService _aacExportProcessingService;
    private readonly ConvertedFileCleanupService _convertedFileCleanupService;
    private readonly ConvertedTrackPreparationPlanService _convertedTrackPreparationPlanService;
    private readonly ConvertedTrackPreparationWorkflowService _convertedTrackPreparationWorkflowService;
    private readonly ExportChapterService _exportChapterService;
    private readonly ChapterAutoEngineService _chapterAutoEngineService;
    private readonly ChapterModeEngineService _chapterModeEngineService;
    private readonly IFinalTagService _finalTagService;
    private readonly IFinalOutputStorageService _finalOutputStorageService;
    private readonly IFinalizedOutputValidationService _finalizedOutputValidationService;

    public ExportWorkflowService(
        WorkManifestService workManifestService,
        ProjectSnapshotService projectSnapshotService,
        IAudioExportProcessingService aacExportProcessingService,
        ConvertedFileCleanupService convertedFileCleanupService,
        ConvertedTrackPreparationPlanService convertedTrackPreparationPlanService,
        ConvertedTrackPreparationWorkflowService convertedTrackPreparationWorkflowService,
        ExportChapterService exportChapterService,
        IFinalTagService finalTagService,
        IFinalOutputStorageService finalOutputStorageService,
        IFinalizedOutputValidationService? finalizedOutputValidationService = null,
        ChapterAutoEngineService? chapterAutoEngineService = null,
        ChapterModeEngineService? chapterModeEngineService = null)
    {
        _workManifestService = workManifestService ?? throw new ArgumentNullException(nameof(workManifestService));
        _projectSnapshotService = projectSnapshotService ?? throw new ArgumentNullException(nameof(projectSnapshotService));
        _aacExportProcessingService = aacExportProcessingService ?? throw new ArgumentNullException(nameof(aacExportProcessingService));
        _convertedFileCleanupService = convertedFileCleanupService ?? throw new ArgumentNullException(nameof(convertedFileCleanupService));
        _convertedTrackPreparationPlanService = convertedTrackPreparationPlanService ?? throw new ArgumentNullException(nameof(convertedTrackPreparationPlanService));
        _convertedTrackPreparationWorkflowService = convertedTrackPreparationWorkflowService ?? throw new ArgumentNullException(nameof(convertedTrackPreparationWorkflowService));
        _exportChapterService = exportChapterService ?? throw new ArgumentNullException(nameof(exportChapterService));
        _chapterAutoEngineService = chapterAutoEngineService ?? new ChapterAutoEngineService();
        _chapterModeEngineService = chapterModeEngineService ?? new ChapterModeEngineService(
            _chapterAutoEngineService,
            new ChapterMasterEngineService(),
            new ChapterCustomEngineService());
        _finalTagService = finalTagService ?? throw new ArgumentNullException(nameof(finalTagService));
        _finalOutputStorageService = finalOutputStorageService ?? throw new ArgumentNullException(nameof(finalOutputStorageService));
        _finalizedOutputValidationService = finalizedOutputValidationService ?? NullFinalizedOutputValidationService.Instance;
    }

    public async Task<ExportWorkflowResult> RunAsync(
        ExportWorkflowRequest request,
        ExportWorkflowCallbacks callbacks,
        CancellationToken token,
        ExportPerformanceContext? performanceContext = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(callbacks);

        PerformancePhaseScope activePerformancePhase = default;

        void CompletePerformancePhase()
        {
            try
            {
                activePerformancePhase.Complete();
            }
            catch
            {
                // Performance instrumentation must never alter the export workflow.
            }
            finally
            {
                activePerformancePhase = default;
            }
        }

        void StartPerformancePhase(string phaseName)
        {
            CompletePerformancePhase();
            activePerformancePhase = performanceContext?.TryStartPhase(phaseName) ?? default;
        }

        void CancelPerformancePhase()
        {
            try
            {
                activePerformancePhase.Cancel(performanceContext?.TryIsPauseRequested() == true);
            }
            catch
            {
                // Performance instrumentation must never alter cancellation behavior.
            }
            finally
            {
                activePerformancePhase = default;
            }
        }

        void FailPerformancePhase(Exception exception)
        {
            try
            {
                activePerformancePhase.Fail(exception);
            }
            catch
            {
                // Performance instrumentation must never replace the business failure.
            }
            finally
            {
                activePerformancePhase = default;
            }
        }

        PerformanceExternalProcessContext? CreatePerformanceExternalProcessContext(string purpose) =>
            performanceContext?.TryCreateExternalProcessContext(activePerformancePhase, purpose);

        void RunSynchronousPerformancePhase(string phaseName, Action action)
        {
            StartPerformancePhase(phaseName);
            try
            {
                action();
                CompletePerformancePhase();
            }
            catch (Exception ex)
            {
                FailPerformancePhase(ex);
                throw;
            }
        }

        var exportPlan = request.Plan;
        var trackSnapshot = exportPlan.TrackSnapshot.ToList();
        var preset = exportPlan.Preset;
        var totalTicks = exportPlan.TotalTicks;
        var convertedTracks = new string[trackSnapshot.Count];
        var manifestLock = new object();
        ProjectExtensionRollbackSnapshot? mergeBaseline = null;
        var rollbackService = new ProjectExtensionRollbackService();
        var preserveCompletedOutput = false;
        var outputCommitted = false;
        var committedOutputPath = exportPlan.FinalOutputPath;
        var preparedTrackCount = 0;
        ConvertedTrackResumeState? conversionResumeState = null;

        ExportWorkflowResult CommittedOutputResult(Exception error)
        {
            if (mergeBaseline is not null) rollbackService.Commit(mergeBaseline);
            System.Diagnostics.Trace.TraceWarning("Export committed; final bookkeeping failed: {0}", error);
            return new ExportWorkflowResult(ExportWorkflowResultStatus.Completed, committedOutputPath,
                exportPlan.ProjectWorkFolder, exportPlan.ConvertedFolder, error, conversionResumeState);
        }

        try
        {
            StartPerformancePhase("export.prepare");

            Directory.CreateDirectory(exportPlan.ConvertedFolder);
            Directory.CreateDirectory(exportPlan.MergeFolder);

            // Alte unvollständige .part-Dateien dürfen nie in einen neuen Export hineinlaufen.
            _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);
            _convertedFileCleanupService.TryDeleteFile(exportPlan.FinalPartPath);

            var manifest = _workManifestService.LoadOrCreate(
                exportPlan.ManifestPath,
                exportPlan.ProjectType,
                exportPlan.ProjectWorkFolder,
                request.CurrentFolderPath,
                preset.DisplayName);

            await using var lifecycle = new PreparedTrackLifecycleSession(_workManifestService,
                _aacExportProcessingService, manifest, exportPlan.ManifestPath, manifestLock,
                request.FfprobePath, exportPlan.ParallelConversions);

            _projectSnapshotService.MarkExportStarted(
                exportPlan.ManifestPath,
                manifest,
                request.Snapshot,
                exportPlan.FinalOutputFolder,
                exportPlan.FinalOutputFileName);

            var lastExportSnapshotUtc = DateTime.UtcNow;

            void SaveTimedExportSnapshot()
            {
                var now = DateTime.UtcNow;
                if (now - lastExportSnapshotUtc < ProjectSnapshotInterval)
                    return;

                lock (manifestLock)
                {
                    _projectSnapshotService.SaveExportSnapshot(
                        exportPlan.ManifestPath,
                        manifest,
                        request.Snapshot,
                        exportPlan.FinalOutputFolder,
                        exportPlan.FinalOutputFileName);
                }

                lastExportSnapshotUtc = now;
            }

            // Reconcile manifest/file attributes off the dispatcher.
            var preparationPlan = await Task.Run(() =>
            {
                lock (manifestLock)
                {
                    var plan = _convertedTrackPreparationPlanService.Build(
                        manifest, exportPlan.ProjectType, trackSnapshot,
                        request.CurrentFolderPath, exportPlan.ConvertedFolder, preset, token);
                    if (plan.ManifestChanged)
                        _workManifestService.Save(exportPlan.ManifestPath, manifest);
                    return plan;
                }
            }, token);

            conversionResumeState = preparationPlan.ResumeState;
            callbacks.ReportConversionResumeState?.Invoke(conversionResumeState);

            // Cache reuse avoids conversion only. It must never grant permission to merge.
            var requiresPreparation = preparationPlan.PendingCount > 0;
            if (requiresPreparation)
            {
                callbacks.SetPipelineState?.Invoke(ProjectPipelineState.Converting);
                callbacks.SetStatusText?.Invoke(LocalizationText.Format(
                    "ExportWorkflow.Preparing", trackSnapshot.Count, exportPlan.ParallelConversions));
                callbacks.ReportConversionProgress?.Invoke(0, trackSnapshot.Count, 0, totalTicks, []);
            }

            foreach (var item in preparationPlan.Items)
                convertedTracks[item.Index] = item.ConvertedPath;

            lifecycle.QueuePreset(preset, token);
            CompletePerformancePhase();
            if (requiresPreparation)
            {
                foreach (var pending in preparationPlan.PendingItems)
                    callbacks.BeforePrepareConvertedFile?.Invoke(pending.ConvertedPath);
                StartPerformancePhase("export.convert");
                var conversionPerformanceContext =
                    CreatePerformanceExternalProcessContext("export.convert");

                var preparationResult = await _convertedTrackPreparationWorkflowService.RunAsync(
                    new ConvertedTrackPreparationWorkflowRequest(
                        preparationPlan,
                        manifest,
                        exportPlan.ManifestPath,
                        preset,
                        request.FfmpegPath,
                        exportPlan.ParallelConversions,
                        totalTicks,
                        manifestLock,
                        conversionPerformanceContext,
                        request.FfprobePath, lifecycle),
                    new ConvertedTrackPreparationWorkflowCallbacks(
                        snapshot => ReportConversionProgress(callbacks, snapshot),
                        SaveTimedExportSnapshot),
                    token);

                CompletePerformancePhase();
                preparedTrackCount = preparationResult.PreparedTrackCount;
            }

            await lifecycle.DrainAsync();
            token.ThrowIfCancellationRequested();

            lock (manifestLock)
            {
                _projectSnapshotService.SaveExportSnapshot(
                    exportPlan.ManifestPath,
                    manifest,
                    request.Snapshot,
                    exportPlan.FinalOutputFolder,
                    exportPlan.FinalOutputFileName);
            }

            var finalChapterSettings = CloneChapterSettings(request.ChapterSettings);
            var activeChapterResult = RebuildChaptersForReviewOrMerge(
                request,
                trackSnapshot,
                finalChapterSettings);

            // Every pipeline, including loaded/completed projects and source extensions,
            // enters this boundary. Persisted state describes progress, never merge permission.
            lock (manifestLock)
            {
                _projectSnapshotService.MarkConversionCompleted(
                    exportPlan.ManifestPath, manifest, request.Snapshot,
                    exportPlan.FinalOutputFolder, exportPlan.FinalOutputFileName);
            }
            callbacks.SetPipelineState?.Invoke(ProjectPipelineState.ReviewBeforeMerge);
            token.ThrowIfCancellationRequested();
            var shouldPauseBeforeMerge = new PauseBeforeMergeDecisionService().ShouldPauseBeforeMerge(
                new PauseBeforeMergeDecisionInput(
                    // A repaired/changed cache requires a fresh review after preparation.
                    request.ManualMergeRequested && !requiresPreparation,
                    callbacks.MergeAutomaticallyAfterConversion()));
            if (shouldPauseBeforeMerge)
            {
                callbacks.SetStatusText?.Invoke(LocalizationText.Get("ExportWorkflow.PausedBeforeMerge"));
                callbacks.SetProgressText?.Invoke(LocalizationText.Format(
                    "ExportWorkflow.PreparedCount", trackSnapshot.Count, trackSnapshot.Count));
                callbacks.SetProgressPercent?.Invoke(100);
                callbacks.NotifyManualReviewStateChanged?.Invoke();
                return new ExportWorkflowResult(
                    ExportWorkflowResultStatus.PausedBeforeMerge,
                    exportPlan.FinalOutputPath,
                    exportPlan.ProjectWorkFolder,
                    exportPlan.ConvertedFolder,
                    ConversionResumeState: conversionResumeState);
            }

            mergeBaseline = rollbackService.CaptureMerge(exportPlan.ProjectWorkFolder, exportPlan.FinalPartPath);
            StartPerformancePhase("export.merge.prepare");
            callbacks.SetPipelineState?.Invoke(ProjectPipelineState.Merging);
            lock (manifestLock)
            {
                _projectSnapshotService.MarkMergingStarted(
                    exportPlan.ManifestPath,
                    manifest,
                    request.Snapshot,
                    exportPlan.FinalOutputFolder,
                    exportPlan.FinalOutputFileName);
            }
            callbacks.SetStatusText?.Invoke(LocalizationText.Get("ExportWorkflow.MergingFinalFile"));
            callbacks.SetProgressText?.Invoke(LocalizationText.Format("ExportWorkflow.MergePercent", 0d));
            callbacks.SetProgressPercent?.Invoke(0);

            CompletePerformancePhase();
            StartPerformancePhase("export.timing");
            // Preparation already attempted timing. Retry only missing/failed analyses;
            // a timing failure never changes the Converted state or requests conversion.
            lifecycle.QueuePreset(preset, token, retry: true);
            await lifecycle.DrainAsync();
            token.ThrowIfCancellationRequested();
            await _aacExportProcessingService.ValidateMergeInputsAsync(
                convertedTracks, exportPlan.TargetSampleRateHz, request.FfprobePath, token, preset);
            var timings = lifecycle.RequireTimings(convertedTracks, preset);
            var mergeSegments = ConvertedAacTimelineService.BuildSegments(timings);
            var exportTimeline = ConvertedAacTimelineService.MapChapters(
                activeChapterResult.RenderedTimeline, trackSnapshot.ToArray(), mergeSegments);
            totalTicks = checked(mergeSegments[^1].EndMicroseconds * 10);
            var concatLines = convertedTracks.SelectMany((path, index) => new[]
            {
                $"file '{EscapeFfmpegConcatPath(path)}'",
                "duration " + mergeSegments[index].ConcatDuration
            }).ToList();
            await File.WriteAllLinesAsync(exportPlan.ConcatListPath, concatLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), token);
            await File.WriteAllTextAsync(
                exportPlan.ChapterMetadataPath,
                _exportChapterService.BuildFfmpegChapterMetadataFromTimeline(
                    exportTimeline),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                token);

            CompletePerformancePhase();
            StartPerformancePhase("export.merge");
            callbacks.ReportMergeProgress?.Invoke(0, convertedTracks.Length, 0);
            await _aacExportProcessingService.MergeConvertedTracksAsync(
                exportPlan.ConcatListPath,
                exportPlan.ChapterMetadataPath,
                exportPlan.FinalPartPath,
                totalTicks,
                request.FfmpegPath,
                token,
                progress =>
                {
                    var progressTicks = Math.Clamp(progress.Ticks, 0, totalTicks);
                    var percent = totalTicks <= 0
                        ? 0
                        : Math.Clamp(progressTicks * 100.0 / totalTicks, 0, 100);

                    callbacks.SetProgressPercent?.Invoke(percent);
                    callbacks.SetProgressText?.Invoke(LocalizationText.Format("ExportWorkflow.MergePercent", percent));
                    var currentFile = totalTicks <= 0 || convertedTracks.Length == 0
                        ? 0
                        : Math.Clamp((int)Math.Ceiling(percent / 100.0 * convertedTracks.Length), 1, convertedTracks.Length);
                    callbacks.ReportMergeProgress?.Invoke(currentFile, convertedTracks.Length, percent);
                },
                CreatePerformanceExternalProcessContext("export.merge"));

            CompletePerformancePhase();
            token.ThrowIfCancellationRequested();

            StartPerformancePhase("export.inspect");
            var preTagInspection = await _finalizedOutputValidationService
                .InspectAsync(
                    exportPlan.FinalPartPath,
                    request.FfprobePath,
                    CreatePerformanceExternalProcessContext("export.inspect"),
                    token)
                .ConfigureAwait(false);
            CompletePerformancePhase();

            var finalChapterCount = activeChapterResult.RenderedTimeline.Count;
            var finalTags = (request.CreateFinalTags?.Invoke(preTagInspection.Duration, finalChapterCount) ?? request.FinalTags)
                .WithFinalTechnicalData(preTagInspection.Duration, finalChapterCount);
            var finalizationSnapshot = CreateFinalizationSnapshot(finalTags, finalChapterSettings) with
            {
                ExpectedDuration = TimeSpan.FromTicks(totalTicks),
                ExpectedChapters = exportTimeline.Select(chapter => new TrackEmbeddedChapterInfo
                { Title = chapter.Title, StartTicks = chapter.StartTicks, EndTicks = chapter.EndTicks }).ToArray()
            };

            StartPerformancePhase("export.tag");
            callbacks.SetStatusText?.Invoke(LocalizationText.Get("ExportWorkflow.WritingTags"));
            callbacks.SetProgressText?.Invoke(LocalizationText.Get("ExportWorkflow.WritingTags"));
            callbacks.SetProgressPercent?.Invoke(100);
            callbacks.NotifyWritingMetadata?.Invoke();

            _finalTagService.WriteFinalTags(exportPlan.FinalPartPath, finalizationSnapshot.OutputTags);
            CompletePerformancePhase();

            token.ThrowIfCancellationRequested();

            StartPerformancePhase("export.validate");
            var postTagInspection = await _finalizedOutputValidationService
                .InspectAsync(
                    exportPlan.FinalPartPath,
                    request.FfprobePath,
                    CreatePerformanceExternalProcessContext("export.validate"),
                    token)
                .ConfigureAwait(false);
            _finalizedOutputValidationService.Validate(finalizationSnapshot, postTagInspection);
            CompletePerformancePhase();

            preserveCompletedOutput = true;

            var finalOutputPath = exportPlan.FinalOutputPath;
            var overwriteFinalOutput = false;
            if (File.Exists(finalOutputPath) || Directory.Exists(finalOutputPath))
            {
                var renamedOutputPath = OutputFileConflictService.CreateRenamedOutputPath(finalOutputPath, preset);
                var conflictAction = callbacks.ResolveFinalOutputConflict?.Invoke(finalOutputPath, renamedOutputPath)
                    ?? FinalOutputConflictAction.Cancel;

                if (conflictAction == FinalOutputConflictAction.Cancel)
                {
                    RunSynchronousPerformancePhase(
                        "export.cleanup",
                        () =>
                        {
                            _convertedFileCleanupService.TryDeleteFile(exportPlan.FinalPartPath);
                            _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);
                        });

                    if (mergeBaseline is not null) rollbackService.Rollback(mergeBaseline);
                    return new ExportWorkflowResult(
                        ExportWorkflowResultStatus.FinalOutputDiscarded,
                        finalOutputPath,
                        exportPlan.ProjectWorkFolder,
                        exportPlan.ConvertedFolder,
                    ConversionResumeState: conversionResumeState);
                }

                if (conflictAction == FinalOutputConflictAction.Rename)
                    finalOutputPath = renamedOutputPath;
                else if (Directory.Exists(finalOutputPath))
                    throw new IOException(LocalizationText.Format("Export.InvalidOutputPath", finalOutputPath));
                else
                    overwriteFinalOutput = true;
            }

            try
            {
                RunSynchronousPerformancePhase(
                    "export.output",
                    () => _finalOutputStorageService.MoveToOutput(
                        exportPlan.FinalPartPath,
                        finalOutputPath,
                        overwriteFinalOutput));
            }
            catch (Exception ex) when (FinalOutputStorageService.IsRecoverableDestinationError(ex))
            {
                var desktopOutputPath = _finalOutputStorageService.CreateDesktopOutputPath(finalOutputPath);
                var failureAction = callbacks.ResolveFinalOutputFailure?.Invoke(finalOutputPath, desktopOutputPath, ex)
                    ?? FinalOutputFailureAction.Discard;

                if (failureAction == FinalOutputFailureAction.SaveToDesktop)
                {
                    RunSynchronousPerformancePhase(
                        "export.output",
                        () => _finalOutputStorageService.MoveToOutput(
                            exportPlan.FinalPartPath,
                            desktopOutputPath,
                            overwrite: false));
                    finalOutputPath = desktopOutputPath;
                }
                else
                {
                    preserveCompletedOutput = false;
                    RunSynchronousPerformancePhase(
                        "export.cleanup",
                        () =>
                        {
                            _convertedFileCleanupService.TryDeleteFile(exportPlan.FinalPartPath);
                            _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);
                        });

                    if (mergeBaseline is not null) rollbackService.Rollback(mergeBaseline);
                    return new ExportWorkflowResult(
                        ExportWorkflowResultStatus.FinalOutputDiscarded,
                        finalOutputPath,
                        exportPlan.ProjectWorkFolder,
                        exportPlan.ConvertedFolder,
                        ex,
                        conversionResumeState);
                }
            }

            outputCommitted = true;
            committedOutputPath = finalOutputPath;
            preserveCompletedOutput = false;
            RunSynchronousPerformancePhase(
                "export.cleanup",
                () =>
                {
                    // Existing presets/cache belong to the stable project, including excluded tracks.
                    _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);
                });

            StartPerformancePhase("export.finalize");
            _projectSnapshotService.MarkExportCompleted(
                exportPlan.ManifestPath,
                manifest,
                request.Snapshot,
                Path.GetDirectoryName(finalOutputPath) ?? exportPlan.FinalOutputFolder,
                Path.GetFileName(finalOutputPath));
            if (mergeBaseline is not null) rollbackService.Commit(mergeBaseline);
            callbacks.SetPipelineState?.Invoke(ProjectPipelineState.Completed);
            CompletePerformancePhase();

            return new ExportWorkflowResult(
                ExportWorkflowResultStatus.Completed,
                finalOutputPath,
                exportPlan.ProjectWorkFolder,
                exportPlan.ConvertedFolder,
                ConversionResumeState: conversionResumeState);
        }
        catch (OperationCanceledException)
        {
            CancelPerformancePhase();
            if (outputCommitted)
                return CommittedOutputResult(new OperationCanceledException("Post-commit finalization canceled."));

            if (!preserveCompletedOutput)
            {
                RunSynchronousPerformancePhase(
                    "export.cleanup",
                    () =>
                    {
                        _convertedFileCleanupService.TryDeleteFile(exportPlan.FinalPartPath);
                        _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);
                    });
            }

            try
            {
                var manifest = _workManifestService.LoadOrCreate(
                    exportPlan.ManifestPath,
                    exportPlan.ProjectType,
                    exportPlan.ProjectWorkFolder,
                    request.CurrentFolderPath,
                    preset.DisplayName);

                var mergePaused = manifest.State.Status == ProjectManifestStatuses.Merging &&
                    callbacks.IsPauseRequested?.Invoke() == true;
                manifest.Resume.MergePaused = mergePaused;
                _projectSnapshotService.MarkExportCanceled(
                    exportPlan.ManifestPath,
                    manifest,
                    request.Snapshot,
                    exportPlan.FinalOutputFolder,
                    exportPlan.FinalOutputFileName,
                    "Export wurde vom Benutzer abgebrochen.");
            }
            catch
            {
                // Ein Fehler beim Schreiben der project.json darf die Abbruchmeldung nicht blockieren.
            }

            if (mergeBaseline is not null && callbacks.IsPauseRequested?.Invoke() != true)
            {
                try { rollbackService.Rollback(mergeBaseline); }
                catch (Exception rollbackError)
                {
                    return new ExportWorkflowResult(ExportWorkflowResultStatus.Failed,
                        exportPlan.FinalOutputPath, exportPlan.ProjectWorkFolder, exportPlan.ConvertedFolder,
                        rollbackError, conversionResumeState);
                }
            }
            return new ExportWorkflowResult(
                ExportWorkflowResultStatus.Canceled,
                exportPlan.FinalOutputPath,
                exportPlan.ProjectWorkFolder,
                exportPlan.ConvertedFolder,
                ConversionResumeState: conversionResumeState);
        }
        catch (Exception ex)
        {
            FailPerformancePhase(ex);
            if (outputCommitted)
                return CommittedOutputResult(ex);

            if (!preserveCompletedOutput)
            {
                RunSynchronousPerformancePhase(
                    "export.cleanup",
                    () =>
                    {
                        _convertedFileCleanupService.TryDeleteFile(exportPlan.FinalPartPath);
                        _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);
                    });
            }

            try
            {
                var manifest = _workManifestService.LoadOrCreate(
                    exportPlan.ManifestPath,
                    exportPlan.ProjectType,
                    exportPlan.ProjectWorkFolder,
                    request.CurrentFolderPath,
                    preset.DisplayName);

                _projectSnapshotService.MarkExportFailed(
                    exportPlan.ManifestPath,
                    manifest,
                    request.Snapshot,
                    exportPlan.FinalOutputFolder,
                    exportPlan.FinalOutputFileName,
                    ex.Message);
            }
            catch
            {
                // Ein Fehler beim Schreiben der project.json darf die Fehlermeldung nicht blockieren.
            }

            if (mergeBaseline is not null)
            {
                try
                {
                    rollbackService.Rollback(mergeBaseline);
                    var restored = _workManifestService.LoadOrCreate(exportPlan.ManifestPath, exportPlan.ProjectType,
                        exportPlan.ProjectWorkFolder, request.CurrentFolderPath, preset.DisplayName);
                    _workManifestService.MarkExportFailed(restored, ex.Message);
                    _workManifestService.Save(exportPlan.ManifestPath, restored);
                }
                catch (Exception rollbackError)
                {
                    // Keep the pending journal and report failure, never a successful discard.
                    ex = new AggregateException(ex, rollbackError);
                }
            }
            return new ExportWorkflowResult(
                ExportWorkflowResultStatus.Failed,
                exportPlan.FinalOutputPath,
                exportPlan.ProjectWorkFolder,
                exportPlan.ConvertedFolder,
                ex,
                conversionResumeState);
        }
    }

    private static string EscapeFfmpegConcatPath(string path)
    {
        return path.Replace("'", "'\\''");
    }

    private static FinalizationSnapshot CreateFinalizationSnapshot(
        FinalAudioTagData finalTags,
        ChapterSettings? chapterSettings)
    {
        var policies = ProjectMetadataFields.OutputFields.ToDictionary(
            field => field,
            field => finalTags.IsFieldEnabled(field) ? OutputFieldPolicy.Enabled : OutputFieldPolicy.Disabled);

        return new FinalizationSnapshot(
            finalTags.ProjectMetadataRevision,
            finalTags.WithDuration(finalTags.Duration),
            policies,
            CloneChapterSettings(chapterSettings),
            finalTags.OutputPresentationCulture,
            finalTags.CompatibilityDescriptionEnabled,
            finalTags.CoverPath);
    }

    private static ChapterSettings? CloneChapterSettings(ChapterSettings? settings)
    {
        if (settings is null)
            return null;

        return new ChapterSettings
        {
            Mode = settings.Mode,
            NumberingStyle = settings.NumberingStyle,
            PreviewDataSource = settings.PreviewDataSource,
            Slot1 = settings.Slot1,
            Separator1 = settings.Separator1,
            Slot2 = settings.Slot2,
            Separator2 = settings.Separator2,
            Slot3 = settings.Slot3
        };
    }

    private ActiveChapterResult RebuildChaptersForReviewOrMerge(
        ExportWorkflowRequest request,
        IList<TrackInfo> tracks,
        ChapterSettings? chapterSettings)
    {
        var normalizedSettings = ChapterSettingsService.Normalize(
            chapterSettings,
            request.LegacyUseLeadingZerosInChapterSuggestions);
        var canonicalMetadata = request.CanonicalChapterMetadata ?? new ChapterCanonicalMetadataSnapshot(
            request.FinalTags.ProjectMetadataRevision ?? 0,
            request.Snapshot.BookTitle,
            MetadataOrigin.LegacyMigrated,
            request.Snapshot.Album,
            MetadataOrigin.LegacyMigrated);
        return _chapterModeEngineService.Apply(new ChapterAutoEngineRequest(
            tracks,
            request.Plan.ProjectType,
            request.Plan.ProjectWorkFolder,
            request.Plan.ProjectWorkFolder,
            canonicalMetadata,
            normalizedSettings,
            request.LegacyUseLeadingZerosInChapterSuggestions));
    }

    private static void ReportConversionProgress(
        ExportWorkflowCallbacks callbacks,
        ConvertedTrackPreparationProgressSnapshot snapshot)
    {
        callbacks.ReportConversionProgress?.Invoke(
            snapshot.CompletedCount,
            snapshot.TotalCount,
            snapshot.CurrentTicks,
            snapshot.TotalTicks,
            snapshot.ActiveTrackIndexes);
    }
}
