using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BookStitch.Dialog;

public enum AppDialogKind
{
    Information,
    Warning,
    Error,
    Question
}

public enum AppDialogResult
{
    None,
    Ok,
    Cancel,
    Yes,
    No
}

public sealed record AppDialogButton(
    string Text,
    AppDialogResult Result,
    bool IsPrimary = false,
    bool IsDefault = false,
    bool IsCancel = false,
    bool IsDanger = false,
    string? ClipboardText = null,
    bool ClosesDialog = true);

public partial class AppMessageDialog : Window
{
    public ObservableCollection<string> DetailItems { get; } = new();

    public AppDialogResult Result { get; private set; } = AppDialogResult.None;

    public AppMessageDialog(
        string title,
        string heading,
        string message,
        AppDialogKind kind,
        IEnumerable<string>? details,
        IReadOnlyList<AppDialogButton>? buttons)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        HeadingText.Text = heading;
        MessageText.Text = message;

        ApplyKind(kind);
        ApplyDetails(details);
        ApplyButtons(buttons);
    }

    private void ApplyKind(AppDialogKind kind)
    {
        switch (kind)
        {
            case AppDialogKind.Warning:
                IconText.Text = "!";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 203, 107));
                IconCircle.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 203, 107));
                IconCircle.Background = new SolidColorBrush(Color.FromRgb(42, 34, 16));
                break;

            case AppDialogKind.Error:
                IconText.Text = "×";
                IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 112, 112));
                IconCircle.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 112, 112));
                IconCircle.Background = new SolidColorBrush(Color.FromRgb(45, 20, 20));
                break;

            case AppDialogKind.Question:
                IconText.Text = "?";
                IconText.Foreground = FindResource("AccentSoftBrush") as Brush;
                IconCircle.BorderBrush = FindResource("AccentSoftBrush") as Brush;
                break;

            default:
                IconText.Text = "i";
                IconText.Foreground = FindResource("AccentSoftBrush") as Brush;
                IconCircle.BorderBrush = FindResource("AccentSoftBrush") as Brush;
                break;
        }
    }

    private void ApplyDetails(IEnumerable<string>? details)
    {
        DetailItems.Clear();

        foreach (var detail in details ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(detail))
                DetailItems.Add(detail.TrimEnd());
        }

        DetailsList.ItemsSource = DetailItems;
        DetailsBorder.Visibility = DetailItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyButtons(IReadOnlyList<AppDialogButton>? buttons)
    {
        ButtonsPanel.Children.Clear();

        var effectiveButtons = buttons is { Count: > 0 }
            ? buttons
            : new[] { new AppDialogButton("OK", AppDialogResult.Ok, IsPrimary: true, IsDefault: true) };

        Button? defaultButton = null;
        for (var index = 0; index < effectiveButtons.Count; index++)
        {
            var dialogButton = effectiveButtons[index];
            var button = new Button
            {
                Content = dialogButton.Text,
                MinWidth = 96,
                Height = 34,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = dialogButton.IsDefault,
                IsCancel = dialogButton.IsCancel,
                Style = FindResource(
                    dialogButton.IsDanger
                        ? "DangerButtonStyle"
                        : dialogButton.IsPrimary
                            ? "PrimaryButtonStyle"
                            : "DialogSecondaryButtonStyle") as Style,
                Tag = dialogButton.Result,
                DataContext = dialogButton,
                TabIndex = index
            };

            button.Click += DialogButton_Click;
            ButtonsPanel.Children.Add(button);
            if (dialogButton.IsDefault)
                defaultButton = button;
        }

        if (defaultButton is not null)
            Loaded += (_, _) => defaultButton.Focus();
    }

    private void DialogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.DataContext is AppDialogButton dialogButton &&
            !string.IsNullOrWhiteSpace(dialogButton.ClipboardText))
        {
            Clipboard.SetText(dialogButton.ClipboardText);
            if (!dialogButton.ClosesDialog)
                return;
        }

        if (button.Tag is AppDialogResult result)
            Result = result;

        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Result = AppDialogResult.Cancel;
        DialogResult = false;
        Close();
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Result = AppDialogResult.Cancel;
        DialogResult = false;
        Close();
    }

}
