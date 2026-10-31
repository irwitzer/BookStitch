using BookStitch.Dialog;
using BookStitch.Services;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    public static IEnumerable<object[]> FailedDiscDialogLanguages => UiLanguageService.Choices
        .Where(x => x.Id != UiLanguageService.System).Select(x => new object[] { x.Id });

    [Theory]
    [MemberData(nameof(FailedDiscDialogLanguages))]
    public void Failed_disc_close_dialog_is_localized_and_fits_at_minimum_size(string language)
    {
        RunOnStaThread(() =>
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            EnsureApplicationResources();
            var dialog = AppDialogService.CreateFailedDiscCloseDialog();
            try
            {
                dialog.Width = dialog.MinWidth;
                dialog.Height = dialog.MinHeight;
                dialog.Show();
                dialog.UpdateLayout();
                var message = (TextBlock)dialog.FindName("MessageText");
                Assert.Equal(LocalizationText.Get("Project.Close.FailedDiscMessage"), message.Text);
                Assert.DoesNotContain("Project.Close.", message.Text);
                var formatted = new FormattedText(message.Text, CultureInfo.CurrentUICulture,
                    message.FlowDirection, new Typeface(message.FontFamily, message.FontStyle, message.FontWeight, message.FontStretch),
                    message.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(message).PixelsPerDip)
                    { MaxTextWidth = message.ActualWidth };
                Assert.True(formatted.Height <= message.ActualHeight + 1, $"{language}: message clipped");
                var panel = (Panel)dialog.FindName("ButtonsPanel");
                var buttons = panel.Children.Cast<Button>().ToArray();
                Assert.Equal(2, buttons.Length);
                Assert.Equal(AppDialogResult.No, buttons[0].Tag);
                Assert.False(buttons[0].IsDefault);
                Assert.True(buttons[1].IsDefault);
                Assert.Equal(AppDialogResult.Yes, buttons[1].Tag);
                Assert.True(panel.ActualWidth <= dialog.ActualWidth - 30, $"{language}: buttons clipped");
                foreach (var button in buttons)
                {
                    var size = new FormattedText((string)button.Content, CultureInfo.CurrentUICulture,
                        button.FlowDirection, new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch),
                        button.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(button).PixelsPerDip);
                    Assert.True(size.Width <= button.ActualWidth - button.Padding.Left - button.Padding.Right + 1,
                        $"{language}: button text clipped");
                }
                var directory = Environment.GetEnvironmentVariable("BOOKSTITCH_FAILED_DISC_DIALOG_PREVIEW_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(dialog);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(directory, language + ".png"));
                    encoder.Save(output);
                }
            }
            finally { dialog.Close(); }
        });
    }

    [Theory]
    [InlineData(AppDialogResult.Yes)]
    [InlineData(AppDialogResult.No)]
    [InlineData(AppDialogResult.Cancel)]
    public void Failed_disc_close_dialog_returns_save_delete_or_cancel(AppDialogResult expected)
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var dialog = AppDialogService.CreateFailedDiscCloseDialog();
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                if (expected == AppDialogResult.Cancel)
                {
                    // Closing the dialog without choosing an action must not imply deletion.
                    dialog.Close();
                    return;
                }
                var panel = (Panel)dialog.FindName("ButtonsPanel");
                panel.Children.Cast<Button>().Single(b => Equals(b.Tag, expected))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            try
            {
                dialog.ShowDialog();
                if (expected == AppDialogResult.Cancel)
                    Assert.True(dialog.Result is AppDialogResult.None or AppDialogResult.Cancel);
                else Assert.Equal(expected, dialog.Result);
            }
            finally { dialog.Close(); }
        });
    }
}
