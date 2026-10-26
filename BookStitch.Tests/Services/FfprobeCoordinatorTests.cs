using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FfprobeCoordinatorTests
{
    [Fact]
    public async Task EnterAsync_EnforcesGlobalLimitAcrossConcurrentConsumers()
    {
        var coordinator = new FfprobeCoordinator(maximumConcurrency: 3);
        var current = 0;
        var maximumObserved = 0;

        var tasks = Enumerable.Range(0, 20)
            .Select(async _ =>
            {
                using var lease = await coordinator.EnterAsync(CancellationToken.None);
                var active = Interlocked.Increment(ref current);
                maximumObserved = Math.Max(maximumObserved, active);
                await Task.Delay(20);
                Interlocked.Decrement(ref current);
            });

        await Task.WhenAll(tasks);

        Assert.Equal(3, maximumObserved);
    }
}
