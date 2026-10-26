using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BookStitch.Dialog;

// Shares app-message chrome, warning presentation, buttons and keyboard handling.
public sealed class DiscCountConfirmationDialog : AppMessageDialog
{
    private readonly DiscCountConfirmationContent _input = new();
    public DiscCountInputControl DiscCountInput => _input.DiscCountInput;
    public CheckBox AlwaysWarnToggle => _input.AlwaysWarnToggle;
    public TextBlock Question => HeadingText;
    public TextBlock ErrorText => _input.ErrorText;
    public Button BackButton => (Button)ButtonsPanel.Children[0];
    public Button OkButton => (Button)ButtonsPanel.Children[1];
    public bool AlwaysWarn => AlwaysWarnToggle.IsChecked == true;

    public DiscCountConfirmationDialog(DiscCountInputState state)
        : base(LocalizationText.Get("Disc.Count.ConfirmTitle"), BuildQuestion(state), string.Empty,
            AppDialogKind.Warning, null,
            [new(LocalizationText.Get("Disc.Count.Back"), AppDialogResult.Cancel, IsCancel: true),
             new(LocalizationText.Get("UiOK"), AppDialogResult.Ok, IsPrimary: true, IsDefault: true)])
    {
        DiscCountInput.DataContext = state;
        AdditionalContent.Content = _input;
        AdditionalContent.Visibility = Visibility.Visible;
        MessageText.Visibility = Visibility.Collapsed;
        ButtonsPanel.HorizontalAlignment = HorizontalAlignment.Left;
        Width = 480;
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override bool TryAcceptResult(AppDialogResult result)
    {
        if (result != AppDialogResult.Ok)
            return true;
        DiscCountInput.Input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (DiscCountInput.State.TryGetValue(out _))
            return true;
        _input.ErrorText.Text = LocalizationText.Format("Disc.Setup.DiscCountRange", DiscCountInput.State.Minimum, DiscCountInput.State.Maximum);
        _input.ErrorText.Visibility = Visibility.Visible;
        DiscCountInput.FocusInput();
        return false;
    }

    private static string BuildQuestion(DiscCountInputState state)
    {
        state.TryGetValue(out var count);
        return count == 1 ? LocalizationText.Get("Disc.Count.ConfirmOne") : LocalizationText.Format("Disc.Count.ConfirmMany", count);
    }
}
