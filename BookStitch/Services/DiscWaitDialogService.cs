using System.Windows;

namespace BookStitch.Services;

public sealed record DiscWaitDialogRequest(
    int DiscNumber,
    int TotalDiscs,
    string MediaName,
    string InitialInstruction,
    string HintText,
    string DriveDisplayName = "",
    Action<DiscPollingDisplayState>? NotifyDisplayState = null,
    bool CanCompleteImport = false,
    bool IsDriveCircle = false);

/// <summary>
/// Compatibility entry point for the original dialog selector. Both settings now
/// use the common boxed dialog, including its duplicate and completion handling.
/// </summary>
public sealed class DiscWaitDialogService : IDiscWaitDialogService
{
    private readonly BoxedDiscWaitDialogService _dialog = new();

    public Task<DiscWaitDialogOutcome> WaitForDiscAsync(
        Window owner,
        DiscWaitDialogRequest request,
        Func<DiscWaitCheckRequest, CancellationToken, Task<DiscPollingResult>> checkDiscAsync,
        Action<string> setStatusText,
        Action<string> setProgressText,
        CancellationToken token) =>
        _dialog.WaitForDiscAsync(owner, request, checkDiscAsync, setStatusText, setProgressText, token);
}
