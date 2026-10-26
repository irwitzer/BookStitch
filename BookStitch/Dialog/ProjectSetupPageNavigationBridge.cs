using System;

namespace BookStitch.Dialog;

internal static class ProjectSetupPageNavigationBridge
{
    private static WeakReference<DiscProjectSetupDialog>? _activeDialog;

    public static event EventHandler? StateChanged;

    public static bool CanNavigate(int direction) =>
        TryGetActive(out var dialog) && dialog.CanNavigateSetupPage(direction);

    public static bool TryNavigate(int direction) =>
        TryGetActive(out var dialog) && dialog.NavigateSetupPage(direction);

    internal static void Register(DiscProjectSetupDialog dialog)
    {
        _activeDialog = new WeakReference<DiscProjectSetupDialog>(dialog);
        StateChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static void Unregister(DiscProjectSetupDialog dialog)
    {
        if (TryGetActive(out var current) && ReferenceEquals(current, dialog))
            _activeDialog = null;
        StateChanged?.Invoke(null, EventArgs.Empty);
    }

    internal static void NotifyNavigationStateChanged() =>
        StateChanged?.Invoke(null, EventArgs.Empty);

    private static bool TryGetActive(out DiscProjectSetupDialog dialog)
    {
        if (_activeDialog is not null && _activeDialog.TryGetTarget(out dialog!))
            return true;
        dialog = null!;
        _activeDialog = null;
        return false;
    }
}
