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
    [InlineData(true)]
    [InlineData(false)]
    public void Cover_warning_commits_shared_threshold_only_on_OK(bool accept)
    {
        RunOnStaThread(() =>
        {
            var owner = new Window();
            var threshold = 1000;
            var saves = 0;
            Exception? failure = null;
            try
            {
                owner.Show();
                owner.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var warning = owner.OwnedWindows.OfType<CoverSourceWarningDialog>().Single();
                    try
                    {
                        Assert.True(warning.AlwaysWarn);
                        warning.AlwaysWarnToggle.IsChecked = false;
                        warning.AlwaysWarnToggle.ApplyTemplate();
                        var label = Assert.IsType<TextBlock>(warning.AlwaysWarnToggle.Template.FindName("ToggleLabel", warning.AlwaysWarnToggle));
                        Assert.Contains(TextDecorations.Strikethrough[0], label.TextDecorations);
                        Assert.Equal(1000, threshold);
                        if (accept)
                            ((Button)warning.ButtonsPanel.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        else
                            warning.Close();
                    }
                    catch (Exception ex) { failure = ex; warning.Close(); }
                }));
                AppDialogService.CoverWarning(owner, "Cover", "Small source", threshold, value => { threshold = value; saves++; });
                Assert.Null(failure);
                Assert.Equal(accept ? 0 : 1000, threshold);
                Assert.Equal(accept ? 1 : 0, saves);
            }
            finally { owner.Close(); }
        });
    }
}
