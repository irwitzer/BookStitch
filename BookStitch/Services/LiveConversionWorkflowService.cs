using BookStitch.Models;

namespace BookStitch.Services;

public sealed class LiveConversionWorkflowService
{
    private readonly AacExportProcessingService _aacExportProcessingService;
    private readonly Func<string> _ffmpegPathProvider;

    public LiveConversionWorkflowService(
        AacExportProcessingService aacExportProcessingService,
        Func<string> ffmpegPathProvider)
    {
        _aacExportProcessingService = aacExportProcessingService;
        _ffmpegPathProvider = ffmpegPathProvider;
    }

    public async Task RunAsync(
        TrackInfo track,
        LiveConversionQueueItem item,
        ExportPreset preset,
        LiveConversionQueueService liveConversionQueue,
        SemaphoreSlim semaphore,
        Action onStarted,
        Action onCompleted,
        Action onFailed,
        CancellationToken token)
    {
        var semaphoreEntered = false;

        try
        {
            await semaphore.WaitAsync(token);
            semaphoreEntered = true;
            onStarted();

            await _aacExportProcessingService.PrepareTrackForExportAsync(
                track,
                item.SourcePath,
                item.ConvertedPath,
                preset,
                _ffmpegPathProvider(),
                token,
                _ => { });

            liveConversionQueue.MarkCompleted();
            onCompleted();
        }
        catch (OperationCanceledException)
        {
            // Ein kontrollierter Benutzerabbruch ist kein Konvertierungsfehler.
        }
        catch
        {
            onFailed();
        }
        finally
        {
            if (semaphoreEntered)
                semaphore.Release();
        }
    }

    public static async Task WaitForTasksAsync(
        List<Task> liveConversionTasks,
        object liveConversionTasksLock,
        Action? periodicSnapshot = null,
        TimeSpan? snapshotInterval = null)
    {
        var observedTaskCount = 0;
        var interval = snapshotInterval ?? TimeSpan.FromSeconds(30);

        while (true)
        {
            Task[] pendingTasks;

            lock (liveConversionTasksLock)
            {
                if (liveConversionTasks.Count == observedTaskCount)
                    return;

                pendingTasks = liveConversionTasks
                    .Skip(observedTaskCount)
                    .ToArray();
                observedTaskCount = liveConversionTasks.Count;
            }

            var allPendingTasks = Task.WhenAll(pendingTasks);
            if (periodicSnapshot is null || interval <= TimeSpan.Zero)
            {
                await allPendingTasks;
            }
            else
            {
                while (!allPendingTasks.IsCompleted)
                {
                    var completed = await Task.WhenAny(allPendingTasks, Task.Delay(interval));
                    if (completed == allPendingTasks)
                        break;

                    periodicSnapshot();
                }

                await allPendingTasks;
            }

            lock (liveConversionTasksLock)
            {
                if (liveConversionTasks.Count == observedTaskCount)
                    return;
            }
        }
    }
}
