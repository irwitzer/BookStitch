using System.Windows;
using System.Windows.Threading;

namespace BookStitch.Services;

public sealed class DialogOwnerFocusService
{
    private readonly Action<Window> _activateOwner;

    public static DialogOwnerFocusService Default { get; } = new();

    public DialogOwnerFocusService()
        : this(window =>
        {
            window.Activate();
            window.Focus();
        })
    {
    }

    internal DialogOwnerFocusService(Action<Window> activateOwner)
    {
        _activateOwner = activateOwner;
    }

    public void RestoreOwnerAfterModalDialog(Window? owner)
    {
        if (!CanRestore(owner))
            return;

        var restoreOwner = owner!;

        try
        {
            restoreOwner.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (!CanRestore(restoreOwner))
                        return;

                    try
                    {
                        _activateOwner(restoreOwner);
                    }
                    catch
                    {
                        // Focus restore must never replace the dialog result or shutdown path.
                    }
                }),
                DispatcherPriority.ContextIdle);
        }
        catch
        {
            // Dispatcher shutdown or focus restore failures are intentionally non-fatal.
        }
    }

    private static bool CanRestore(Window? owner)
    {
        if (owner is null ||
            !owner.IsVisible ||
            owner.Dispatcher.HasShutdownStarted ||
            owner.Dispatcher.HasShutdownFinished)
        {
            return false;
        }

        return true;
    }
}
