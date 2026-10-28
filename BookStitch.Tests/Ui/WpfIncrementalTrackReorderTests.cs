using BookStitch.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Incremental_track_reorder_keeps_selection_and_updates_numbers_without_view_reset()
    {
        RunOnStaThread(() =>
        {
            var a=new TrackInfo {Index=1,ChapterTitle="One"};
            var b=new TrackInfo {Index=2,ChapterTitle="Two"};
            var rows=new BulkObservableCollection<TrackInfo>();rows.ReplaceAll([a,b]);
            var grid=new DataGrid {ItemsSource=rows,AutoGenerateColumns=false,SelectionMode=DataGridSelectionMode.Extended};
            var column=new DataGridTextColumn {Binding=new Binding(nameof(TrackInfo.DisplayIndex))};
            grid.Columns.Add(column);
            var host=new Window {Content=grid,Width=600,Height=300,ShowInTaskbar=false};
            try
            {
                host.Show();DrainDispatcher();grid.SelectedItems.Add(b);
                rows.Reorder([b,a]); b.Index=1;a.Index=2;
                b.NotifyOrderPresentationChanged();a.NotifyOrderPresentationChanged();
                DrainDispatcher();
                Assert.Same(b,Assert.Single(grid.SelectedItems.Cast<TrackInfo>()));
                Assert.Same(b,grid.Items[0]);
                Assert.Equal("1",Assert.IsType<TextBlock>(column.GetCellContent(b)).Text);
                Assert.Equal("2",Assert.IsType<TextBlock>(column.GetCellContent(a)).Text);
            }
            finally {host.Close();}
        });
    }
}
