using BookStitch.Models;

namespace BookStitch.Services;

public sealed class LiveConversionWorkflowService
{
    private readonly IAudioExportProcessingService _aacExportProcessingService;
    private readonly Func<string> _ffmpegPathProvider;

    public LiveConversionWorkflowService(
        IAudioExportProcessingService aacExportProcessingService,
        Func<string> ffmpegPathProvider)
    {
        _aacExportProcessingService = aacExportProcessingService;
        _ffmpegPathProvider = ffmpegPathProvider;
    }

    internal IAudioExportProcessingService AudioProcessing => _aacExportProcessingService;

    public async Task RunAsync(
        TrackInfo track,
        LiveConversionQueueItem item,
        ExportPreset preset,
        LiveConversionQueueService liveConversionQueue,
        SemaphoreSlim semaphore,
        Action onStarted,
        Action onCompleted,
        Action onFailed,
        CancellationToken token,
        Func<PerformanceExternalProcessContext?>? performanceContextFactory = null,
        Action? persistPreparedTrack = null)
    {
        var semaphoreEntered = false;

        try
        {
            await semaphore.WaitAsync(token);
            semaphoreEntered = true;
            onStarted();

            PerformanceExternalProcessContext? performanceContext = null;
            try
            {
                performanceContext = performanceContextFactory?.Invoke();
            }
            catch
            {
                // Diagnostic context creation must never alter conversion behavior.
            }

            await _aacExportProcessingService.PrepareTrackForExportAsync(
                track,
                item.SourcePath,
                item.ConvertedPath,
                preset,
                _ffmpegPathProvider(),
                token,
                _ => { },
                performanceContext);

            // Register only a successfully finalized artifact.
            persistPreparedTrack?.Invoke();
            liveConversionQueue.MarkCompleted();
            onCompleted();
        }
        catch (PreparedTrackCheckpointException)
        {
            throw; // Let the owner stop/settle the workflow; do not report a failed AAC conversion.
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
