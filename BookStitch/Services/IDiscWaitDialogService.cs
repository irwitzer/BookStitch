using System.Windows;

namespace BookStitch.Services;

public enum DiscWaitDialogOutcome
{
    Ready,
    Deferred
}

public interface IDiscWaitDialogService
{
    Task<DiscWaitDialogOutcome> WaitForDiscAsync(
        Window owner,
        DiscWaitDialogRequest request,
        Func<CancellationToken, Task<DiscPollingResult>> checkDiscAsync,
        Action<string> setStatusText,
        Action<string> setProgressText,
        CancellationToken token);
}
