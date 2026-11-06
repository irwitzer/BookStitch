using BookStitch.Services;
using System.Windows;
using System.Windows.Controls;
using Xunit;
namespace BookStitch.Tests.Ui;
public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Audio_column_modes_preserve_manual_visibility_until_default_layout_is_restored()
    {
        RunOnStaThread(() =>
        {
            var keys = new[] { "BitrateKbps", "DisplayPreparedBitrateKbps", "SampleRateHz", "PreparedSampleRateHz", "Channels", "DisplayPreparedChannelLayout", "AudioProfile", "DisplayPreparedProfile", "Codec", "Extension" };
            var grid = new DataGrid { AutoGenerateColumns = false };
            foreach (var key in keys) grid.Columns.Add(new DataGridTextColumn { SortMemberPath = key });
            void Apply(AudioColumnMode mode, bool custom = false) => AudioColumnVisibilityService.Apply(grid.Columns, c => c.SortMemberPath, mode, custom);
            Apply(AudioColumnMode.Input);
            Assert.All(Enumerable.Range(0, 8), i => Assert.Equal(i % 2 == 0 ? Visibility.Visible : Visibility.Collapsed, grid.Columns[i].Visibility));
            Apply(AudioColumnMode.Output);
            Assert.All(Enumerable.Range(0, 8), i => Assert.Equal(i % 2 == 1 ? Visibility.Visible : Visibility.Collapsed, grid.Columns[i].Visibility));
            Apply(AudioColumnMode.Both); Assert.All(grid.Columns, c => Assert.Equal(Visibility.Visible, c.Visibility));
            grid.Columns[0].Visibility = Visibility.Collapsed; grid.Columns[3].Visibility = Visibility.Collapsed;
            var manual = grid.Columns.Select(c => c.Visibility).ToArray();
            foreach (var mode in Enum.GetValues<AudioColumnMode>()) { Apply(mode, custom: true); Assert.Equal(manual, grid.Columns.Select(c => c.Visibility)); }
            Apply(AudioColumnMode.Output); // Restore defaults clears the manual flag and uses the current workflow phase.
            Assert.Equal(Visibility.Visible, grid.Columns[3].Visibility); Assert.Equal(Visibility.Collapsed, grid.Columns[0].Visibility);
            Assert.Equal(Visibility.Visible, grid.Columns[8].Visibility); Assert.Equal(Visibility.Visible, grid.Columns[9].Visibility);
        });
    }
}
