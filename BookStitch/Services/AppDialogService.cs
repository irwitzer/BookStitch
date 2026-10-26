using BookStitch.Dialog;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BookStitch.Services;

public static class AppDialogService
{
    public static bool ConfirmProjectUpdate(Window owner) => Show(owner,
        LocalizationText.Get("Integrity.Startup.Title"),LocalizationText.Get("Integrity.Startup.Title"),
        LocalizationText.Get("Integrity.Startup.Message"),AppDialogKind.Information,
        buttons:[new(LocalizationText.Get("Integrity.Startup.Now"),AppDialogResult.Yes,IsPrimary:true,IsDefault:true),
                 new(LocalizationText.Get("Integrity.Startup.Later"),AppDialogResult.Cancel,IsCancel:true)])==AppDialogResult.Yes;

    public static void CoverWarning(Window owner, string heading, string message,
        int thresholdPixels, Action<int> setThresholdPixels)
    {
        var dialog = new CoverSourceWarningDialog(heading, message);
        ModalDialogService.ShowDialog(dialog, owner);
        if (dialog.Result == AppDialogResult.Ok)
            setThresholdPixels(CoverWarningSettingsService.ResolveWarningChoice(thresholdPixels, dialog.AlwaysWarn));
    }

    public static AppDialogResult Show(
        Window owner,
        string title,
        string heading,
        string message,
        AppDialogKind kind = AppDialogKind.Information,
        IEnumerable<string>? details = null,
        IReadOnlyList<AppDialogButton>? buttons = null,
        double? width = null,
        double? height = null,
        bool restoreOwnerFocus = true)
    {
        var dialog = new AppMessageDialog(title, heading, message, kind, details, buttons)
        {
            Owner = owner,
            Topmost = false
        };

        if (width.HasValue)
            dialog.Width = Math.Max(dialog.MinWidth, width.Value);

        if (height.HasValue)
            dialog.Height = Math.Max(dialog.MinHeight, height.Value);

        ModalDialogService.ShowDialog(dialog, owner, restoreOwnerFocus: restoreOwnerFocus);
        return dialog.Result;
    }

    public static AppDialogResult Info(
        Window owner,
        string heading,
        string message,
        IEnumerable<string>? details = null,
        string title = "BookStitch")
    {
        return Show(
            owner,
            title,
            heading,
            message,
            AppDialogKind.Information,
            details,
            new[] { new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true) });
    }

    public static AppDialogResult Warning(
        Window owner,
        string heading,
        string message,
        IEnumerable<string>? details = null,
        string title = "BookStitch")
    {
        return Show(
            owner,
            title,
            heading,
            message,
            AppDialogKind.Warning,
            details,
            new[] { new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true) });
    }

    public static AppDialogResult Error(
        Window owner,
        string heading,
        string message,
        IEnumerable<string>? details = null,
        string title = "BookStitch")
    {
        return Show(
            owner,
            title,
            heading,
            message,
            AppDialogKind.Error,
            details,
            new[] { new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true) });
    }

    public static bool Confirm(
        Window owner,
        string heading,
        string message,
        IEnumerable<string>? details = null,
        string title = "BookStitch")
    {
        var result = Show(
            owner,
            title,
            heading,
            message,
            AppDialogKind.Question,
            details,
            new[]
            {
                new AppDialogButton(LocalizationText.Get("Common.Yes"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton(LocalizationText.Get("Common.No"), AppDialogResult.No, IsCancel: true)
            });

        return result == AppDialogResult.Yes;
    }

    public static AppDialogResult YesNoCancel(
        Window owner,
        string heading,
        string message,
        IEnumerable<string>? details = null,
        string title = "BookStitch")
    {
        return Show(
            owner,
            title,
            heading,
            message,
            AppDialogKind.Question,
            details,
            new[]
            {
                new AppDialogButton(LocalizationText.Get("Common.Yes"), AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton(LocalizationText.Get("Common.No"), AppDialogResult.No),
                new AppDialogButton(LocalizationText.Get("Common.Cancel"), AppDialogResult.Cancel, IsCancel: true)
            });
    }

    public static IReadOnlyList<string> LimitDetails(IEnumerable<string> details, int maxItems = 60)
    {
        var list = details.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();

        if (list.Count <= maxItems)
            return list;

        return list
            .Take(maxItems)
            .Concat(new[] { LocalizationText.Format("Common.MoreEntries", list.Count - maxItems) })
            .ToList();
    }
}
