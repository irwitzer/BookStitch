using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.IO;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    private static AacDecisionPlan CompactSample(int reused = 8, int converted = 4)
    {
        var plan = new AacDecisionPlan { EffectivePreset = "AAC Stereo 96 kbps", Channels = 2, BitrateKbps = 96, SampleRateHz = 48000 };
        for (var i = 0; i < reused + converted; i++)
            plan.Tracks.Add(new(Guid.NewGuid(), i, i < reused, i < reused ? null : AacConversionReason.SampleRate,
                (long)((i < reused ? 5.25 : 2.75) * TimeSpan.TicksPerMinute)));
        return plan;
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(1, 0)] [InlineData(8, 4)]
    public void Aac_card_omits_zero_rows_and_uses_exact_plan_sums_before_display_rounding(int reused, int converted)
    {
        RunOnStaThread(() => WithSettingsCulture("de", () =>
        {
            var plan = CompactSample(reused, converted);
            var dialog = new AacDecisionDialog(new(plan, plan, plan));
            try
            {
                Assert.Equal(reused == 0 ? Visibility.Collapsed : Visibility.Visible, dialog.ReuseSummary.Visibility);
                Assert.Equal(converted == 0 ? Visibility.Collapsed : Visibility.Visible, dialog.ConvertSummary.Visibility);
                Assert.Equal(AacDecisionPresentationService.ReuseSummary(plan) ?? "", dialog.ReuseSummary.Text);
                Assert.Equal(AacDecisionPresentationService.ConvertSummary(plan) ?? "", dialog.ConvertSummary.Text);
                Assert.Empty(dialog.DetailItems);
                if (reused == 8)
                {
                    Assert.Contains("42", dialog.ReuseSummary.Text); Assert.Contains("11", dialog.ConvertSummary.Text);
                    Assert.Equal(42 * TimeSpan.TicksPerMinute, plan.ReusedDurationTicks);
                    Assert.Equal(11 * TimeSpan.TicksPerMinute, plan.ConvertedDurationTicks);
                }
                if (reused == 1) Assert.Contains("1 Track bleibt", dialog.ReuseSummary.Text);
                if (converted == 1) Assert.Contains("1 Track wird", dialog.ConvertSummary.Text);
            }
            finally { dialog.Close(); }
        }));
    }

    [Fact]
    public void Aac_compact_card_fits_all_21_locales_and_a_small_work_area()
    {
        RunOnStaThread(() =>
        {
            var cultures = new[] { "bn-BD", "cs", "da", "de", "en", "es", "fi", "fr", "gsw-CH", "it", "ja", "nb", "nl", "pl", "pt-BR", "ru", "sv", "tr", "vi", "zh-CN", "zh-HK" };
            foreach (var locale in cultures)
                WithSettingsCulture(locale, () => CheckLayout(locale, small: false));
            WithSettingsCulture("fr", () => CheckLayout("fr", small: true));
        });

        static void CheckLayout(string locale, bool small)
        {
            var plan = CompactSample(); var dialog = new AacDecisionDialog(new(plan, plan, plan));
            if (small) { dialog.Width = 380; dialog.MaxHeight = 360; }
            try
            {
                dialog.Show(); DrainDispatcher(); dialog.UpdateLayout();
                Assert.Equal("AAC Pass-Through", dialog.HeadingText.Text);
                Assert.Equal("AAC Pass-Through", dialog.Title);
                Assert.InRange(dialog.ActualWidth, small ? 320 : 520, small ? 400 : 620);
                Assert.True(dialog.ActualHeight <= dialog.MaxHeight + 1, locale);
                if (!small && locale is "de" or "en") Assert.InRange(dialog.ActualHeight, 320, 440);
                Assert.False(dialog.DetailsBorder.IsVisible);
                Assert.Contains("96", dialog.RecommendedProfile.Text);
                Assert.Contains("48", dialog.RecommendedProfile.Text);
                var buttons = dialog.DecisionButtons.Children.OfType<Button>().ToArray();
                var first = buttons[0].TranslatePoint(new Point(), dialog);
                var second = buttons[1].TranslatePoint(new Point(), dialog);
                var back = buttons[2].TranslatePoint(new Point(), dialog);
                Assert.Equal(first.Y, second.Y, 1); Assert.True(back.Y > first.Y + buttons[0].ActualHeight);
                foreach (var button in buttons)
                {
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                    var text = Assert.IsType<TextBlock>(button.Content);
                    text.Measure(new Size(text.ActualWidth, double.PositiveInfinity));
                    Assert.True(text.DesiredSize.Height <= text.ActualHeight + 1, locale + ": button clipping");
                    Assert.DoesNotContain("96", text.Text); Assert.DoesNotContain("48000", text.Text);
                }
                var output = Environment.GetEnvironmentVariable("BOOKSTITCH_AAC_DIALOG_PREVIEW_DIR");
                if (!small && output is not null && locale is "de" or "en" or "bn-BD" or "fr")
                {
                    Directory.CreateDirectory(output);
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render((Visual)dialog.Content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, $"aac-compact-{locale}.png")); encoder.Save(file);
                }
            }
            finally { dialog.Close(); }
        }
    }

    [Fact]
    public void Aac_compact_escape_returns_to_project_without_confirmation()
    {
        RunOnStaThread(() =>
        {
            var plan = CompactSample(); var dialog = new AacDecisionDialog(new(plan, plan, plan));
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
            {
                var source = PresentationSource.FromVisual(dialog)!;
                dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape)
                    { RoutedEvent = Keyboard.KeyDownEvent });
            });
            try { dialog.ShowDialog(); Assert.Equal(AppDialogResult.Cancel, dialog.Result); }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void Aac_card_sums_short_tracks_before_formatting_minutes()
    {
        RunOnStaThread(() => WithSettingsCulture("de", () =>
        {
            var plan = CompactSample(2, 0);
            plan.Tracks = plan.Tracks.Select(t => t with { DurationTicks = 30 * TimeSpan.TicksPerSecond + 1 }).ToList();
            Assert.Contains("1 Min.", AacDecisionPresentationService.ReuseSummary(plan));
            plan.Tracks.RemoveAt(1);
            Assert.Contains("unter 1 Min.", AacDecisionPresentationService.ReuseSummary(plan));
        }));
    }

    [Theory]
    [InlineData(0, AppDialogResult.Yes)]
    [InlineData(1, AppDialogResult.No)]
    [InlineData(2, AppDialogResult.Cancel)]
    public void Aac_decision_has_three_explicit_actions_and_defaults_to_optimized(int action, AppDialogResult result)
    {
        RunOnStaThread(() =>
        {
            var plan = new AacDecisionPlan { EffectivePreset = "AAC Stereo 128 kbps", SampleRateHz = 44100 };
            var dialog = new AacDecisionDialog(new(plan, plan, plan));
            Exception? failure = null;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var panel = dialog.DecisionButtons;
                    var buttons = panel.Children.OfType<Button>().ToArray();
                    Assert.Equal(3, buttons.Length);
                    Assert.True(buttons[0].IsDefault); Assert.True(buttons[2].IsCancel);
                    Assert.All(buttons, button => Assert.True(button.IsEnabled && button.IsVisible));
                    buttons[action].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception error) { failure = error; dialog.Close(); }
            });
            try
            {
                dialog.ShowDialog();
                Assert.Null(failure);
                Assert.Equal(result, dialog.Result);
            }
            finally { dialog.Close(); }
        });
    }
}
