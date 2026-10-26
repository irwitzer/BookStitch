using System.Diagnostics;

namespace BookStitch.Services;

public sealed class ApplicationRestartService
{
    private readonly Func<string?> _processPathProvider;
    private readonly Action<string> _startProcess;
    private bool _restartRequested;
    private bool _restartApproved;
    private bool _relaunchAttempted;

    public ApplicationRestartService()
        : this(
            () => Environment.ProcessPath,
            path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }))
    {
    }

    internal ApplicationRestartService(
        Func<string?> processPathProvider,
        Action<string> startProcess)
    {
        _processPathProvider = processPathProvider ?? throw new ArgumentNullException(nameof(processPathProvider));
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public bool IsRestartRequested => _restartRequested;

    public bool IsRestartApproved => _restartApproved;

    public void RequestRestart()
    {
        _restartRequested = true;
        _restartApproved = false;
    }

    public void CancelRestart()
    {
        _restartRequested = false;
        _restartApproved = false;
    }

    public void ConfirmCloseCompleted()
    {
        if (_restartRequested)
            _restartApproved = true;
    }

    public bool TryRelaunchOnExit()
    {
        if (!_restartApproved || _relaunchAttempted)
            return false;

        _relaunchAttempted = true;
        var processPath = _processPathProvider();
        if (string.IsNullOrWhiteSpace(processPath))
            return false;

        _startProcess(processPath);
        return true;
    }
}
