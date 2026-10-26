using System.IO;

using BookStitch.Models;

namespace BookStitch.Services;

public enum AudioDiscLiveConversionOutcome
{
    Completed,
    Reused,
    Failed,
    Canceled
}

public sealed class AudioDiscLiveConversionSession : IDisposable
{
    private readonly Func<AudioDiscRippedTrack, CancellationToken, Task<AudioDiscLiveConversionOutcome>> _processor;
    private readonly List<Task> _tasks = new();
    private readonly HashSet<string> _knownTracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _syncRoot = new();
    private readonly AudioDiscLiveConversionManifestSession? _manifestSession;
    private readonly Action<AudioDiscLiveConversionSessionSnapshot>? _statusChanged;
    private readonly HashSet<int> _activeTrackNumbers = new();

    private int _acceptedCount;
    private int _completedCount;
    private int _failedCount;
    private int _canceledCount;
    private int _duplicateCount;
    private int _reusedCount;
    private readonly int _existingConvertedCount;
    private bool _disposed;

    public AudioDiscLiveConversionSession(
        AudioDiscLiveConversionService preparationService,
        LiveConversionWorkflowService workflowService,
        WorkManifestService workManifestService,
        AudioDiscProjectManifest audioDiscManifest,
        ExportPreset preset,
        int maxParallelConversions,
        Action<AudioDiscLiveConversionSessionSnapshot>? statusChanged = null,
        int existingConvertedCount = 0,
        Func<AudioDiscRippedTrack, PerformanceOperationScope>? startPerformanceOperation = null,
        Func<Guid, PerformanceExternalProcessContext?>? createPerformanceContext = null,
        Action? tryFinalizePerformanceSession = null,
        string ffprobePath = "",
        Func<bool>? isPauseRequested = null)
    {
        ArgumentNullException.ThrowIfNull(preparationService);
        ArgumentNullException.ThrowIfNull(workflowService);
        ArgumentNullException.ThrowIfNull(workManifestService);
        ArgumentNullException.ThrowIfNull(audioDiscManifest);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioDiscManifest.ProjectFolder);

        _statusChanged = statusChanged;
        _existingConvertedCount = Math.Max(0, existingConvertedCount);
        _manifestSession = new AudioDiscLiveConversionManifestSession(
            workManifestService,
            audioDiscManifest,
            preset, audio: workflowService.AudioProcessing, ffprobePath: ffprobePath,
            effectiveParallelism: maxParallelConversions);

        var queue = new LiveConversionQueueService();
        var semaphore = new SemaphoreSlim(Math.Max(1, maxParallelConversions));

        bool PauseRequested()
        {
            try { return isPauseRequested?.Invoke() == true; }
            catch { return false; }
        }

        _processor = async (rippedTrack, token) =>
        {
            var preparation = preparationService.CreatePreparation(
                rippedTrack,
                audioDiscManifest.ProjectFolder,
                preset);

            if (_manifestSession.ReconcileTrack(preparation, token))
                return AudioDiscLiveConversionOutcome.Reused;

            _manifestSession.MarkTrackStarted(preparation);

            var queueItem = new LiveConversionQueueItem(
                preparation.SourcePath,
                preparation.ConvertedPath,
                preset.DisplayName,
                rippedTrack.DiscNumber,
                rippedTrack.GlobalIndex);

            if (!queue.TryEnqueue(queueItem) ||
                !queue.TryDequeue(out var queuedItem))
            {
                _manifestSession.MarkTrackFailed(preparation, "Live-AAC-Konvertierung konnte nicht eingeplant werden.");
                return AudioDiscLiveConversionOutcome.Failed;
            }

            var completed = false;
            PerformanceOperationScope conversionPerformanceScope = default;
            try
            {
                await workflowService.RunAsync(
                    preparation.Track,
                    queuedItem,
                    preset,
                    queue,
                    semaphore,
                    () =>
                    {
                        if (startPerformanceOperation is null)
                            return;

                        try
                        {
                            conversionPerformanceScope = startPerformanceOperation(rippedTrack);
                        }
                        catch
                        {
                            conversionPerformanceScope = default;
                        }
                    },
                    () => completed = true,
                    () => { },
                    token,
                    () =>
                    {
                        if (!conversionPerformanceScope.IsActive || createPerformanceContext is null)
                            return null;

                        try
                        {
                            return createPerformanceContext(conversionPerformanceScope.OperationId);
                        }
                        catch
                        {
                            return null;
                        }
                    },
                    persistPreparedTrack: () => _manifestSession.MarkTrackCompleted(preparation, token));

                if (completed)
                    conversionPerformanceScope.Complete();
                else if (token.IsCancellationRequested)
                    conversionPerformanceScope.Cancel(PauseRequested());
                else
                    conversionPerformanceScope.Fail(new InvalidOperationException("Audio-CD-Live-Konvertierung fehlgeschlagen."));
            }
            catch (OperationCanceledException)
            {
                conversionPerformanceScope.Cancel(PauseRequested());
                throw;
            }
            catch
            {
                conversionPerformanceScope.Fail(new InvalidOperationException("Audio-CD-Live-Konvertierung wurde durch eine Ausnahme beendet."));
                throw;
            }
            finally
            {
                try
                {
                    tryFinalizePerformanceSession?.Invoke();
                }
                catch
                {
                    // Performance finalization must never alter live conversion behavior.
                }
            }

            if (completed)
            {
                return AudioDiscLiveConversionOutcome.Completed;
            }

            if (token.IsCancellationRequested)
            {
                _manifestSession.MarkTrackCanceled(preparation);
                return AudioDiscLiveConversionOutcome.Canceled;
            }

            _manifestSession.MarkTrackFailed(preparation, "Live-AAC-Konvertierung fehlgeschlagen.");
            return AudioDiscLiveConversionOutcome.Failed;
        };

        _ownedSemaphore = semaphore;
    }

    public AudioDiscLiveConversionSession(
        Func<AudioDiscRippedTrack, CancellationToken, Task<AudioDiscLiveConversionOutcome>> processor,
        Action<AudioDiscLiveConversionSessionSnapshot>? statusChanged = null,
        int existingConvertedCount = 0)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _statusChanged = statusChanged;
        _existingConvertedCount = Math.Max(0, existingConvertedCount);
    }

    private readonly SemaphoreSlim? _ownedSemaphore;

    public Task QueueAsync(AudioDiscRippedTrack rippedTrack, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(rippedTrack);

        var key = rippedTrack.GlobalIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

        lock (_syncRoot)
        {
            if (!_knownTracks.Add(key))
            {
                _duplicateCount++;
                return Task.CompletedTask;
            }

            _acceptedCount++;
        }

        // Do not start conversion work while holding the session lock.
        // The processor and status callback may execute synchronously before their first await.
        var task = Task.Run(() => ProcessAsync(rippedTrack, token), CancellationToken.None);

        lock (_syncRoot)
        {
            _tasks.Add(task);
        }

        return Task.CompletedTask;
    }

    public async Task<int> QueueExistingRippedTracksAsync(
        AudioDiscProjectManifest manifest,
        CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(manifest);

        var acceptedBefore = GetSnapshot().AcceptedCount;

        foreach (var track in manifest.Discs
                     .OrderBy(disc => disc.DiscNumber)
                     .SelectMany(disc => disc.Tracks.OrderBy(track => track.TrackNumber)))
        {
            if (!string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(track.RelativePath))
            {
                continue;
            }

            var sourcePath = Path.Combine(manifest.ProjectFolder, track.RelativePath);
            if (!IsCompleteFile(sourcePath))
                continue;

            await QueueAsync(
                new AudioDiscRippedTrack(
                    track.DiscNumber,
                    track.GlobalIndex,
                    track.TrackNumber,
                    track.SourceId,
                    sourcePath,
                    track.Duration),
                token);
        }

        return GetSnapshot().AcceptedCount - acceptedBefore;
    }

    public async Task WaitForCompletionAsync(
        Action? periodicSnapshot = null,
        TimeSpan? snapshotInterval = null)
    {
        Task[] tasks;

        lock (_syncRoot)
        {
            tasks = _tasks.ToArray();
        }

        if (tasks.Length == 0)
        {
            if (_manifestSession is not null) await _manifestSession.DrainTimingAsync();
            return;
        }

        var allTasks = Task.WhenAll(tasks);
        var interval = snapshotInterval ?? TimeSpan.FromSeconds(30);

        if (periodicSnapshot is null || interval <= TimeSpan.Zero)
        {
            await allTasks;
            if (_manifestSession is not null) await _manifestSession.DrainTimingAsync();
            return;
        }

        while (!allTasks.IsCompleted)
        {
            var completed = await Task.WhenAny(allTasks, Task.Delay(interval));
            if (completed == allTasks)
                break;

            periodicSnapshot();
        }

        await allTasks;
        if (_manifestSession is not null) await _manifestSession.DrainTimingAsync();
    }

    public void SaveManifestSnapshot()
    {
        _manifestSession?.SaveProjectSnapshot();
    }

    public void MarkManifestCanceled(string reason)
    {
        _manifestSession?.MarkSessionCanceled(reason);
    }

    public void MarkManifestFailed(string errorSummary)
    {
        _manifestSession?.MarkSessionFailed(errorSummary);
    }

    public AudioDiscLiveConversionSessionSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return new AudioDiscLiveConversionSessionSnapshot(
                _acceptedCount,
                _completedCount,
                _failedCount,
                _canceledCount,
                _duplicateCount,
                _reusedCount,
                _activeTrackNumbers.OrderBy(number => number).ToArray(),
                _existingConvertedCount);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _manifestSession?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _ownedSemaphore?.Dispose();
    }

    private static bool IsCompleteFile(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task ProcessAsync(AudioDiscRippedTrack rippedTrack, CancellationToken token)
    {
        AudioDiscLiveConversionOutcome outcome;

        lock (_syncRoot)
        {
            _activeTrackNumbers.Add(rippedTrack.GlobalIndex);
        }
        NotifyStatusChanged();

        try
        {
            outcome = await _processor(rippedTrack, token);
        }
        catch (OperationCanceledException)
        {
            outcome = AudioDiscLiveConversionOutcome.Canceled;
        }
        catch
        {
            outcome = AudioDiscLiveConversionOutcome.Failed;
        }

        lock (_syncRoot)
        {
            _activeTrackNumbers.Remove(rippedTrack.GlobalIndex);
            switch (outcome)
            {
                case AudioDiscLiveConversionOutcome.Completed:
                    _completedCount++;
                    break;
                case AudioDiscLiveConversionOutcome.Reused:
                    _reusedCount++;
                    break;
                case AudioDiscLiveConversionOutcome.Canceled:
                    _canceledCount++;
                    break;
                default:
                    _failedCount++;
                    break;
            }
        }

        NotifyStatusChanged();
    }

    private void NotifyStatusChanged()
    {
        _statusChanged?.Invoke(GetSnapshot());
    }
}
