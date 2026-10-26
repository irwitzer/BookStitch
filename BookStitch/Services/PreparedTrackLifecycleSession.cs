using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

// Owns child timing work for one existing manifest owner/run. Conversion workers never
// wait for probe slots; callers drain after producers settle and before review/rollback.
public sealed class PreparedTrackLifecycleSession : IAsyncDisposable
{
    private readonly WorkManifestService _store;
    private readonly IAudioExportProcessingService _audio;
    private readonly ExportWorkManifest _manifest;
    private readonly string _manifestPath, _ffprobePath;
    private readonly object _manifestLock;
    private readonly SemaphoreSlim _timingSlots;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _tasks = [];
    private readonly HashSet<Guid> _queued = [];
    private readonly HashSet<string> _preparing = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _queueLock = new();
    private readonly SemaphoreSlim _drainSlots = new(1);
    private readonly Dictionary<Guid, ConvertedAacTiming> _pendingTimings = [];
    private Task? _disposeTask;
    private bool _disposing;
    private Exception? _checkpointFailure;

    public PreparedTrackLifecycleSession(WorkManifestService store, IAudioExportProcessingService audio,
        ExportWorkManifest manifest, string manifestPath, object manifestLock,
        string ffprobePath, int effectiveConversionParallelism)
    {
        _store = store; _audio = audio; _manifest = manifest; _manifestPath = manifestPath;
        _manifestLock = manifestLock; _ffprobePath = ffprobePath;
        _timingSlots = new SemaphoreSlim(GetTimingParallelism(effectiveConversionParallelism));
    }

    internal static int GetTimingParallelism(int effectiveConversionParallelism) =>
        Math.Clamp((Math.Max(1, effectiveConversionParallelism) + 3) / 4, 1, 8);

    public bool TryMarkStarted(int index, TrackInfo track, string source, string converted, ExportPreset preset,
        CancellationToken token)
    {
        lock (_manifestLock)
        {
            token.ThrowIfCancellationRequested();
            if (_checkpointFailure is not null) throw _checkpointFailure;
            if (!_preparing.Add(Path.GetFullPath(converted))) return false;
            _store.MarkTrackStarted(_manifest, index, track, source, converted, preset,
                ProjectManifestTrackStatuses.Converting);
            return true;
        }
    }

    public void MarkStarted(int index, TrackInfo track, string source, string converted, ExportPreset preset, CancellationToken token)
    {
        if (!TryMarkStarted(index, track, source, converted, preset, token))
            throw new InvalidOperationException(LocalizationText.Get("Export.CacheCollision"));
    }

    public bool CanReuse(string source, string converted, ExportPreset preset)
    {
        lock (_manifestLock)
            return _manifest.Tracks.Any(entry => TrackPathService.PathEquals(entry.SourcePath, source) &&
                TrackPathService.PathEquals(entry.ConvertedPath, converted) &&
                WorkManifestService.IsReusableConvertedManifestEntry(entry, preset.DisplayName));
    }

    public ExportWorkManifestTrack Complete(int index, TrackInfo track, string source, string converted,
        ExportPreset preset, CancellationToken token)
    {
        ExportWorkManifestTrack entry;
        lock (_manifestLock)
        {
            // Once the producer has successfully finalized the audio, finish its
            // hard checkpoint even if Pause arrived in that last boundary gap.
            // Cancellation still prevents timing work/results; producers settle
            // before the caller publishes Paused or rolls back.
            if (_checkpointFailure is not null) throw _checkpointFailure;
            entry = _store.UpdateTrack(_manifest, index, track, source, converted, preset);
            _preparing.Remove(Path.GetFullPath(converted));
            // The durable Converted + ID checkpoint precedes every timing job.
            SaveCriticalCheckpoint();
        }
        QueueTiming(entry, token);
        return entry;
    }

    public void QueuePreset(ExportPreset preset, CancellationToken token, bool retry = false)
    {
        ExportWorkManifestTrack[] entries;
        lock (_manifestLock)
            entries = _manifest.Tracks.Where(entry => WorkManifestService.IsReusableConvertedManifestEntry(
                entry, preset.DisplayName)).ToArray();
        foreach (var entry in entries) QueueTiming(entry, token, retry);
    }

    private static bool HasTiming(ExportWorkManifestTrack entry) =>
        entry.AacTiming is { IsValid: true } timing && timing.Length == entry.ConvertedSizeBytes &&
        timing.LastWriteUtcTicks == entry.ConvertedLastWriteUtcTicks;

    private void SaveCriticalCheckpoint()
    {
        try { _store.Save(_manifestPath, _manifest); }
        catch (Exception error)
        {
            _checkpointFailure = new PreparedTrackCheckpointException(error);
            throw _checkpointFailure;
        }
    }

    public void QueueTiming(ExportWorkManifestTrack entry, CancellationToken token, bool retry = false)
    {
        lock (_manifestLock)
        {
            if (_checkpointFailure is not null || HasTiming(entry) || entry.PreparationId == Guid.Empty) return;
            var id = entry.PreparationId;
            var path = entry.ConvertedPath;
            lock (_queueLock)
            {
                if (_disposing) return;
                if (!_queued.Add(id) && !retry) return;
                _tasks.Add(Task.Run(() => AnalyzeAsync(id, path, token)));
            }
        }
    }

    private async Task AnalyzeAsync(Guid id, string path, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        var childToken = cancellation.Token;
        var entered = false;
        try
        {
            await _timingSlots.WaitAsync(childToken).ConfigureAwait(false);
            entered = true;
            ConvertedAacTiming timing;
            try
            {
                timing = await _audio.AnalyzeConvertedTimingAsync(path, _ffprobePath, null, childToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (childToken.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceWarning("Prepared AAC timing failed for {0}: {1}", path, error);
                return; // The successfully prepared audio remains Converted and reusable.
            }
            lock (_manifestLock)
            {
                if (childToken.IsCancellationRequested || _checkpointFailure is not null) return;
                var current = _manifest.Tracks.SingleOrDefault(entry => entry.PreparationId == id &&
                    TrackPathService.PathEquals(entry.ConvertedPath, path));
                if (current is null || !WorkManifestService.IsReusableConvertedManifestEntry(current, current.Preset) ||
                    !timing.IsValid || timing.Length != current.ConvertedSizeBytes ||
                    timing.LastWriteUtcTicks != current.ConvertedLastWriteUtcTicks) return;
                current.AacTiming = timing;
                // Reconstructible results piggyback on critical commits, then flush once
                // producers/children settle at the existing review/pause/merge boundary.
                _pendingTimings[id] = timing;
            }
        }
        catch (OperationCanceledException) when (childToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Prepared AAC timing failed for {0}: {1}", path, error);
        }
        finally { if (entered) _timingSlots.Release(); }
    }

    public async Task DrainAsync()
    {
        await _drainSlots.WaitAsync().ConfigureAwait(false);
        try
        {
            Task[] tasks;
            lock (_queueLock)
            {
                tasks = _tasks.ToArray();
                _tasks.Clear(); // A fault is observed once, never replayed by disposal.
            }
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            finally { FlushTimings(); }
        }
        finally { _drainSlots.Release(); }
    }

    private void FlushTimings()
    {
        lock (_manifestLock)
        {
            if (_pendingTimings.Count == 0) return;
            if (_checkpointFailure is not null)
            {
                _pendingTimings.Clear();
                return; // Disposal cannot persist a failed critical checkpoint indirectly.
            }
            try { _store.Save(_manifestPath, _manifest); }
            catch (Exception error)
            {
                // Keep durable Converted + PreparationId. Failed timing is retryable;
                // never mark preparation failed or overwrite a newer manifest owner.
                foreach (var entry in _manifest.Tracks)
                    if (_pendingTimings.TryGetValue(entry.PreparationId, out var timing) &&
                        ReferenceEquals(entry.AacTiming, timing)) entry.AacTiming = null;
                System.Diagnostics.Trace.TraceWarning("Prepared AAC timing checkpoint failed; audio remains prepared: {0}", error);
            }
            finally { _pendingTimings.Clear(); }
        }
    }

    public IReadOnlyList<ConvertedAacTiming> RequireTimings(IReadOnlyList<string> paths, ExportPreset preset)
    {
        lock (_manifestLock)
            return paths.Select(path =>
            {
                var entry = _manifest.Tracks.SingleOrDefault(entry =>
                    TrackPathService.PathEquals(entry.ConvertedPath, path) &&
                    string.Equals(entry.Preset, preset.DisplayName, StringComparison.OrdinalIgnoreCase));
                if (entry is null || !WorkManifestService.IsReusableConvertedManifestEntry(entry, preset.DisplayName) ||
                    !HasTiming(entry)) throw new InvalidDataException(LocalizationText.Get("AacTimeline.Invalid"));
                return entry.AacTiming!;
            }).ToArray();
    }

    public ValueTask DisposeAsync()
    {
        lock (_queueLock)
        {
            _disposing = true;
            // Never acquire the manifest lock while holding the queue lock.
            return new ValueTask(_disposeTask ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            try { await _stop.CancelAsync().ConfigureAwait(false); }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceWarning("Prepared AAC child cancellation failed: {0}", error);
            }
            await DrainAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            // Disposal must not replace an already handled pipeline error with a
            // second dispatcher failure; child faults have been consumed by Drain.
            System.Diagnostics.Trace.TraceWarning("Prepared AAC lifecycle disposal failed: {0}", error);
        }
        finally { _timingSlots.Dispose(); _stop.Dispose(); }
    }
}

// A finalized-audio checkpoint is a workflow failure, not an individual media
// conversion failure that live workers may count and continue past.
internal sealed class PreparedTrackCheckpointException(Exception error) : IOException(error.Message, error);
