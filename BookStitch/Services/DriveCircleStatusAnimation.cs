using System.Diagnostics;
using System.Windows.Threading;

namespace BookStitch.Services;

/// <summary>One scoped UI timer; never polls drives or changes the wait outcome.</summary>
internal sealed class DriveCircleStatusAnimation : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action _changed;
    private readonly long _started = Stopwatch.GetTimestamp();
    private CancellationTokenRegistration _cancellation;
    public bool IsActive { get; private set; } = true;
    public string Text { get; private set; } = DiscDriveRoundWaitPresentation.ActivityProgress(0);

    public DriveCircleStatusAnimation(Dispatcher dispatcher, Action changed, CancellationToken token)
    {
        _changed = changed;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += Tick;
        _timer.Start();
        _cancellation = token.Register(() => dispatcher.BeginInvoke(new Action(Dispose)));
    }

    private void Tick(object? sender, EventArgs args)
    {
        Text = DiscDriveRoundWaitPresentation.ActivityProgress((long)(Stopwatch.GetElapsedTime(_started).TotalMilliseconds / 700));
        _changed();
    }

    public void Dispose()
    {
        if (!IsActive) return;
        IsActive = false;
        _timer.Stop();
        _timer.Tick -= Tick;
        _cancellation.Dispose();
        _changed();
    }
}
