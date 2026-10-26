using BookStitch.Services;
using BookStitch.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace BookStitch.Controls;

public partial class SettingsConversionControl : UserControl
{
    private GlobalSettingsManager? _settings;
    public SettingsConversionControl() => InitializeComponent();
    // Presentation only: reuse the selected preset model without changing settings or workflow state.
    private void ExportPresetDescription_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (ExportPresetComboBox.SelectedItem is not string selected)
        {
            ExportPresetDescription.Text = string.Empty;
            return;
        }
        var preset = ExportPreset.Parse(selected);
        string? key = (preset.Channels, preset.BitrateKbps) switch
        {
            (1, 64) => "Settings.Conversion.Preset.Mono64",
            (1, 96) => "Settings.Conversion.Preset.Mono96",
            (2, 96) => "Settings.Conversion.Preset.Stereo96",
            (2, 128) => "Settings.Conversion.Preset.Stereo128",
            (2, 160) => "Settings.Conversion.Preset.Stereo160",
            (2, 192) => "Settings.Conversion.Preset.Stereo192",
            (2, 256) => "Settings.Conversion.Preset.Stereo256",
            _ => null
        };
        ExportPresetDescription.Text = key is null ? string.Empty : LocalizationText.Get(key);
    }
    public void Initialize(GlobalSettingsManager settings)
    {
        StandardSampleRateComboBox.ItemsSource = new[] { 44100, 48000 }.Select(rate =>
            new ComboBoxItem { Content = rate.ToString("N0", CultureInfo.CurrentUICulture) + " Hz", Tag = rate }).ToArray();
        StandardSampleRateComboBox.SelectedValue = SampleRateSettingsService.Normalize(settings.Current.DefaultSampleRateHz);
        _settings = settings;
    }
    private void StandardSampleRate_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (_settings is null) return;
        _settings.Current.DefaultSampleRateHz = SampleRateSettingsService.Normalize(
            StandardSampleRateComboBox.SelectedValue is int rate ? rate : 44100);
        _settings.SaveImmediate();
    }
}
