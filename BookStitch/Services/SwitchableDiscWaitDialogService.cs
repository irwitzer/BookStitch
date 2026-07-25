using System.Windows;

namespace BookStitch.Services;

/// <summary>
/// Zentrale Umschaltstelle zwischen bewährtem Legacy-Dialog und neuer Boxed-Variante.
/// Beide Implementierungen müssen erhalten bleiben. Der Legacy-Dialog ist ein
/// absichtlicher produktiver Fallback und kein ungenutzter Doppelcode.
/// </summary>
public sealed class SwitchableDiscWaitDialogService : IDiscWaitDialogService
{
    private readonly Func<bool> _useBoxedDialog;
    private readonly IDiscWaitDialogService _legacy = new DiscWaitDialogService();
    private readonly IDiscWaitDialogService _boxed = new BoxedDiscWaitDialogService();

    public SwitchableDiscWaitDialogService(Func<bool> useBoxedDialog)
    {
        _useBoxedDialog = useBoxedDialog ?? throw new ArgumentNullException(nameof(useBoxedDialog));
    }

    public Task<DiscWaitDialogOutcome> WaitForDiscAsync(
        Window owner, DiscWaitDialogRequest request,
        Func<CancellationToken, Task<DiscPollingResult>> checkDiscAsync,
        Action<string> setStatusText, Action<string> setProgressText,
        CancellationToken token)
        => (_useBoxedDialog() ? _boxed : _legacy).WaitForDiscAsync(
            owner, request, checkDiscAsync, setStatusText, setProgressText, token);
}
