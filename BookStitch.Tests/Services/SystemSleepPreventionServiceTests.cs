using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SystemSleepPreventionServiceTests
{
    [Fact]
    public void SetActive_OnNonWindowsTracksRequestedStateWithoutCallingWindowsApi()
    {
        var callCount = 0;
        var service = new SystemSleepPreventionService(_ =>
        {
            callCount++;
            return 1;
        });

        var enabled = service.SetActive(true);
        var disabled = service.SetActive(false);

        Assert.True(enabled);
        Assert.True(disabled);
        Assert.False(service.IsActive);
        Assert.True(service.LastApplySucceeded);
        Assert.Equal(OperatingSystem.IsWindows() ? 2 : 0, callCount);
    }

    [Fact]
    public void SetActive_WhenWindowsApiFails_DoesNotMarkStateActive()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var service = new SystemSleepPreventionService(_ => 0);

        var enabled = service.SetActive(true);

        Assert.False(enabled);
        Assert.False(service.IsActive);
        Assert.False(service.LastApplySucceeded);
    }
}
