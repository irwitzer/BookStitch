using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class OpticalMediaChangeMonitorTests
{
    [Fact]
    public void DecodeDriveRoots_MapsVolumeUnitMaskToStableDriveRoots()
    {
        var roots = OpticalMediaChangeMonitor.DecodeDriveRoots((1u << 3) | (1u << 6) | (1u << 25));

        Assert.Equal([@"D:\", @"G:\", @"Z:\"], roots);
    }

    [Fact]
    public void DecodeDriveRoots_EmptyMaskProducesNoDriveRoots()
    {
        Assert.Empty(OpticalMediaChangeMonitor.DecodeDriveRoots(0));
    }
}
