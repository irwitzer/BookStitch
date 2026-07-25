using System.Runtime.InteropServices;

namespace BookStitch.Services;

public sealed class SystemSleepPreventionService : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    private bool _isActive;

    public void SetActive(bool active)
    {
        if (_isActive == active)
            return;

        _isActive = active;
        Apply(active);
    }

    public void Dispose()
    {
        SetActive(false);
    }

    private static void Apply(bool active)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var flags = active
            ? EsContinuous | EsSystemRequired
            : EsContinuous;

        SetThreadExecutionState(flags);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
