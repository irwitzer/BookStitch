using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FfmpegCancellationStartBoundaryTests
{
    [Fact]
    public async Task Already_canceled_work_never_attempts_to_launch_a_process()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FfmpegRunnerService().RunAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing"),
                [], cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }
}
