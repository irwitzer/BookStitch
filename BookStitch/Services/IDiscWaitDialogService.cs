using System.Windows;

namespace BookStitch.Services;

public interface IDiscWaitDialogService
{
    Task<DiscWaitDialogOutcome> WaitForDiscAsync(
        Window owner,
        DiscWaitDialogRequest request,
        Func<DiscWaitCheckRequest, CancellationToken, Task<DiscPollingResult>> checkDiscAsync,
        Action<string> setStatusText,
        Action<string> setProgressText,
        CancellationToken token);
}
