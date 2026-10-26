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
    object ManifestSyncRoot,
    PerformanceExternalProcessContext? PerformanceContext = null,
    string? FfprobePath = null,
    PreparedTrackLifecycleSession? LifecycleSession = null);

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
    private readonly IAudioExportProcessingService _aacExportProcessingService;

    public ConvertedTrackPreparationWorkflowService(
        WorkManifestService workManifestService,
        IAudioExportProcessingService aacExportProcessingService)
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

        await using var ownedLifecycle = request.LifecycleSession is null
            ? new PreparedTrackLifecycleSession(_workManifestService, _aacExportProcessingService,
                request.Manifest, request.ManifestPath, request.ManifestSyncRoot, request.FfprobePath ?? "", request.ParallelConversions)
            : null;
        var lifecycle = request.LifecycleSession ?? ownedLifecycle!;
        lifecycle.QueuePreset(request.Preset, token);

        var progress = new ConvertedTrackPreparationProgressService(
            batch.ResumeState,
            request.TotalTicks);
        callbacks.ReportProgress?.Invoke(progress.GetSnapshot());

        var preparedTrackCount = 0;
        using var semaphore = new SemaphoreSlim(Math.Max(1, request.ParallelConversions));

        var groups = batch.Items.GroupBy(item => System.IO.Path.GetFullPath(item.ConvertedPath),
            StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var group in groups)
        {
            if (group.Select(item => AudioProcessingService.NormalizeProcessingAction(item.Track.ProcessingAction))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1 ||
                group.Select(item => System.IO.Path.GetFullPath(item.SourcePath))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
                throw new InvalidOperationException(LocalizationText.Get("Export.CacheCollision"));
        }
        var tasks = groups.Select(async group =>
        {
            var item = group.First();
            await semaphore.WaitAsync(token);

            try
            {
                var durationTicks = TrackDurationService.GetEffectiveDurationTicks(item.Track);

                try
                {
                    var startedProgress = progress.StartTrack(item.Index);
                    callbacks.ReportProgress?.Invoke(startedProgress);

                    lock (request.ManifestSyncRoot)
                    {
                        _workManifestService.MarkTrackStarted(
                            request.Manifest,
                            item.Index,
                            item.Track,
                            item.SourcePath,
                            item.ConvertedPath,
                            request.Preset,
                            AudioProcessingService.NormalizeProcessingAction(item.Track.ProcessingAction) == ProcessingActionIds.Reuse
                                ? ProjectManifestTrackStatuses.Copied
                                : ProjectManifestTrackStatuses.Converting);
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
                        },
                        request.PerformanceContext,
                        request.FfprobePath);

                }
                catch (OperationCanceledException)
                {
                    lock (request.ManifestSyncRoot)
                    {
                        _workManifestService.MarkTrackCanceled(request.Manifest, item.Index, item.Track,
                            item.SourcePath, item.ConvertedPath, request.Preset);
                        _workManifestService.Save(request.ManifestPath, request.Manifest);
                    }
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

                Interlocked.Add(ref preparedTrackCount, group.Count());

                lifecycle.Complete(item.Index, item.Track, item.SourcePath, item.ConvertedPath, request.Preset, token);

                foreach (var reference in group)
                {
                    var completedProgress = progress.CompleteTrack(reference.Index,
                        TrackDurationService.GetEffectiveDurationTicks(reference.Track));
                    callbacks.ReportProgress?.Invoke(completedProgress);
                }
                callbacks.SaveTimedSnapshot?.Invoke();
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        try { await Task.WhenAll(tasks); }
        finally { await lifecycle.DrainAsync(); }
        token.ThrowIfCancellationRequested();

        return new ConvertedTrackPreparationWorkflowResult(
            preparedTrackCount,
            progress.GetSnapshot());
    }

}
