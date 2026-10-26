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
            var names = new[] { "TrackGridOriginalBitrateColumn", "TrackGridOriginalSampleRateColumn", "TrackGridOriginalChannelLayoutColumn", "TrackGridBitrateColumn", "TrackGridChannelLayoutColumn" };
            var columns = names.Select(name => new XElement(source.Descendants(wpf + "DataGridTextColumn")
                .Single(element => (string?)element.Attribute(x + "Name") == name))).ToArray();
            var markup = new XElement(wpf + "DataGrid",
                new XAttribute(XNamespace.Xmlns + "x", x),
                new XAttribute(XNamespace.Xmlns + "services", "clr-namespace:BookStitch.Services;assembly=BookStitch"),
                new XAttribute("AutoGenerateColumns", "False"),
                new XElement(wpf + "DataGrid.Columns", columns));
            var grid = (DataGrid)XamlReader.Parse(markup.ToString());
            var track = new TrackInfo { BitrateKbps = 256, SampleRateHz = 44100, Channels = 2,
                PreparedConvertedPreset = "AAC Mono 64 kbps", HasReusableConvertedFile = true };
            grid.Items.Add(track);
            var window = new Window { Content = grid, Width = 650, Height = 180, ShowInTaskbar = false };
            try
            {
                window.Show();
                DrainDispatcher();
                Assert.Equal("256", Assert.IsType<TextBlock>(grid.Columns[0].GetCellContent(track)).Text);
                Assert.Equal(track.DisplayOriginalSampleRateHz,
                    Assert.IsType<TextBlock>(grid.Columns[1].GetCellContent(track)).Text);
                Assert.Equal("Stereo", Assert.IsType<TextBlock>(grid.Columns[2].GetCellContent(track)).Text);
                Assert.Equal(Visibility.Collapsed, grid.Columns[3].Visibility);
                Assert.Equal(Visibility.Collapsed, grid.Columns[4].Visibility);
                track.BitrateKbps = 192;
                track.SampleRateHz = 48000;
                track.Channels = 1;
                DrainDispatcher();
                Assert.Equal("192", Assert.IsType<TextBlock>(grid.Columns[0].GetCellContent(track)).Text);
                Assert.Equal("48.000", Assert.IsType<TextBlock>(grid.Columns[1].GetCellContent(track)).Text);
                Assert.Equal("Mono", Assert.IsType<TextBlock>(grid.Columns[2].GetCellContent(track)).Text);
                var headerKeys = new[] { "UiOriginalBitrate", "UiOriginalSampleRate", "UiOriginalMonoStereo" };
                for (var index = 0; index < 3; index++)
                {
                    var header = new System.Windows.Controls.Primitives.DataGridColumnHeader
                    { Style = grid.Columns[index].HeaderStyle };
                    Assert.Equal(LocalizationText.Get(headerKeys[index]),
                        System.Windows.Automation.AutomationProperties.GetName(header));
                }
                grid.Columns[0].Visibility = Visibility.Collapsed;
                grid.Columns[1].Visibility = Visibility.Collapsed;
                grid.Columns[2].Visibility = Visibility.Collapsed;
                grid.Columns[3].Visibility = Visibility.Visible;
                grid.Columns[4].Visibility = Visibility.Visible;
                DrainDispatcher();
                Assert.Equal("64", Assert.IsType<TextBlock>(grid.Columns[3].GetCellContent(track)).Text);
                Assert.Equal("Mono", Assert.IsType<TextBlock>(grid.Columns[4].GetCellContent(track)).Text);
                track.Channels = 2;
                grid.Columns[0].Visibility = Visibility.Visible;
                grid.Columns[2].Visibility = Visibility.Visible;
                DrainDispatcher();
                Assert.Equal("192", Assert.IsType<TextBlock>(grid.Columns[0].GetCellContent(track)).Text);
                Assert.Equal("Stereo", Assert.IsType<TextBlock>(grid.Columns[2].GetCellContent(track)).Text);
                Assert.Equal("Mono", Assert.IsType<TextBlock>(grid.Columns[4].GetCellContent(track)).Text);
            }
            finally { window.Close(); }
        });
    }
}
