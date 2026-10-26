using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PerformanceEnvironmentContextServiceTests
{
    [Fact]
    public void Capture_ReturnsStableRuntimeAndProcessorContext()
    {
        var service = new PerformanceEnvironmentContextService();

        var snapshot = service.Capture();

        Assert.False(string.IsNullOrWhiteSpace(snapshot.OperatingSystem));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.OperatingSystemVersion));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.OperatingSystemArchitecture));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ProcessArchitecture));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.DotNetRuntime));

        Assert.NotNull(snapshot.LogicalProcessorCount);
        Assert.InRange(snapshot.LogicalProcessorCount!.Value, 1, int.MaxValue);

        Assert.NotNull(snapshot.ProcessWorkingSetBytes);
        Assert.InRange(snapshot.ProcessWorkingSetBytes!.Value, 0L, long.MaxValue);

        if (snapshot.PhysicalCoreCount.HasValue)
            Assert.InRange(snapshot.PhysicalCoreCount.Value, 1, int.MaxValue);

        if (snapshot.TotalPhysicalMemoryBytes.HasValue)
            Assert.InRange(snapshot.TotalPhysicalMemoryBytes.Value, 1L, long.MaxValue);

        if (snapshot.AvailablePhysicalMemoryBytes.HasValue)
            Assert.InRange(snapshot.AvailablePhysicalMemoryBytes.Value, 0L, long.MaxValue);

        if (snapshot.TotalPhysicalMemoryBytes.HasValue &&
            snapshot.AvailablePhysicalMemoryBytes.HasValue)
        {
            Assert.InRange(
                snapshot.AvailablePhysicalMemoryBytes.Value,
                0L,
                snapshot.TotalPhysicalMemoryBytes.Value);
        }

        if (snapshot.CpuModel is not null)
            Assert.False(string.IsNullOrWhiteSpace(snapshot.CpuModel));
    }

    [Fact]
    public void Capture_CreatesIndependentSnapshotsWithoutBackgroundState()
    {
        var service = new PerformanceEnvironmentContextService();

        var first = service.Capture();
        var second = service.Capture();

        Assert.NotSame(first, second);
        Assert.Equal(first.OperatingSystemArchitecture, second.OperatingSystemArchitecture);
        Assert.Equal(first.ProcessArchitecture, second.ProcessArchitecture);
        Assert.Equal(first.LogicalProcessorCount, second.LogicalProcessorCount);
    }
}