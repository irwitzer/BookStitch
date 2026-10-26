using System.Windows;

namespace BookStitch.Services;

public static class ModalDialogService
{
    public static bool? ShowDialog(
        Window dialog,
        Window owner,
        DialogOwnerFocusService? focusService = null,
        bool restoreOwnerFocus = true)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(owner);

        if (!ReferenceEquals(dialog.Owner, owner))
            dialog.Owner = owner;

        try
        {
            return dialog.ShowDialog();
        }
        finally
        {
            if (restoreOwnerFocus)
                (focusService ?? DialogOwnerFocusService.Default)
                    .RestoreOwnerAfterModalDialog(owner);
        }
    }
}
