using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>
/// Measures the physical Audio-CD rip and the complete Audio-CD pipeline separately.
/// The service deliberately contains no UI or persistence logic so measurements can
/// later be logged, displayed or exported without coupling the ripping workflow to it.
/// </summary>
public sealed class AudioDiscPipelineTimingService
{
    private readonly TimeProvider _timeProvider;
    private readonly object _syncRoot = new();

    private long? _startedTimestamp;
    private long? _ripCompletedTimestamp;
    private long? _pipelineCompletedTimestamp;
    private DateTimeOffset? _startedUtc;

    public AudioDiscPipelineTimingService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Start()
    {
        lock (_syncRoot)
        {
            _startedTimestamp = _timeProvider.GetTimestamp();
            _ripCompletedTimestamp = null;
            _pipelineCompletedTimestamp = null;
            _startedUtc = _timeProvider.GetUtcNow();
        }
    }

    public void MarkRipCompleted()
    {
        lock (_syncRoot)
        {
            EnsureStarted();
            _ripCompletedTimestamp ??= _timeProvider.GetTimestamp();
        }
    }

    public AudioDiscPipelineTimingSnapshot Complete()
    {
        lock (_syncRoot)
        {
            EnsureStarted();
            _ripCompletedTimestamp ??= _timeProvider.GetTimestamp();
            _pipelineCompletedTimestamp ??= _timeProvider.GetTimestamp();
            return CreateSnapshot(isRunning: false);
        }
    }

    public AudioDiscPipelineTimingSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return CreateSnapshot(isRunning: _startedTimestamp.HasValue && !_pipelineCompletedTimestamp.HasValue);
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            _startedTimestamp = null;
            _ripCompletedTimestamp = null;
            _pipelineCompletedTimestamp = null;
            _startedUtc = null;
        }
    }

    private AudioDiscPipelineTimingSnapshot CreateSnapshot(bool isRunning)
    {
        if (!_startedTimestamp.HasValue)
            return new AudioDiscPipelineTimingSnapshot(null, null, null, false);

        var currentTimestamp = _timeProvider.GetTimestamp();
        var ripEnd = _ripCompletedTimestamp;
        var totalEnd = _pipelineCompletedTimestamp ?? (isRunning ? currentTimestamp : null);

        return new AudioDiscPipelineTimingSnapshot(
            _startedUtc,
            ripEnd.HasValue ? _timeProvider.GetElapsedTime(_startedTimestamp.Value, ripEnd.Value) : null,
            totalEnd.HasValue ? _timeProvider.GetElapsedTime(_startedTimestamp.Value, totalEnd.Value) : null,
            isRunning);
    }

    private void EnsureStarted()
    {
        if (!_startedTimestamp.HasValue)
            throw new InvalidOperationException("The Audio-CD pipeline timer has not been started.");
    }
}
