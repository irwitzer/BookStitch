using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ConvertedTrackPreparationWorkflowRequest(
    ConvertedTrackPreparationPlan PreparationPlan,
    ExportWorkManifest Manifest,
    string ManifestPath,
    ExportPreset Preset,
    string FfmpegPath,
    int ParallelConversions,
    long TotalTicks,
    object ManifestSyncRoot);

public sealed record ConvertedTrackPreparationWorkflowCallbacks(
    Action<ConvertedTrackPreparationProgressSnapshot>? ReportProgress = null,
    Action? SaveTimedSnapshot = null);

public sealed record ConvertedTrackPreparationWorkflowResult(
    int PreparedTrackCount,
    ConvertedTrackPreparationProgressSnapshot FinalProgress);

public sealed record ConvertedTrackPreparationBatch(
    IReadOnlyList<ConvertedTrackPreparationPlanItem> Items,
    ConvertedTrackResumeState ResumeState);

public sealed class ConvertedTrackPreparationWorkflowService
{
    private readonly WorkManifestService _workManifestService;
    private readonly AacExportProcessingService _aacExportProcessingService;

    public ConvertedTrackPreparationWorkflowService(
        WorkManifestService workManifestService,
        AacExportProcessingService aacExportProcessingService)
    {
        _workManifestService = workManifestService ?? throw new ArgumentNullException(nameof(workManifestService));
        _aacExportProcessingService = aacExportProcessingService ?? throw new ArgumentNullException(nameof(aacExportProcessingService));
    }

    public async Task<ConvertedTrackPreparationWorkflowResult> RunAsync(
        ConvertedTrackPreparationWorkflowRequest request,
        ConvertedTrackPreparationWorkflowCallbacks callbacks,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(request.ManifestSyncRoot);

        return await RunBatchAsync(
            request,
            new ConvertedTrackPreparationBatch(
                request.PreparationPlan.PendingItems,
                request.PreparationPlan.ResumeState),
            callbacks,
            token);
    }

    public async Task<ConvertedTrackPreparationWorkflowResult> RunBatchAsync(
        ConvertedTrackPreparationWorkflowRequest request,
        ConvertedTrackPreparationBatch batch,
        ConvertedTrackPreparationWorkflowCallbacks callbacks,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(request.ManifestSyncRoot);

        var progress = new ConvertedTrackPreparationProgressService(
            batch.ResumeState,
            request.TotalTicks);
        callbacks.ReportProgress?.Invoke(progress.GetSnapshot());

        var preparedTrackCount = 0;
        using var semaphore = new SemaphoreSlim(Math.Max(1, request.ParallelConversions));

        var tasks = batch.Items.Select(async item =>
        {
            await semaphore.WaitAsync(token);

            try
            {
                var durationTicks = TrackDurationService.GetEffectiveDurationTicks(item.Track);

                try
                {
                    callbacks.ReportProgress?.Invoke(progress.StartTrack(item.Index));

                    lock (request.ManifestSyncRoot)
                    {
                        _workManifestService.MarkTrackStarted(
                            request.Manifest,
                            item.Index,
                            item.Track,
                            item.SourcePath,
                            item.ConvertedPath,
                            request.Preset,
                            AudioProcessingService.NormalizeProcessingAction(item.Track.ProcessingAction) == "Übernehmen"
                                ? ProjectManifestTrackStatuses.Copied
                                : ProjectManifestTrackStatuses.Converting);
                        _workManifestService.Save(request.ManifestPath, request.Manifest);
                    }

                    await _aacExportProcessingService.PrepareTrackForExportAsync(
                        item.Track,
                        item.SourcePath,
                        item.ConvertedPath,
                        request.Preset,
                        request.FfmpegPath,
                        token,
                        ffmpegProgress =>
                        {
                            var snapshot = progress.UpdateActiveTrack(
                                item.Index,
                                ffmpegProgress.Ticks,
                                durationTicks);
                            callbacks.ReportProgress?.Invoke(snapshot);
                            callbacks.SaveTimedSnapshot?.Invoke();
                        });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lock (request.ManifestSyncRoot)
                    {
                        _workManifestService.MarkTrackFailed(
                            request.Manifest,
                            item.Index,
                            item.Track,
                            item.SourcePath,
                            item.ConvertedPath,
                            request.Preset,
                            ex.Message);
                        _workManifestService.Save(request.ManifestPath, request.Manifest);
                    }

                    throw new ExportTrackException(
                        item.Track.Index,
                        item.Track.FileName,
                        item.SourcePath,
                        ex);
                }

                Interlocked.Increment(ref preparedTrackCount);

                lock (request.ManifestSyncRoot)
                {
                    _workManifestService.UpdateTrack(
                        request.Manifest,
                        item.Index,
                        item.Track,
                        item.SourcePath,
                        item.ConvertedPath,
                        request.Preset);
                    _workManifestService.Save(request.ManifestPath, request.Manifest);
                }

                callbacks.ReportProgress?.Invoke(progress.CompleteTrack(item.Index, durationTicks));
                callbacks.SaveTimedSnapshot?.Invoke();
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        return new ConvertedTrackPreparationWorkflowResult(
            preparedTrackCount,
            progress.GetSnapshot());
    }
}
