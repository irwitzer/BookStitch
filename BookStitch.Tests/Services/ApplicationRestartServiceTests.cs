using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ApplicationRestartServiceTests
{
    [Fact]
    public void RequestedRestart_DoesNotRelaunchBeforeCloseIsConfirmed()
    {
        var launches = 0;
        var service = CreateService(() => launches++);

        service.RequestRestart();

        Assert.True(service.IsRestartRequested);
        Assert.False(service.IsRestartApproved);
        Assert.False(service.TryRelaunchOnExit());
        Assert.Equal(0, launches);
    }

    [Fact]
    public void CancelRestart_DiscardsIntentPermanently()
    {
        var launches = 0;
        var service = CreateService(() => launches++);
        service.RequestRestart();

        service.CancelRestart();
        service.ConfirmCloseCompleted();

        Assert.False(service.IsRestartRequested);
        Assert.False(service.TryRelaunchOnExit());
        Assert.Equal(0, launches);
    }

    [Fact]
    public void ConfirmedRestart_RelaunchesExactlyOnceOnExit()
    {
        var launches = 0;
        var service = CreateService(() => launches++);
        service.RequestRestart();
        service.ConfirmCloseCompleted();

        Assert.True(service.TryRelaunchOnExit());
        Assert.False(service.TryRelaunchOnExit());
        Assert.Equal(1, launches);
    }

    private static ApplicationRestartService CreateService(Action launch) =>
        new(() => "BookStitch.exe", _ => launch());
}
