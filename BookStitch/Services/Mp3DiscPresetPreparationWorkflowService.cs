using BookStitch.Models;
using System.Collections.Concurrent;

namespace BookStitch.Services;

public sealed class Mp3DiscPresetPreparationWorkflowService
{
    private readonly Mp3DiscPreparationService _preparationService;
    private readonly AacExportProcessingService _aacExportProcessingService;
    private readonly Func<string> _ffmpegPathProvider;

    public Mp3DiscPresetPreparationWorkflowService(
        Mp3DiscPreparationService preparationService,
        AacExportProcessingService aacExportProcessingService,
        Func<string> ffmpegPathProvider)
    {
        _preparationService = preparationService;
        _aacExportProcessingService = aacExportProcessingService;
        _ffmpegPathProvider = ffmpegPathProvider;
    }

    public void StartBackgroundPreparation(
        IEnumerable<TrackInfo> tracks,
        string projectFolder,
        ExportPreset preset,
        string convertedFolder,
        Func<string, string, bool> canReusePreparedTrack,
        SemaphoreSlim sharedSemaphore,
        List<Task> taskList,
        object taskListLock,
        Func<bool> shouldContinue,
        int workerCount,
        Action onCompleted,
        Action onFailed,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(canReusePreparedTrack);
        ArgumentNullException.ThrowIfNull(sharedSemaphore);
        ArgumentNullException.ThrowIfNull(taskList);
        ArgumentNullException.ThrowIfNull(taskListLock);
        ArgumentNullException.ThrowIfNull(shouldContinue);
        ArgumentNullException.ThrowIfNull(onCompleted);
        ArgumentNullException.ThrowIfNull(onFailed);

        var candidates = _preparationService.BuildMissingPresetPreparationItems(
            tracks,
            projectFolder,
            preset,
            convertedFolder,
            canReusePreparedTrack);
        if (candidates.Count == 0)
            return;

        var queue = new ConcurrentQueue<Mp3DiscPresetPreparationItem>(candidates);
        var resolvedWorkerCount = Math.Clamp(workerCount, 1, Math.Max(1, candidates.Count));

        for (var worker = 0; worker < resolvedWorkerCount; worker++)
        {
            var task = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && shouldContinue() && queue.TryDequeue(out var item))
                {
                    await sharedSemaphore.WaitAsync(token);

                    try
                    {
                        if (!shouldContinue())
                            return;

                        if (canReusePreparedTrack(item.SourcePath, item.ConvertedPath))
                        {
                            onCompleted();
                            continue;
                        }

                        await _aacExportProcessingService.PrepareTrackForExportAsync(
                            item.Track,
                            item.SourcePath,
                            item.ConvertedPath,
                            preset,
                            _ffmpegPathProvider(),
                            token,
                            _ => { });

                        onCompleted();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        onFailed();
                    }
                    finally
                    {
                        sharedSemaphore.Release();
                    }
                }
            }, token);

            lock (taskListLock)
            {
                taskList.Add(task);
            }
        }
    }

    public async Task<int> PrepareMissingTracksAsync(
        IEnumerable<TrackInfo> tracks,
        string projectFolder,
        ExportPreset preset,
        string convertedFolder,
        Func<string, string, bool> canReusePreparedTrack,
        int maxParallel,
        Action<int, int>? onProgress,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(canReusePreparedTrack);

        var candidates = _preparationService.BuildMissingPresetPreparationItems(
            tracks,
            projectFolder,
            preset,
            convertedFolder,
            canReusePreparedTrack);
        if (candidates.Count == 0)
            return 0;

        var parallel = Math.Clamp(maxParallel, 1, 40);
        var completed = 0;
        var failed = new ConcurrentBag<string>();

        using var semaphore = new SemaphoreSlim(parallel);

        var tasks = candidates.Select(async item =>
        {
            await semaphore.WaitAsync(token);

            try
            {
                if (canReusePreparedTrack(item.SourcePath, item.ConvertedPath))
                {
                    var reused = Interlocked.Increment(ref completed);
                    onProgress?.Invoke(reused, candidates.Count);
                    return;
                }

                await _aacExportProcessingService.PrepareTrackForExportAsync(
                    item.Track,
                    item.SourcePath,
                    item.ConvertedPath,
                    preset,
                    _ffmpegPathProvider(),
                    token,
                    _ => { });

                var done = Interlocked.Increment(ref completed);
                onProgress?.Invoke(done, candidates.Count);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed.Add($"{item.Track.FileName}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        if (!failed.IsEmpty)
        {
            throw new InvalidOperationException(
                "Einige bereits importierte Dateien konnten nicht für das aktuelle Preset vorbereitet werden.\n\n" +
                string.Join("\n", failed.Take(12)));
        }

        return completed;
    }
}
