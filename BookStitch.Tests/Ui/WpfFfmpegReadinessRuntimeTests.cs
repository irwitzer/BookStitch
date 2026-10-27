using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Ffmpeg_install_instruction_is_compiled_and_fits_status_line_in_all_locales()
    {
        RunOnStaThread(() =>
        {
            using var master = JsonDocument.Parse(RepositorySource.Read(
                ["BookStitch", "Resources", "BookStitch.LanguageMaster.json"]));
            var entry = master.RootElement.GetProperty("entries").GetProperty("MainWindow.Ffmpeg.InstallRequired");
            var xaml = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
            var status = xaml.Descendants().Single(e => (string?)e.Attribute("Text") == "{Binding StatusText}");
            var minimumWidth = double.Parse((string)xaml.Root!.Attribute("MinWidth")!, CultureInfo.InvariantCulture);
            var coverWidth = double.Parse((string)status.Parent!.Element(status.Name.Namespace + "Grid.ColumnDefinitions")!
                .Elements().Last().Attribute("Width")!, CultureInfo.InvariantCulture);
            var availableWidth = minimumWidth - coverWidth - 40;
            foreach (var locale in master.RootElement.GetProperty("locales").EnumerateArray())
            {
                var language = locale.GetProperty("locale").GetString()!;
                var expected = language == "de" ? entry.GetProperty("source").GetProperty("value").GetString()
                    : entry.GetProperty("translations").GetProperty(language).GetProperty("value").GetString();
                var text = LocalizationText.Get("MainWindow.Ffmpeg.InstallRequired", CultureInfo.GetCultureInfo(language));
                Assert.Equal(expected, text);
                var block = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 14 };
                block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(block.DesiredSize.Width <= availableWidth, $"{language}: installation instruction exceeds the status line");
            }
        });
    }
}
