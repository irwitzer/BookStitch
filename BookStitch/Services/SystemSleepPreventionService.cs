using System.Runtime.InteropServices;

namespace BookStitch.Services;

public sealed class SystemSleepPreventionService : IDisposable
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    private readonly Func<uint, uint> _setThreadExecutionState;
    private bool _isActive;

    public SystemSleepPreventionService()
        : this(SetThreadExecutionState)
    {
    }

    public SystemSleepPreventionService(Func<uint, uint> setThreadExecutionState)
    {
        _setThreadExecutionState = setThreadExecutionState ?? throw new ArgumentNullException(nameof(setThreadExecutionState));
    }

    public bool IsActive => _isActive;

    public bool LastApplySucceeded { get; private set; } = true;

    public bool SetActive(bool active)
    {
        if (_isActive == active)
            return LastApplySucceeded;

        LastApplySucceeded = Apply(active);
        if (LastApplySucceeded)
            _isActive = active;

        return LastApplySucceeded;
    }

    public void Dispose()
    {
        SetActive(false);
    }

    private bool Apply(bool active)
    {
        if (!OperatingSystem.IsWindows())
            return true;

        var flags = active
            ? EsContinuous | EsSystemRequired
            : EsContinuous;

        return _setThreadExecutionState(flags) != 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);
}
