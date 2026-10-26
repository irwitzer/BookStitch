using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.Services;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Narrated_question_starts_only_when_selected_and_stops_on_other_question_or_category()
    {
        RunOnStaThread(() => RunWithSettingsWindow(new AppSettings(), window =>
        {
            window.Show();
            DrainDispatcher();
            var faq = (SettingsFaqControl)window.FindName("FaqSettingsControl");
            var combo = (ComboBox)faq.FindName("QuestionsComboBox");
            var narration = (FaqNarrationControl)faq.FindName("AudibleNarration");
            Assert.False(narration.IsPlaying);
            var entry = combo.Items.Cast<FaqEntry>().Single(item => item.Narration is not null);
            combo.SelectedItem = entry;
            Assert.True(narration.IsPlaying);
            Assert.Equal(Visibility.Collapsed, ((ItemsControl)faq.FindName("AnswerSections")).Visibility);
            Assert.Same(FaqSidebarImageService.GetImage(entry.SidebarImage), ((Image)faq.FindName("FaqSidebarImage")).Source);
            combo.SelectedIndex = 0; // Stop before dispatching actual device-open callbacks.
            Assert.False(narration.IsPlaying);
            Assert.Equal(Visibility.Visible, ((ItemsControl)faq.FindName("AnswerSections")).Visibility);
            combo.SelectedItem = entry;
            var navigation = (ListBox)window.FindName("SettingsNavigationList");
            navigation.SelectedIndex = 0;
            Assert.False(narration.IsPlaying);
            DrainDispatcher();
        }, initialTabKey: "FAQ"));
    }

    [Theory]
    [InlineData("Black")]
    [InlineData("Ivory")]
    public void Narration_follows_media_position_pauses_resumes_fades_replays_and_stops_on_close(string background)
    {
        RunOnStaThread(() =>
        {
            AppearanceSettingsService.ApplyToResources(Application.Current.Resources, new AppSettings { BackgroundPreset = background });
            var player = new TestNarrationPlayer();
            var control = new FaqNarrationControl(player) { ImageWidth = 480 };
            var entry = FaqCatalog.LoadEnglish().Single(item => item.Narration is not null);
            control.Configure(entry);
            var window = new Window { Width = 620, Height = 700, Content = control };
            try
            {
                window.Show();
                DrainDispatcher();
                Assert.True(control.IsPlaying);
                Assert.True(File.Exists(player.Source!.LocalPath));
                var text = (TextBlock)control.FindName("NarrationText");
                var image = (Image)control.FindName("FinalImage");
                Assert.Equal(Visibility.Collapsed, image.Visibility);
                player.Position = TimeSpan.FromSeconds(5);
                control.RefreshPlayback();
                var prefix = text.Text;
                Assert.StartsWith(prefix, FaqNarrationTests.ApprovedText);
                Assert.InRange(prefix.Length, 20, FaqNarrationTests.ApprovedText.Length - 1);
                control.TogglePlayback();
                Assert.False(control.IsPlaying);
                Assert.Equal(1, player.Pauses);
                player.Position = TimeSpan.FromSeconds(12);
                control.RefreshPlayback();
                Assert.Equal(prefix, text.Text);
                control.TogglePlayback();
                control.RefreshPlayback();
                Assert.True(control.IsPlaying);
                Assert.Equal(1, player.Opens);
                Assert.True(text.Text.Length > prefix.Length);
                player.Finish();
                Assert.Equal(FaqNarrationTests.ApprovedText, text.Text);
                Assert.Equal(Visibility.Visible, image.Visibility);
                control.TogglePlayback();
                var opacity = image.Opacity;
                control.RefreshPlayback();
                Assert.Equal(opacity, image.Opacity);
                control.TogglePlayback();
                WaitForChat(() => !control.IsPlaying);
                Assert.Equal(1, image.Opacity);
                DrainDispatcher();
                Assert.Equal(480, image.ActualWidth, 2);
                Assert.Equal(image.ActualWidth, image.ActualHeight, 2);
                Assert.Equal(28, Assert.IsType<RectangleGeometry>(image.Clip).RadiusX);
                control.TogglePlayback();
                Assert.Equal(2, player.Opens);
                Assert.Equal(Visibility.Collapsed, image.Visibility);
                Assert.True(text.Text.Length < FaqNarrationTests.ApprovedText.Length);
                control.Configure(null); // Same lifecycle operation as switching FAQ entries.
                Assert.False(control.IsPlaying);
                Assert.Empty(text.Text);
                player.Finish(); // A late notification from the old session cannot reveal content.
                Assert.Equal(Visibility.Collapsed, image.Visibility);
                control.Configure(entry);
                Assert.True(control.IsPlaying);
                window.Close();
                DrainDispatcher();
                Assert.False(control.IsPlaying);
                Assert.True(player.Closes >= 3);
            }
            finally
            {
                window.Close();
                AppearanceSettingsService.ApplyToResources(Application.Current.Resources, new AppSettings());
            }
        });
    }

    [Fact]
    public void Narration_media_failure_leaves_complete_readable_answer_and_replay()
    {
        RunOnStaThread(() =>
        {
            var player = new TestNarrationPlayer();
            var control = new FaqNarrationControl(player);
            control.Configure(FaqCatalog.LoadEnglish().Single(item => item.Narration is not null));
            var window = new Window { Content = control, Width = 620, Height = 700 };
            try
            {
                window.Show();
                DrainDispatcher();
                player.Fail();
                Assert.False(control.IsPlaying);
                Assert.Equal(FaqNarrationTests.ApprovedText, ((TextBlock)control.FindName("NarrationText")).Text);
                Assert.Equal(Visibility.Visible, ((Image)control.FindName("FinalImage")).Visibility);
                control.TogglePlayback();
                Assert.True(control.IsPlaying);
            }
            finally { window.Close(); }
        });
    }

    private sealed class TestNarrationPlayer : IFaqNarrationPlayer
    {
        public event EventHandler? Opened;
        public event EventHandler? Ended;
        public event EventHandler? Failed;
        public TimeSpan Position { get; set; }
        public int Opens { get; private set; }
        public int Pauses { get; private set; }
        public int Closes { get; private set; }
        public Uri? Source { get; private set; }
        public void Open(Uri source) { Source = source; Position = TimeSpan.Zero; Opens++; Opened?.Invoke(this, EventArgs.Empty); }
        public void Play() { }
        public void Pause() => Pauses++;
        public void Close() => Closes++;
        public void Finish() => Ended?.Invoke(this, EventArgs.Empty);
        public void Fail() => Failed?.Invoke(this, EventArgs.Empty);
    }
}
