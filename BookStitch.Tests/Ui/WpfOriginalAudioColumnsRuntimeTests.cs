using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Original_audio_columns_bind_source_values_and_expose_full_accessible_headers()
    {
        RunOnStaThread(() =>
        {
            var source = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var names = new[] { "TrackGridOriginalBitrateColumn", "TrackGridBitrateColumn", "TrackGridOriginalSampleRateColumn", "TrackGridSampleRateColumn", "TrackGridOriginalChannelLayoutColumn", "TrackGridChannelLayoutColumn", "TrackGridOriginalProfileColumn", "TrackGridProfileColumn" };
            var columns = names.Select(name => new XElement(source.Descendants(wpf + "DataGridTextColumn")
                .Single(element => (string?)element.Attribute(x + "Name") == name))).ToArray();
            var markup = new XElement(wpf + "DataGrid",
                new XAttribute(XNamespace.Xmlns + "x", x),
                new XAttribute(XNamespace.Xmlns + "services", "clr-namespace:BookStitch.Services;assembly=BookStitch"),
                new XAttribute("AutoGenerateColumns", "False"),
                new XElement(wpf + "DataGrid.Columns", columns));
            var grid = (DataGrid)XamlReader.Parse(markup.ToString());
            var track = new TrackInfo { BitrateKbps = 256, SampleRateHz = 44100, Channels = 2, Codec = "AAC", AudioProfile = "HE-AAC",
                PreparedConvertedPreset = "AAC Mono 64 kbps", HasReusableConvertedFile = true,
                PreparedAudio = new("aac", "LC", 48000, 1, 63123m) };
            grid.Items.Add(track);
            var window = new Window { Content = grid, Width = 1500, Height = 180, ShowInTaskbar = false };
            try
            {
                window.Show(); DrainDispatcher();
                string Cell(int index) => Assert.IsType<TextBlock>(grid.Columns[index].GetCellContent(track)).Text;
                Assert.Equal(new[] { "256", "63", "44.100", "48.000", "Stereo", "Mono", "HE-AAC v1", "AAC-LC" }, Enumerable.Range(0, 8).Select(Cell));
                Assert.All(grid.Columns, column => Assert.Equal(Visibility.Visible, column.Visibility));
                track.BitrateKbps = 192; track.SampleRateHz = 48000; track.Channels = 1; track.AudioProfile = "HE-AACv2";
                DrainDispatcher();
                Assert.Equal("192", Cell(0)); Assert.Equal("48.000", Cell(2)); Assert.Equal("Mono", Cell(4)); Assert.Equal("HE-AAC v2", Cell(6));
                Assert.Equal("63", Cell(1)); // Actual prepared bitrate, not the nominal 64-kbit preset.
                var keys = new[] { "BitrateIn", "BitrateOut", "SampleRateIn", "SampleRateOut", "ChannelsIn", "ChannelsOut", "ProfileIn", "ProfileOut" };
                foreach (var locale in new[] { "bn-BD", "cs", "da", "de", "en", "es", "fi", "fr", "gsw-CH", "it", "ja", "nb", "nl", "pl", "pt-BR", "ru", "sv", "tr", "vi", "zh-CN", "zh-HK" })
                    WithSettingsCulture(locale, () =>
                    {
                        var localizedGrid = (DataGrid)XamlReader.Parse(markup.ToString());
                        for (var index = 0; index < 8; index++)
                        {
                            var header = new System.Windows.Controls.Primitives.DataGridColumnHeader { Style = localizedGrid.Columns[index].HeaderStyle };
                            var expected = LocalizationText.Get("Tracks.Audio." + keys[index]);
                            Assert.EndsWith(index % 2 == 0 ? " IN" : " OUT", expected);
                            Assert.Equal(expected, System.Windows.Automation.AutomationProperties.GetName(header));
                        }
                    });
                track.HasReusableConvertedFile = false; DrainDispatcher();
                Assert.All(new[] { 1, 3, 5, 7 }, index => Assert.Empty(Cell(index)));
                Assert.Equal("192", Cell(0));
            }
            finally { window.Close(); }
        });
    }
}
