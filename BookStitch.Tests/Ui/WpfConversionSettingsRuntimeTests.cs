using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData("AAC Mono 64 kbps", "Mono64")]
    [InlineData("AAC Mono 96 kbps", "Mono96")]
    [InlineData("AAC Stereo 96 kbps", "Stereo96")]
    [InlineData("AAC Stereo 128 kbps", "Stereo128")]
    [InlineData("AAC Stereo 160 kbps", "Stereo160")]
    [InlineData("AAC Stereo 192 kbps", "Stereo192")]
    [InlineData("AAC Stereo 256 kbps", "Stereo256")]
    public void Conversion_help_tracks_initial_and_replaced_presets_without_mutating_shared_state(string initialPreset, string suffix)
    {
        RunOnStaThread(() =>
        {
            var model = new ConversionBindingFixture { SelectedExportPreset = initialPreset };
            var view = new SettingsConversionControl();
            var otherView = new SettingsConversionControl { DataContext = model };
            var replacements = 0;
            // Same initialization guard as the canonical MainWindow wiring, protected by the source contract.
            view.ExportPresetComboBox.SelectionChanged += (_, args) =>
            {
                if (args.RemovedItems.Count > 0) replacements++;
            };
            view.DataContext = model;
            DrainDispatcher();
            Assert.Equal(0, replacements);
            Assert.Equal(initialPreset, model.SelectedExportPreset);
            Assert.Equal(LocalizationText.Get("Settings.Conversion.Preset." + suffix), view.ExportPresetDescription.Text);

            var cases = new[] { "Mono64", "Mono96", "Stereo96", "Stereo128", "Stereo160", "Stereo192", "Stereo256" };
            for (var index = 0; index < ExportPreset.AvailableDisplayNames.Count; index++)
            {
                // Change from the source, then from the additional settings view: both use the same owner.
                model.SelectedExportPreset = ExportPreset.AvailableDisplayNames[index];
                DrainDispatcher();
                Assert.Equal(LocalizationText.Get("Settings.Conversion.Preset." + cases[index]), view.ExportPresetDescription.Text);
                view.ExportPresetComboBox.SelectedItem = initialPreset;
                DrainDispatcher();
                Assert.Equal(initialPreset, model.SelectedExportPreset);
                Assert.Equal(initialPreset, otherView.ExportPresetComboBox.SelectedItem);
                Assert.Equal(LocalizationText.Get("Settings.Conversion.Preset." + suffix), view.ExportPresetDescription.Text);
            }
            Assert.True(replacements > 0);
            view.ParallelJobsTextBox.Text = "3";
            view.ParallelJobsTextBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            DrainDispatcher();
            Assert.Equal("3", model.ParallelJobsInput);
            Assert.Equal("3", otherView.ParallelJobsTextBox.Text);
            Assert.True(view.IncreaseParallelJobsButton.IsEnabled);
            Assert.True(view.DecreaseParallelJobsButton.IsEnabled);
        });
    }

    [Theory]
    [InlineData("de")][InlineData("en")][InlineData("bn-BD")][InlineData("cs")]
    [InlineData("da")][InlineData("es")][InlineData("fi")][InlineData("fr")]
    [InlineData("gsw-CH")][InlineData("it")][InlineData("ja")][InlineData("nb")]
    [InlineData("nl")][InlineData("pl")][InlineData("pt-BR")][InlineData("ru")]
    [InlineData("sv")][InlineData("tr")][InlineData("vi")][InlineData("zh-CN")][InlineData("zh-HK")]
    public void Conversion_form_measures_all_languages_at_minimum_window_width(string locale)
    {
        RunOnStaThread(() =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                RunWithSettingsWindow(new AppSettings(), dialog =>
                {
                    var view = dialog.ConversionSettingsControl;
                    view.DataContext = new ConversionBindingFixture();
                    dialog.Width = dialog.MinWidth;
                    dialog.Height = dialog.MinHeight;
                    dialog.Show();
                    DrainDispatcher();
                    dialog.UpdateLayout();
                    var grid = view.ExportInputsPanel;
                    var preset = view.ExportPresetComboBox;
                    var parallel = view.ParallelJobsInputs;
                    var rate = view.StandardSampleRateComboBox;
                    double X(FrameworkElement e) => e.TranslatePoint(new Point(), grid).X;
                    Assert.InRange(System.Math.Abs(X(preset) - X(parallel)), 0, 0.1);
                    Assert.InRange(System.Math.Abs(X(preset) - X(rate)), 0, 0.1);
                    foreach (var input in new FrameworkElement[] { preset, parallel, rate })
                    {
                        Assert.True(input.ActualWidth > 0);
                        Assert.True(X(input) + input.ActualWidth <= grid.ActualWidth + 0.1, locale);
                        Assert.True(input.ActualWidth < grid.ActualWidth / 2, locale);
                    }
                    Assert.True(preset.ActualWidth > rate.ActualWidth);
                    foreach (var label in new FrameworkElement[] { view.OpenWorkFolderButton, view.OpenTaskManagerButton, view.SampleRateLabel })
                    {
                        Assert.True(label.ActualWidth > 0);
                        Assert.True(X(label) + label.ActualWidth <= X(preset), locale);
                    }
                    foreach (var text in new[] { view.ExportPresetDescription, view.ParallelJobsDescription,
                                 view.SampleRateDescription, view.PassThroughDescription })
                    {
                        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
                        Assert.False(string.IsNullOrWhiteSpace(text.Text));
                        Assert.InRange(System.Math.Abs(text.ActualWidth - grid.ActualWidth), 0, 0.1);
                        Assert.InRange(X(text), 0, 0.1);
                    }
                    foreach (var pair in new[] { (input: (FrameworkElement)preset, help: view.ExportPresetDescription),
                                 (input: (FrameworkElement)parallel, help: view.ParallelJobsDescription),
                                 (input: (FrameworkElement)rate, help: view.SampleRateDescription) })
                        Assert.True(pair.help.TranslatePoint(new Point(), grid).Y >=
                            pair.input.TranslatePoint(new Point(), grid).Y + pair.input.ActualHeight, locale);
                    Assert.True(view.SampleRateDescription.ActualHeight > view.SampleRateDescription.FontSize * 2);
                    Assert.True(view.PassThroughHeading.TranslatePoint(new Point(), grid).Y >
                        view.SampleRateDescription.TranslatePoint(new Point(), grid).Y + view.SampleRateDescription.ActualHeight);
                    Assert.EndsWith(locale is "ja" or "zh-CN" or "zh-HK" ? "：" : ":", view.SampleRateLabel.Text);
                    // Both controls use the same form inset; this is also checked structurally without WPF.
                    Assert.Equal((Thickness)view.FindResource("SettingsFormMargin"), grid.Margin);
                }, initialTabKey: "Conversion");
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        });
    }

    private sealed class ConversionBindingFixture : INotifyPropertyChanged
    {
        private string _preset = ExportPreset.DefaultDisplayName;
        private string _jobs = "Auto";
        public event PropertyChangedEventHandler? PropertyChanged;
        public System.Collections.Generic.IReadOnlyList<string> ExportPresets => ExportPreset.AvailableDisplayNames;
        public bool CanChangeExportPreset => true;
        public bool CanChangeExportOptions => true;
        public string SelectedExportPreset
        {
            get => _preset;
            set { _preset = value; PropertyChanged?.Invoke(this, new(nameof(SelectedExportPreset))); }
        }
        public string ParallelJobsInput
        {
            get => _jobs;
            set { _jobs = value; PropertyChanged?.Invoke(this, new(nameof(ParallelJobsInput))); }
        }
    }
}
