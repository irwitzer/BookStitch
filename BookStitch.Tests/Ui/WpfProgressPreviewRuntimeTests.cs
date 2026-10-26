using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Pearl_background_is_offered_only_while_developer_access_is_enabled_and_refreshes_live()
    {
        RunOnStaThread(() =>
        {
            var access = DeveloperToolsCommands.AccessState;
            access.Initialize(new AppSettings(), BookStitchEnvironment.Production, () => true);
            var settings = new AppSettings { BackgroundPreset = nameof(BackgroundPreset.PearlStone) };
            try
            {
                RunWithSettingsWindow(settings, window =>
                {
                    window.Show();
                    DrainDispatcher();
                    var general = (SettingsGeneralControl)window.FindName("GeneralSettingsControl");
                    var appearance = (SettingsAppearanceControl)general.FindName("AppearanceSettingsControl");
                    var choices = (ComboBox)appearance.FindName("BackgroundPresetComboBox");
                    bool HasPearl() => choices.Items.OfType<ComboBoxItem>().Any(item =>
                        (string?)item.Tag == nameof(BackgroundPreset.PearlStone));
                    Assert.False(HasPearl());
                    for (var click = 0; click < 10; click++) access.RegisterDiscoveryClick();
                    DrainDispatcher();
                    Assert.True(HasPearl()); // Discovery enables access while keeping wrench icons hidden.
                    access.VisibilityChoice = DeveloperToolsVisibilityChoice.Show;
                    DrainDispatcher();
                    Assert.True(HasPearl());
                    access.VisibilityChoice = DeveloperToolsVisibilityChoice.Hide;
                    DrainDispatcher();
                    Assert.True(HasPearl());
                    access.VisibilityChoice = DeveloperToolsVisibilityChoice.Conceal;
                    DrainDispatcher();
                    Assert.False(HasPearl());
                    Assert.Equal(nameof(BackgroundPreset.Black), settings.BackgroundPreset);
                    Assert.Contains(BackgroundPresetCatalog.All, preset => preset.Preset == BackgroundPreset.PearlStone);
                });
            }
            finally { access.Initialize(new AppSettings(), BookStitchEnvironment.Production, () => false); }
        });
    }

    private static PreviewProgressBar CreatePreviewProgress() => new()
    {
        Width = 220, Height = 14, Minimum = 0, Maximum = 100,
        Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x85, 0x8A))
    };

    [Fact]
    public void Solid_progress_uses_the_standard_template_and_tracks_the_fill_value()
    {
        RunOnStaThread(() =>
        {
            var bar = CreatePreviewProgress();
            var host = new Window { Content = bar, Width = 300, Height = 100, ShowInTaskbar = false };
            try
            {
                host.Show();
                DrainDispatcher();
                var track = (FrameworkElement)bar.Template.FindName("PART_Track", bar);
                var indicator = (FrameworkElement)bar.Template.FindName("PART_Indicator", bar);
                foreach (var percent in new[] { 0d, 25d, 50d, 100d })
                {
                    bar.Value = percent;
                    host.UpdateLayout();
                    DrainDispatcher();
                    Assert.Equal(track.ActualWidth * percent / 100, indicator.ActualWidth, 3);
                }
                bar.IsIndeterminate = true;
                host.UpdateLayout();
                Assert.Equal(track.ActualWidth, indicator.ActualWidth, 3);
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void Progress_preview_holds_full_then_restores_current_bound_progress_without_changing_source()
    {
        RunOnStaThread(() =>
        {
            var source = new Slider { Minimum = 0, Maximum = 100, Value = 25 };
            var bar = CreatePreviewProgress();
            bar.SetBinding(RangeBase.ValueProperty, new Binding("Value") { Source = source });
            bar.IsIndeterminate = true;
            var panel = new StackPanel { Visibility = Visibility.Collapsed };
            panel.Children.Add(bar);
            var host = new Window { Content = panel, Width = 300, Height = 100, ShowInTaskbar = false };
            try
            {
                host.Show();
                bar.StartPreview(1, true, panel);
                source.Value = 65;
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
                Assert.Equal(100, bar.Value);
                Assert.Equal(Color.FromRgb(0xD9, 0xA4, 0x41), Assert.IsType<SolidColorBrush>(bar.Foreground).Color);
                Assert.False(bar.IsIndeterminate);
                Assert.Equal(Visibility.Visible, panel.Visibility);
                Assert.Equal(65, source.Value);
                bar.StopPreview();
                Assert.Equal(65, bar.Value);
                Assert.Equal(Color.FromRgb(0x16, 0x85, 0x8A), Assert.IsType<SolidColorBrush>(bar.Foreground).Color);
                Assert.True(bar.IsIndeterminate);
                Assert.Equal(Visibility.Collapsed, panel.Visibility);
                Assert.NotNull(BindingOperations.GetBindingExpression(bar, RangeBase.ValueProperty));
                Assert.Throws<ArgumentOutOfRangeException>(() => bar.StartPreview(0, false));
            }
            finally { host.Close(); }
        });
    }
}
