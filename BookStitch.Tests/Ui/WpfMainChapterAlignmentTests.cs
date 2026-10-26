using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Main_chapter_separators_and_numbering_align_without_label_clipping_in_all_locales()
    {
        using var master = JsonDocument.Parse(RepositorySource.Read(["BookStitch", "Resources", "BookStitch.LanguageMaster.json"]));
        var locales = master.RootElement.GetProperty("locales").EnumerateArray()
            .Select(locale => locale.GetProperty("locale").GetString()!).ToArray();
        RunOnStaThread(() =>
        {
            var doc = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
            var mode = doc.Descendants().Single(e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "MainChapterModeComboBox"));
            var rule = doc.Descendants().Single(e => e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "MainChapterCustomRulePanel"));
            var upper = (Grid)LoadResumePermissionElement(doc, mode.Parent!);
            var lower = (Grid)LoadResumePermissionElement(doc, rule);
            var panel = new StackPanel();
            Grid.SetIsSharedSizeScope(panel, true);
            panel.Children.Add(upper);
            panel.Children.Add(lower);
            var window = new Window { Content = panel, Width = 1170, Height = 250,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 12, ShowInTaskbar = false };
            try
            {
                window.Show();
                var label = (TextBlock)upper.FindName("MainChapterNumberingStyleLabelTextBlock");
                var numbering = (ComboBox)upper.FindName("MainChapterNumberingStyleComboBox");
                var album = (ComboBox)lower.FindName("MainChapterSlot2ComboBox");
                var first = (ComboBox)lower.FindName("MainChapterSeparator1ComboBox");
                var second = (ComboBox)lower.FindName("MainChapterSeparator2ComboBox");
                double? stableWidth = null;
                foreach (var locale in locales)
                {
                    var culture = CultureInfo.GetCultureInfo(locale);
                    label.Text = LocalizationText.Get("NumberingStyle", culture);
                    ((TextBlock)upper.FindName("MainChapterHeadingTextBlock")).Text = LocalizationText.Get("GenericChapterLabel", culture);
                    ((TextBlock)upper.FindName("MainChapterModeLabelTextBlock")).Text = LocalizationText.Get("Mode", culture);
                    ((TextBlock)lower.FindName("MainChapterCustomHeadingTextBlock")).Text = LocalizationText.Get("CustomRules", culture);
                    foreach (var width in new[] { 1170d, 1600d })
                    {
                        window.Width = width;
                        DrainDispatcher();
                        var natural = new TextBlock { Text = label.Text, FontFamily = label.FontFamily,
                            FontSize = label.FontSize, FontWeight = label.FontWeight };
                        natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Assert.True(natural.DesiredSize.Width <= label.ActualWidth + .1,
                            $"{locale}: numbering label needs {natural.DesiredSize.Width}, available {label.ActualWidth}");
                        Assert.Equal(numbering.TranslatePoint(new Point(), panel).X, album.TranslatePoint(new Point(), panel).X, 3);
                        Assert.Equal(first.ActualWidth, second.ActualWidth, 3);
                        Assert.True(first.ActualWidth > 82);
                        stableWidth ??= first.ActualWidth;
                        Assert.Equal(stableWidth.Value, first.ActualWidth, 3);
                        var last = (ComboBox)lower.FindName("MainChapterSlot3ComboBox");
                        Assert.True(last.TranslatePoint(new Point(last.ActualWidth, 0), panel).X <= panel.ActualWidth);
                    }
                }
            }
            finally { window.Close(); }
        });
    }
}
