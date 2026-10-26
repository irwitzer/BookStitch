using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BookStitch.Services;

namespace BookStitch.Dialog;

/// <summary>A pre-start decision only; never starts media jobs or changes global settings.</summary>
public sealed class AacSampleRateDialog : AppMessageDialog
{
    public CheckBox PreferStandardToggle { get; }
    public TextBlock Explanation { get; }
    public bool PreferStandard => PreferStandardToggle.IsChecked == true;

    public AacSampleRateDialog(AacSampleRateDecision decision, string preset)
        : base("BookStitch", LocalizationText.Get(decision.FullPassThrough ? "Folder.PassThrough.Title" : "Aac.SampleRate.HighTitle"),
            decision.FullPassThrough
                ? LocalizationText.Format("Folder.PassThrough.Message", preset, FormatRate(decision.TargetRate))
                : LocalizationText.Format("Aac.SampleRate.HighMessage", FormatRate(decision.TargetRate)),
            AppDialogKind.Information, null,
            [new(LocalizationText.Get("UiOK"), AppDialogResult.Ok, IsPrimary: true, IsDefault: true),
             new(LocalizationText.Get("UiAbbrechen"), AppDialogResult.Cancel, IsCancel: true)])
    {
        Width = 720; Height = decision.TargetRate > 48000 ? 500 : 420;
        MinHeight = Height; MinWidth = 600;
        var content = new StackPanel();
        if (decision.FullPassThrough && decision.TargetRate > 48000)
            content.Children.Add(new TextBlock { Text = LocalizationText.Get("Aac.SampleRate.HighCompatibility"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        PreferStandardToggle = new CheckBox { Content = LocalizationText.Format("Aac.SampleRate.Toggle", FormatRate(decision.StandardRate)),
            Style = (Style)FindResource("SettingsToggleStyle"), IsChecked = false, Margin = new Thickness(0, 0, 0, 12),
            Visibility = decision.FullPassThrough && decision.TargetRate == decision.StandardRate
                ? Visibility.Collapsed : Visibility.Visible };
        AutomationProperties.SetName(PreferStandardToggle, PreferStandardToggle.Content.ToString());
        Explanation = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void Refresh() => Explanation.Text = PreferStandard && decision.StandardRate != decision.TargetRate
            ? LocalizationText.Format("Aac.SampleRate.ConvertExplanation", FormatRate(decision.StandardRate))
            : decision.FullPassThrough ? LocalizationText.Get("Aac.SampleRate.CopyExplanation")
            : LocalizationText.Format("Aac.SampleRate.MixedExplanation", FormatRate(decision.TargetRate));
        PreferStandardToggle.Checked += (_, _) => Refresh();
        PreferStandardToggle.Unchecked += (_, _) => Refresh();
        content.Children.Add(PreferStandardToggle); content.Children.Add(Explanation);
        AdditionalContent.Content = content; AdditionalContent.Visibility = Visibility.Visible;
        Refresh();
    }

    internal static string FormatRate(int rate) => rate.ToString("N0", CultureInfo.CurrentUICulture);
}
