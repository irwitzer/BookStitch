using System.Windows.Threading;

namespace BookStitch.Services;

public sealed class DispatcherCoalescingProgress<T> : IProgress<T>, IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action<T> _handler;
    private readonly object _syncRoot = new();
    private T? _latestValue;
    private bool _hasPendingValue;

    public DispatcherCoalescingProgress(
        Dispatcher dispatcher,
        Action<T> handler,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(handler);

        _handler = handler;
        _timer = new DispatcherTimer(
            interval ?? TimeSpan.FromMilliseconds(200),
            DispatcherPriority.Background,
            Timer_Tick,
            dispatcher);
        _timer.Start();
    }

    public void Report(T value)
    {
        lock (_syncRoot)
        {
            _latestValue = value;
            _hasPendingValue = true;
        }
    }

    public void Flush()
    {
        if (!_timer.Dispatcher.CheckAccess())
        {
            _timer.Dispatcher.Invoke(Flush);
            return;
        }

        if (TryTakeLatest(out var value))
            _handler(value);
    }

    public void Dispose()
    {
        if (!_timer.Dispatcher.CheckAccess())
        {
            _timer.Dispatcher.Invoke(Dispose);
            return;
        }

        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        Flush();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (TryTakeLatest(out var value))
            _handler(value);
    }

    private bool TryTakeLatest(out T value)
    {
        lock (_syncRoot)
        {
            if (!_hasPendingValue)
            {
                value = default!;
                return false;
            }

            value = _latestValue!;
            _latestValue = default;
            _hasPendingValue = false;
            return true;
        }
    }
}
