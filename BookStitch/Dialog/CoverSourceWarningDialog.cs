using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using BookStitch.Services;

namespace BookStitch.Dialog;

public sealed class CoverSourceWarningDialog : AppMessageDialog
{
    public CheckBox AlwaysWarnToggle { get; }
    public bool AlwaysWarn => AlwaysWarnToggle.IsChecked == true;

    public CoverSourceWarningDialog(string heading, string message)
        : base("BookStitch", heading, message, AppDialogKind.Warning, null,
            [new(LocalizationText.Get("UiOK"), AppDialogResult.Ok, IsPrimary: true, IsDefault: true)])
    {
        AlwaysWarnToggle = new CheckBox
        {
            Content = LocalizationText.Get("Warnings.AlwaysWarn"),
            Style = (Style)FindResource("WarningToggleStyle"),
            IsChecked = true,
            Margin = new Thickness(0, 8, 0, 0)
        };
        AutomationProperties.SetName(AlwaysWarnToggle, LocalizationText.Get("Warnings.AlwaysWarn"));
        AdditionalContent.Content = AlwaysWarnToggle;
        AdditionalContent.Visibility = Visibility.Visible;
    }
}
