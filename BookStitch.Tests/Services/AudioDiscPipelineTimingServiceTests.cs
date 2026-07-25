using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscPipelineTimingServiceTests
{
    [Fact]
    public void NewTimer_HasEmptySnapshot()
    {
        var timer = new AudioDiscPipelineTimingService();

        var snapshot = timer.GetSnapshot();

        Assert.Null(snapshot.StartedUtc);
        Assert.Null(snapshot.RipDuration);
        Assert.Null(snapshot.TotalDuration);
        Assert.False(snapshot.IsRunning);
    }

    [Fact]
    public void Start_CreatesRunningSnapshot()
    {
        var timer = new AudioDiscPipelineTimingService();

        timer.Start();
        var snapshot = timer.GetSnapshot();

        Assert.NotNull(snapshot.StartedUtc);
        Assert.Null(snapshot.RipDuration);
        Assert.NotNull(snapshot.TotalDuration);
        Assert.True(snapshot.IsRunning);
    }

    [Fact]
    public void Complete_RecordsRipAndTotalDuration()
    {
        var timer = new AudioDiscPipelineTimingService();

        timer.Start();
        timer.MarkRipCompleted();
        var snapshot = timer.Complete();

        Assert.NotNull(snapshot.RipDuration);
        Assert.NotNull(snapshot.TotalDuration);
        Assert.True(snapshot.TotalDuration >= snapshot.RipDuration);
        Assert.False(snapshot.IsRunning);
    }

    [Fact]
    public void CompleteWithoutStart_Throws()
    {
        var timer = new AudioDiscPipelineTimingService();

        Assert.Throws<InvalidOperationException>(() => timer.Complete());
    }

    [Fact]
    public void Reset_ClearsMeasurements()
    {
        var timer = new AudioDiscPipelineTimingService();
        timer.Start();
        timer.Complete();

        timer.Reset();
        var snapshot = timer.GetSnapshot();

        Assert.Null(snapshot.StartedUtc);
        Assert.Null(snapshot.RipDuration);
        Assert.Null(snapshot.TotalDuration);
        Assert.False(snapshot.IsRunning);
    }
}
