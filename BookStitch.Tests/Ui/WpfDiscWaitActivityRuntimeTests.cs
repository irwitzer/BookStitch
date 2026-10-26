using BookStitch.Dialog;
using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData("de", "MP3-CD")]
    [InlineData("en", "MP3-CD")]
    [InlineData("fr", "MP3-CD")]
    [InlineData("es", "MP3-CD")]
    [InlineData("cs", "MP3-CD")]
    [InlineData("de", "Audio-CD")]
    [InlineData("en", "Audio-CD")]
    [InlineData("fr", "Audio-CD")]
    [InlineData("es", "Audio-CD")]
    [InlineData("cs", "Audio-CD")]
    public void Disc_wait_actions_fit_all_languages_in_completion_pause_continue_order(string culture, string media)
    {
        RunOnStaThread(() =>
        {
            var previous = System.Globalization.CultureInfo.CurrentUICulture;
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var dialog = new BoxedDiscWaitDialog(media + " 3/4", "Z:", "heading", "insert", "hint", true);
            try
            {
                dialog.Width = dialog.MinWidth;
                dialog.Show();
                DrainDispatcher();
                var complete = Assert.IsType<Button>(dialog.FindName("CompleteImportButton"));
                var pause = Assert.IsType<Button>(dialog.FindName("PauseButton"));
                var next = Assert.IsType<Button>(dialog.FindName("CheckButton"));
                Assert.Equal(LocalizationText.Get("Disc.Wait.Pause"), pause.Content);
                Assert.Equal(culture is "de" or "en" or "fr" ? "Pause" : culture == "es" ? "Pausa" : "Pauza", pause.Content);
                var buttons = new[] { complete, pause, next };
                Assert.Equal(buttons, ((Panel)complete.Parent).Children.OfType<Button>());
                foreach (var button in buttons)
                {
                    var label = new TextBlock { Text = (string)button.Content, FontFamily = button.FontFamily,
                        FontSize = button.FontSize, FontWeight = button.FontWeight };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.True(button.ActualWidth >= label.DesiredSize.Width + button.Padding.Left + button.Padding.Right);
                    var left = button.TranslatePoint(new Point(0, 0), dialog).X;
                    Assert.True(left >= 0 && left + button.ActualWidth <= dialog.ActualWidth);
                    Assert.True(button.IsTabStop);
                }
                var actions = new List<string>();
                dialog.CompleteImportRequested += (_, _) => actions.Add("complete");
                dialog.DeferRequested += (_, _) => actions.Add("pause");
                dialog.ManualCheckRequested += (_, _) => actions.Add("continue");
                foreach (var button in buttons) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(new[] { "complete", "pause", "continue" }, actions);
            }
            finally { dialog.Close(); System.Globalization.CultureInfo.CurrentUICulture = previous; }
        });
    }

    [Theory]
    [InlineData("MP3-CD", "complete")]
    [InlineData("Audio-CD", "complete")]
    [InlineData("MP3-CD", "pause")]
    [InlineData("Audio-CD", "pause")]
    [InlineData("MP3-CD", "cancel")]
    [InlineData("Audio-CD", "cancel")]
    public void Late_ready_poll_cannot_change_completed_paused_or_cancelled_wait(string media, string action)
    {
        RunOnStaThread(() =>
        {
            using var cancel = new CancellationTokenSource();
            var owner = new Window();
            var release = new TaskCompletionSource<DiscPollingResult>();
            var updates = new List<string>();
            var notifications = new List<DiscPollingDisplayState>();
            owner.Show();
            try
            {
                var result = new BoxedDiscWaitDialogService().WaitForDiscAsync(owner,
                    new(3, 4, media, "insert", "hint", "Z:", notifications.Add, CanCompleteImport: true),
                    (_, _) =>
                    {
                        var dialog = owner.OwnedWindows.OfType<BoxedDiscWaitDialog>().Single();
                        owner.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (action == "cancel") cancel.Cancel();
                            else ((Button)dialog.FindName(action == "complete" ? "CompleteImportButton" : "PauseButton"))
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }));
                        return release.Task;
                    }, updates.Add, updates.Add, cancel.Token);
                if (action == "cancel") Assert.ThrowsAny<OperationCanceledException>(() => result.GetAwaiter().GetResult());
                else Assert.Equal(action == "complete" ? DiscWaitDialogOutcome.ImportCompleted : DiscWaitDialogOutcome.Deferred,
                    result.GetAwaiter().GetResult());
                var count = updates.Count;
                release.SetResult(new(true, "late ready", "late ready", "late ready", DiscPollingDisplayState.Ready));
                DrainDispatcher();
                Assert.Equal(count, updates.Count);
                Assert.Empty(notifications);
                Assert.Empty(owner.OwnedWindows.Cast<Window>());
            }
            finally { owner.Close(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Disc_wait_activity_is_circle_only_and_stops_on_ready_or_cancellation(bool circle, bool cancelDuringWait)
    {
        RunOnStaThread(() =>
        {
            using var cancel = new CancellationTokenSource();
            var owner = new Window();
            var displayed = new List<string>();
            var checks = 0;
            var probeRunning = false;
            var animatedDuringProbe = false;
            DriveCircleStatusAnimation? animation = null;
            var raw = "";
            void Paint()
            {
                var text = animation is { IsActive: true } ? animation.Text : raw;
                displayed.Add(text);
                if (probeRunning && animation is { IsActive: true }) animatedDuringProbe = true;
                if (cancelDuringWait && text.EndsWith("...", StringComparison.Ordinal)) cancel.Cancel();
            }
            owner.Show();
            try
            {
                if (circle) animation = new(owner.Dispatcher, Paint, cancel.Token);
                var task = new BoxedDiscWaitDialogService().WaitForDiscAsync(owner,
                    new(3, 5, "fixture", "insert fixture", "hint", IsDriveCircle: circle),
                    async (_, token) =>
                    {
                        checks++;
                        probeRunning = true;
                        raw = "probe U hardware details";
                        Paint();
                        if (circle) await Task.Delay(2000, token);
                        probeRunning = false;
                        return new DiscPollingResult(true, "ready", "ready", "ready", DiscPollingDisplayState.Ready);
                    }, _ => { }, value => { raw = value; Paint(); }, cancel.Token);
                if (cancelDuringWait)
                    Assert.ThrowsAny<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                else
                    Assert.Equal(DiscWaitDialogOutcome.Ready, task.GetAwaiter().GetResult());
                Assert.Equal(1, checks);
                if (circle)
                {
                    Assert.True(animatedDuringProbe);
                    Assert.DoesNotContain(displayed, text => text.Contains("hardware", StringComparison.Ordinal));
                    Assert.Contains(displayed, text => text.EndsWith("..", StringComparison.Ordinal));
                }
                else Assert.DoesNotContain(displayed, text => text.StartsWith(LocalizationText.Get("Disc.DriveRound.Activity.Progress"), StringComparison.Ordinal));
                animation?.Dispose();
                var countAfterClose = displayed.Count;
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                try { Dispatcher.PushFrame(frame); }
                finally { timer.Stop(); }
                Assert.Equal(countAfterClose, displayed.Count);
                Assert.Empty(owner.OwnedWindows.Cast<Window>());
            }
            finally { animation?.Dispose(); owner.Close(); }
        });
    }

    [Fact]
    public void Duplicate_checkbox_has_clear_separation_without_changing_dialog_height_or_keyboard_access()
    {
        RunOnStaThread(() =>
        {
            var dialog = new BoxedDiscWaitDialog("fixture", "U", "heading", "instruction", "hint", true);
            try
            {
                dialog.Show();
                DrainDispatcher();
                // The HWND height is rounded to device pixels (350 DIPs = 437.5 px at 125%).
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(dialog).DpiScaleY;
                Assert.Equal(Math.Round(350 * dpi), Math.Round(dialog.Height * dpi));
                var checkbox = Assert.IsType<CheckBox>(dialog.FindName("AcceptDuplicateCheckBox"));
                var hint = Assert.IsType<TextBlock>(dialog.FindName("HintTextBlock"));
                var top = checkbox.TranslatePoint(new Point(0, 0), dialog).Y;
                var hintBottom = hint.TranslatePoint(new Point(0, hint.ActualHeight), dialog).Y;
                Assert.True(top - hintBottom >= 20);
                Assert.True(checkbox.Focusable);
                Assert.True(checkbox.IsTabStop);
                checkbox.IsChecked = true;
                Assert.True(dialog.AcceptNextDuplicate);
            }
            finally { dialog.Close(); }
        });
    }
}
