namespace BookStitch.Tests.TestHelpers;

/// <summary>Manually advances the one-shot deadlines used by CancellationTokenSource.</summary>
internal sealed class DeadlineTestTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly HashSet<DeadlineTimer> _timers = [];
    private long _ticks;

    public override DateTimeOffset GetUtcNow() { lock (_gate) return DateTimeOffset.UnixEpoch.AddTicks(_ticks); }
    public override long GetTimestamp() { lock (_gate) return _ticks; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        DeadlineTimer[] due;
        lock (_gate)
        {
            _ticks += elapsed.Ticks;
            due = _timers.Where(timer => timer.Due <= _ticks).ToArray();
            foreach (var timer in due) timer.Due = long.MaxValue;
        }
        foreach (var timer in due) timer.Callback(timer.State);
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new DeadlineTimer(this, callback, state);
        lock (_gate) _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    private sealed class DeadlineTimer(DeadlineTestTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public long Due = long.MaxValue;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan && period != TimeSpan.Zero)
                throw new NotSupportedException("This test clock models one-shot deadlines only.");
            lock (clock._gate)
            {
                if (_disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : checked(clock._ticks + dueTime.Ticks);
                return true;
            }
        }

        public void Dispose() { lock (clock._gate) { _disposed = true; clock._timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
