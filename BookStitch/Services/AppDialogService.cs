using BookStitch.Dialog;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace BookStitch.Services;

public static class AppDialogService
{
    public static AppDialogResult Show(
        Window owner,
        string title,
        string heading,
        string message,
        AppDialogKind kind = AppDialogKind.Information,
        IEnumerable<string>? details = null,
        IReadOnlyList<AppDialogButton>? buttons = null,
        double? width = null,
        double? height = null)
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

        dialog.ShowDialog();
        dialog.Activate();
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
                new AppDialogButton("Ja", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton("Nein", AppDialogResult.No, IsCancel: true)
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
                new AppDialogButton("Ja", AppDialogResult.Yes, IsPrimary: true, IsDefault: true),
                new AppDialogButton("Nein", AppDialogResult.No),
                new AppDialogButton("Abbrechen", AppDialogResult.Cancel, IsCancel: true)
            });
    }

    public static IReadOnlyList<string> LimitDetails(IEnumerable<string> details, int maxItems = 60)
    {
        var list = details.Where(item => !string.IsNullOrWhiteSpace(item)).ToList();

        if (list.Count <= maxItems)
            return list;

        return list
            .Take(maxItems)
            .Concat(new[] { $"… und {list.Count - maxItems} weitere Einträge" })
            .ToList();
    }
}
