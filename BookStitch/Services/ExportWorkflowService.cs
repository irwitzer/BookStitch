using BookStitch.Models;
using System.IO;
using System.Text;

namespace BookStitch.Services;

public sealed class ExportWorkflowService
{
    private static readonly TimeSpan ProjectSnapshotInterval = TimeSpan.FromSeconds(30);

    private readonly WorkManifestService _workManifestService;
    private readonly ProjectSnapshotService _projectSnapshotService;
    private readonly AacExportProcessingService _aacExportProcessingService;
    private readonly ConvertedFileCleanupService _convertedFileCleanupService;
    private readonly ConvertedTrackPreparationPlanService _convertedTrackPreparationPlanService;
    private readonly ConvertedTrackPreparationWorkflowService _convertedTrackPreparationWorkflowService;
    private readonly ExportChapterService _exportChapterService;
    private readonly FinalTagService _finalTagService;
    private readonly FinalOutputStorageService _finalOutputStorageService;

    public ExportWorkflowService(
        WorkManifestService workManifestService,
        ProjectSnapshotService projectSnapshotService,
        AacExportProcessingService aacExportProcessingService,
        ConvertedFileCleanupService convertedFileCleanupService,
        ConvertedTrackPreparationPlanService convertedTrackPreparationPlanService,
        ConvertedTrackPreparationWorkflowService convertedTrackPreparationWorkflowService,
        ExportChapterService exportChapterService,
        FinalTagService finalTagService,
        FinalOutputStorageService finalOutputStorageService)
    {
        _workManifestService = workManifestService ?? throw new ArgumentNullException(nameof(workManifestService));
        _projectSnapshotService = projectSnapshotService ?? throw new ArgumentNullException(nameof(projectSnapshotService));
        _aacExportProcessingService = aacExportProcessingService ?? throw new ArgumentNullException(nameof(aacExportProcessingService));
        _convertedFileCleanupService = convertedFileCleanupService ?? throw new ArgumentNullException(nameof(convertedFileCleanupService));
        _convertedTrackPreparationPlanService = convertedTrackPreparationPlanService ?? throw new ArgumentNullException(nameof(convertedTrackPreparationPlanService));
        _convertedTrackPreparationWorkflowService = convertedTrackPreparationWorkflowService ?? throw new ArgumentNullException(nameof(convertedTrackPreparationWorkflowService));
        _exportChapterService = exportChapterService ?? throw new ArgumentNullException(nameof(exportChapterService));
        _finalTagService = finalTagService ?? throw new ArgumentNullException(nameof(finalTagService));
        _finalOutputStorageService = finalOutputStorageService ?? throw new ArgumentNullException(nameof(finalOutputStorageService));
    }

    public async Task<ExportWorkflowResult> RunAsync(
        ExportWorkflowRequest request,
        ExportWorkflowCallbacks callbacks,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(callbacks);

        var exportPlan = request.Plan;
        var trackSnapshot = exportPlan.TrackSnapshot.ToList();
        var preset = exportPlan.Preset;
        var totalTicks = exportPlan.TotalTicks;
        var convertedTracks = new string[trackSnapshot.Count];
        var manifestLock = new object();
        var preserveCompletedOutput = false;
        var preparedTrackCount = 0;
        ConvertedTrackResumeState? conversionResumeState = null;

        try
        {
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

            var manualMergeReviewAlreadyCompleted = _workManifestService.HasCompletedManualMergeReview(
                manifest,
                preset.DisplayName);

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

            callbacks.SetPipelineState?.Invoke(ProjectPipelineState.Converting);
            callbacks.SetStatusText?.Invoke($"Export läuft... {trackSnapshot.Count} Tracks werden mit {exportPlan.ParallelConversions} parallelen Jobs vorbereitet.");
            callbacks.ReportConversionProgress?.Invoke(0, trackSnapshot.Count, 0, totalTicks, []);

            ConvertedTrackPreparationPlan preparationPlan;

            lock (manifestLock)
            {
                preparationPlan = _convertedTrackPreparationPlanService.Build(
                    manifest,
                    exportPlan.ProjectType,
                    trackSnapshot,
                    request.CurrentFolderPath,
                    exportPlan.ConvertedFolder,
                    preset);

                if (preparationPlan.ManifestChanged)
                    _workManifestService.Save(exportPlan.ManifestPath, manifest);
            }

            conversionResumeState = preparationPlan.ResumeState;
            callbacks.ReportConversionResumeState?.Invoke(conversionResumeState);

            foreach (var item in preparationPlan.Items)
                convertedTracks[item.Index] = item.ConvertedPath;

            var preparationResult = await _convertedTrackPreparationWorkflowService.RunAsync(
                new ConvertedTrackPreparationWorkflowRequest(
                    preparationPlan,
                    manifest,
                    exportPlan.ManifestPath,
                    preset,
                    request.FfmpegPath,
                    exportPlan.ParallelConversions,
                    totalTicks,
                    manifestLock),
                new ConvertedTrackPreparationWorkflowCallbacks(
                    snapshot => ReportConversionProgress(callbacks, snapshot),
                    SaveTimedExportSnapshot),
                token);

            preparedTrackCount = preparationResult.PreparedTrackCount;

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

            var pauseBeforeMerge = callbacks.ShouldPauseBeforeMerge?.Invoke() ?? request.PauseBeforeMerge;
            var shouldPauseBeforeMerge = pauseBeforeMerge &&
                (preparedTrackCount > 0 || !manualMergeReviewAlreadyCompleted);

            if (shouldPauseBeforeMerge)
            {
                callbacks.SetStatusText?.Invoke("Konvertierung abgeschlossen. BookStitch stoppt vor dem Zusammenfügen.");
                callbacks.SetProgressText?.Invoke($"Konvertierung abgeschlossen: {trackSnapshot.Count}/{trackSnapshot.Count} Tracks vorbereitet.");
                callbacks.SetProgressPercent?.Invoke(100);

                lock (manifestLock)
                {
                    _projectSnapshotService.MarkConversionCompleted(
                        exportPlan.ManifestPath,
                        manifest,
                        request.Snapshot,
                        exportPlan.FinalOutputFolder,
                        exportPlan.FinalOutputFileName);
                }

                callbacks.SetPipelineState?.Invoke(ProjectPipelineState.ReviewBeforeMerge);
                callbacks.NotifyManualReviewStateChanged?.Invoke();
                return new ExportWorkflowResult(
                    ExportWorkflowResultStatus.PausedBeforeMerge,
                    exportPlan.FinalOutputPath,
                    exportPlan.ProjectWorkFolder,
                    exportPlan.ConvertedFolder,
                    ConversionResumeState: conversionResumeState);
            }

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
            callbacks.SetStatusText?.Invoke("Konvertierung abgeschlossen. Finale Datei mit Kapiteln wird zusammengefügt...");
            callbacks.SetProgressText?.Invoke("Zusammenfügen: 0,0%");
            callbacks.SetProgressPercent?.Invoke(0);

            var concatLines = convertedTracks
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => $"file '{EscapeFfmpegConcatPath(path)}'")
                .ToList();

            await File.WriteAllLinesAsync(exportPlan.ConcatListPath, concatLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), token);
            await File.WriteAllTextAsync(exportPlan.ChapterMetadataPath, _exportChapterService.BuildFfmpegChapterMetadata(trackSnapshot), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), token);

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
                    callbacks.SetProgressText?.Invoke($"Zusammenfügen: {percent:0.0}%");
                    var currentFile = totalTicks <= 0 || convertedTracks.Length == 0
                        ? 0
                        : Math.Clamp((int)Math.Ceiling(percent / 100.0 * convertedTracks.Length), 1, convertedTracks.Length);
                    callbacks.ReportMergeProgress?.Invoke(currentFile, convertedTracks.Length, percent);
                });

            token.ThrowIfCancellationRequested();

            callbacks.SetStatusText?.Invoke("Finale Tags und Cover werden geschrieben...");
            callbacks.SetProgressText?.Invoke("Finale Tags und Cover werden geschrieben...");
            callbacks.SetProgressPercent?.Invoke(100);
            callbacks.NotifyWritingMetadata?.Invoke();

            _finalTagService.WriteFinalTags(exportPlan.FinalPartPath, request.FinalTags);

            token.ThrowIfCancellationRequested();

            preserveCompletedOutput = true;

            var finalOutputPath = exportPlan.FinalOutputPath;
            var overwriteFinalOutput = false;
            if (File.Exists(finalOutputPath))
            {
                var renamedOutputPath = OutputFileConflictService.CreateRenamedOutputPath(finalOutputPath, preset);
                var conflictAction = callbacks.ResolveFinalOutputConflict?.Invoke(finalOutputPath, renamedOutputPath)
                    ?? FinalOutputConflictAction.Cancel;

                if (conflictAction == FinalOutputConflictAction.Cancel)
                {
                    _convertedFileCleanupService.TryDeleteFile(exportPlan.FinalPartPath);
                    _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);

                    return new ExportWorkflowResult(
                        ExportWorkflowResultStatus.FinalOutputDiscarded,
                        finalOutputPath,
                        exportPlan.ProjectWorkFolder,
                        exportPlan.ConvertedFolder,
                    ConversionResumeState: conversionResumeState);
                }

                if (conflictAction == FinalOutputConflictAction.Rename)
                    finalOutputPath = renamedOutputPath;
                else
                    overwriteFinalOutput = true;
            }

            try
            {
                _finalOutputStorageService.MoveToOutput(
                    exportPlan.FinalPartPath,
                    finalOutputPath,
                    overwriteFinalOutput);
            }
            catch (Exception ex) when (FinalOutputStorageService.IsRecoverableDestinationError(ex))
            {
                var desktopOutputPath = _finalOutputStorageService.CreateDesktopOutputPath(finalOutputPath);
                var failureAction = callbacks.ResolveFinalOutputFailure?.Invoke(finalOutputPath, desktopOutputPath, ex)
                    ?? FinalOutputFailureAction.Discard;

                if (failureAction == FinalOutputFailureAction.SaveToDesktop)
                {
                    _finalOutputStorageService.MoveToOutput(
                        exportPlan.FinalPartPath,
                        desktopOutputPath,
                        overwrite: false);
                    finalOutputPath = desktopOutputPath;
                }
                else
                {
                    preserveCompletedOutput = false;
                    _convertedFileCleanupService.TryDeleteFile(exportPlan.FinalPartPath);
                    _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);

                    return new ExportWorkflowResult(
                        ExportWorkflowResultStatus.FinalOutputDiscarded,
                        finalOutputPath,
                        exportPlan.ProjectWorkFolder,
                        exportPlan.ConvertedFolder,
                        ex,
                        conversionResumeState);
                }
            }

            preserveCompletedOutput = false;
            _convertedFileCleanupService.DeleteUnusedConvertedFiles(exportPlan.ConvertedFolder, convertedTracks);
            _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);

            callbacks.SetPipelineState?.Invoke(ProjectPipelineState.Completed);

            _projectSnapshotService.MarkExportCompleted(
                exportPlan.ManifestPath,
                manifest,
                request.Snapshot,
                Path.GetDirectoryName(finalOutputPath) ?? exportPlan.FinalOutputFolder,
                Path.GetFileName(finalOutputPath));

            return new ExportWorkflowResult(
                ExportWorkflowResultStatus.Completed,
                finalOutputPath,
                exportPlan.ProjectWorkFolder,
                exportPlan.ConvertedFolder,
                ConversionResumeState: conversionResumeState);
        }
        catch (OperationCanceledException)
        {
            if (!preserveCompletedOutput)
            {
                _convertedFileCleanupService.TryDeleteFile(exportPlan.FinalPartPath);
                _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);
            }

            try
            {
                var manifest = _workManifestService.LoadOrCreate(
                    exportPlan.ManifestPath,
                    exportPlan.ProjectType,
                    exportPlan.ProjectWorkFolder,
                    request.CurrentFolderPath,
                    preset.DisplayName);

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

            return new ExportWorkflowResult(
                ExportWorkflowResultStatus.Canceled,
                exportPlan.FinalOutputPath,
                exportPlan.ProjectWorkFolder,
                exportPlan.ConvertedFolder,
                ConversionResumeState: conversionResumeState);
        }
        catch (Exception ex)
        {
            if (!preserveCompletedOutput)
            {
                _convertedFileCleanupService.TryDeleteFile(exportPlan.FinalPartPath);
                _convertedFileCleanupService.DeletePartFiles(exportPlan.ProjectWorkFolder);
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
