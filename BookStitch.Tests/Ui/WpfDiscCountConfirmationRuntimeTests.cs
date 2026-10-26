using BookStitch.Dialog;
using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData("OK")]
    [InlineData("Back")]
    [InlineData("Close")]
    [InlineData("Escape")]
    public void Warning_toggle_is_committed_only_by_OK_and_suppresses_all_boundary_counts(string action)
    {
        RunOnStaThread(() =>
        {
            var accept = action == "OK";
            using var inputSource = CreateKeyboardInputSource("Warning toggle exit paths");
            var settings = new BookStitch.Models.AppSettings();
            var saved = 0;
            var setup = CreateProjectSetupDialog(ProjectSetupSourceKind.AudioDisc, confirmDiscCountOnStart: true,
                shouldWarnDiscCount: () => !WarningSuppressionService.IsSuppressed(settings, WarningSuppressionService.DiscCount),
                setAlwaysWarnDiscCount: always =>
                {
                    WarningSuppressionService.SetSuppressed(settings, WarningSuppressionService.DiscCount, !always);
                    saved++;
                });
            Exception? failure = null;
            setup.Loaded += (_, _) => setup.Dispatcher.BeginInvoke(new Action(() =>
            {
                setup.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var warning = setup.OwnedWindows.OfType<DiscCountConfirmationDialog>().Single();
                    try
                    {
                        Assert.IsAssignableFrom<AppMessageDialog>(warning);
                        Assert.True(warning.AlwaysWarn);
                        warning.AlwaysWarnToggle.IsChecked = false;
                        warning.AlwaysWarnToggle.ApplyTemplate();
                        var label = Assert.IsType<TextBlock>(warning.AlwaysWarnToggle.Template.FindName("ToggleLabel", warning.AlwaysWarnToggle));
                        Assert.Contains(TextDecorations.Strikethrough[0], label.TextDecorations);
                        var track = Assert.IsType<System.Windows.Controls.Border>(warning.AlwaysWarnToggle.Template.FindName("SwitchTrack", warning.AlwaysWarnToggle));
                        Assert.Equal(
                            Assert.IsType<System.Windows.Media.SolidColorBrush>(warning.FindResource("ErrorSurfaceBrush")).Color,
                            Assert.IsType<System.Windows.Media.SolidColorBrush>(track.Background).Color);
                        Assert.Equal(0, saved);
                        if (action == "Close") warning.Close();
                        else if (action == "Escape") RaiseKey(warning, inputSource, Key.Escape, Keyboard.KeyDownEvent);
                        else (accept ? warning.OkButton : warning.BackButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception ex) { failure = ex; warning.Close(); }
                }));
                setup.StartProjectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!accept) setup.Close();
            }));
            Assert.Equal(accept, setup.ShowDialog() == true);
            Assert.Null(failure);
            Assert.Equal(accept ? 1 : 0, saved);
            Assert.Equal(accept, WarningSuppressionService.IsSuppressed(settings, WarningSuppressionService.DiscCount));
            if (!accept) return;
            foreach (var kind in new[] { ProjectSetupSourceKind.AudioDisc, ProjectSetupSourceKind.Mp3Disc })
            foreach (var count in new[] { 1, 13, 99 })
            {
                var next = CreateProjectSetupDialog(kind, defaultDiscCount: count, confirmDiscCountOnStart: true,
                    shouldWarnDiscCount: () => !WarningSuppressionService.IsSuppressed(settings, WarningSuppressionService.DiscCount));
                next.Loaded += (_, _) => next.Dispatcher.BeginInvoke(new Action(() =>
                    next.StartProjectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
                Assert.True(next.ShowDialog());
                Assert.Equal(count, next.Result.TotalDiscs);
            }
        });
    }

    [Fact]
    public void Returning_then_starting_again_rechecks_the_current_count()
    {
        RunOnStaThread(() =>
        {
            var setup = CreateProjectSetupDialog(ProjectSetupSourceKind.AudioDisc, confirmDiscCountOnStart: true);
            Exception? failure = null;
            var confirmations = 0;
            setup.Loaded += (_, _) => setup.Dispatcher.BeginInvoke(new Action(() =>
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var currentAttempt = attempt;
                    setup.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var warning = setup.OwnedWindows.OfType<DiscCountConfirmationDialog>().Single();
                        try
                        {
                            confirmations++;
                            if (currentAttempt == 0)
                            {
                                warning.DiscCountInput.Input.Text = "13";
                                warning.DiscCountInput.Input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                                warning.BackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                            else
                            {
                                Assert.Equal(LocalizationText.Format("Disc.Count.ConfirmMany", 13), warning.Question.Text);
                                warning.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                        }
                        catch (Exception ex) { failure = ex; warning.Close(); setup.Close(); }
                    }));
                    setup.StartProjectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (failure is not null) break;
                }
            }));
            Assert.True(setup.ShowDialog());
            Assert.Null(failure);
            Assert.Equal(2, confirmations);
            Assert.Equal(13, setup.Result.TotalDiscs);
        });
    }

    [Theory]
    [InlineData(ProjectSetupSourceKind.AudioDisc)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc)]
    public void Continuation_setup_does_not_confirm_single_cd(ProjectSetupSourceKind kind)
    {
        RunOnStaThread(() =>
        {
            var setup = CreateProjectSetupDialog(kind);
            setup.Loaded += (_, _) => setup.Dispatcher.BeginInvoke(new Action(() =>
                setup.StartProjectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            Assert.True(setup.ShowDialog());
            Assert.Equal(1, setup.Result.TotalDiscs);
        });
    }

    [Fact]
    public void Confirmation_count_control_uses_same_stepper_keyboard_and_wheel_state()
    {
        RunOnStaThread(() =>
        {
            var setup = CreateProjectSetupDialog(ProjectSetupSourceKind.AudioDisc, defaultDiscCount: 13);
            var warning = new DiscCountConfirmationDialog(setup.DiscCountState);
            using var source = CreateKeyboardInputSource("Shared counter controls");
            try
            {
                setup.Show();
                warning.Show();
                DrainDispatcher();
                warning.DiscCountInput.IncreaseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("14", setup.DiscCountState.Text);
                warning.DiscCountInput.DecreaseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RaiseKey(warning.DiscCountInput.Input, source, Key.Down, Keyboard.PreviewKeyDownEvent);
                Assert.Equal("12", setup.DiscCountState.Text);
                warning.DiscCountInput.Input.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
                    { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                Assert.Equal("13", setup.DiscCountState.Text);
                DrainDispatcher();
                Assert.Equal("13", setup.DiscCountTextBox.Text);
                Assert.Equal("13", warning.DiscCountInput.Input.Text);
            }
            finally { warning.Close(); setup.Close(); }
        });
    }

    [Theory]
    [InlineData(ProjectSetupSourceKind.AudioDisc, 1, true)]
    [InlineData(ProjectSetupSourceKind.AudioDisc, 2, false)]
    [InlineData(ProjectSetupSourceKind.AudioDisc, 12, false)]
    [InlineData(ProjectSetupSourceKind.AudioDisc, 13, true)]
    [InlineData(ProjectSetupSourceKind.AudioDisc, 20, true)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc, 1, true)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc, 2, false)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc, 12, false)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc, 13, true)]
    [InlineData(ProjectSetupSourceKind.Mp3Disc, 20, true)]
    public void New_cd_start_confirms_only_boundary_counts_and_uses_final_shared_value(ProjectSetupSourceKind kind, int count, bool expected)
    {
        RunOnStaThread(() =>
        {
            var setup = CreateProjectSetupDialog(kind, defaultDiscCount: count, confirmDiscCountOnStart: true);
            using var source = CreateKeyboardInputSource("CD confirmation enter");
            Exception? failure = null;
            var confirmations = 0;
            setup.Loaded += (_, _) => setup.Dispatcher.BeginInvoke(new Action(() =>
            {
                setup.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var warning = setup.OwnedWindows.OfType<DiscCountConfirmationDialog>().SingleOrDefault();
                    try
                    {
                        Assert.Equal(expected, warning is not null);
                        if (warning is null) return;
                        confirmations++;
                        Assert.Same(setup.DiscCountState, warning.DiscCountInput.State);
                        Assert.Equal(count.ToString(), warning.DiscCountInput.Input.Text);
                        Assert.Equal(count == 1 ? LocalizationText.Get("Disc.Count.ConfirmOne") : LocalizationText.Format("Disc.Count.ConfirmMany", count), warning.Question.Text);
                        warning.DiscCountInput.Input.Text = count == 1 ? "20" : "1";
                        warning.DiscCountInput.Input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                        Assert.Equal(warning.DiscCountInput.Input.Text, setup.DiscCountState.Text);
                        RaiseKey(warning.DiscCountInput.Input, source, Key.Enter, Keyboard.PreviewKeyDownEvent);
                    }
                    catch (Exception ex) { failure = ex; warning?.Close(); setup.Close(); }
                }), DispatcherPriority.Normal);
                setup.StartProjectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            var accepted = setup.ShowDialog();
            DrainDispatcher();
            Assert.Null(failure);
            Assert.True(accepted);
            Assert.Equal(expected ? 1 : 0, confirmations);
            Assert.Equal(expected ? count == 1 ? 20 : 1 : count, setup.Result.TotalDiscs);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Confirmation_invalid_input_blocks_enter_and_back_or_close_preserves_state(bool windowClose)
    {
        RunOnStaThread(() =>
        {
            var setup = CreateProjectSetupDialog(ProjectSetupSourceKind.Mp3Disc, confirmDiscCountOnStart: true);
            using var source = CreateKeyboardInputSource("Invalid CD confirmation");
            Exception? failure = null;
            setup.Loaded += (_, _) => setup.Dispatcher.BeginInvoke(new Action(() =>
            {
                setup.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var warning = setup.OwnedWindows.OfType<DiscCountConfirmationDialog>().Single();
                    try
                    {
                        warning.DiscCountInput.Input.Text = "";
                        warning.DiscCountInput.Input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                        RaiseKey(warning.DiscCountInput.Input, source, Key.Enter, Keyboard.PreviewKeyDownEvent);
                        Assert.True(warning.IsVisible);
                        Assert.Equal(Visibility.Visible, warning.ErrorText.Visibility);
                        warning.DiscCountInput.Input.Text = "7";
                        warning.DiscCountInput.Input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                        if (windowClose) warning.Close();
                        else warning.BackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception ex) { failure = ex; warning.Close(); }
                }));
                setup.StartProjectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                try
                {
                    Assert.True(setup.IsVisible);
                    Assert.Equal("7", setup.DiscCountState.Text);
                    Assert.Equal("7", setup.DiscCountTextBox.Text);
                }
                catch (Exception ex) { failure ??= ex; }
                setup.Close();
            }));
            Assert.NotEqual(true, setup.ShowDialog());
            Assert.Null(failure);
        });
    }
}
