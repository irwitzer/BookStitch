using BookStitch;
using BookStitch.Models;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;
using Xunit;
namespace BookStitch.Tests.Ui;
public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Unsorted_headers_do_not_reserve_an_empty_sort_block_and_sorted_headers_keep_the_arrow()
    {
        RunOnStaThread(() =>
        {
            var grid = new DataGrid { AutoGenerateColumns = false, FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
            var column = new DataGridTextColumn { Header = "Dateiwarnung", Width = DataGridLength.Auto };
            grid.Columns.Add(column);
            var window = new Window { Content = grid, Width = 300, Height = 120, ShowInTaskbar = false };
            DataGridColumnHeader? FindHeader(DependencyObject node)
            {
                if (node is DataGridColumnHeader candidate && candidate.Column == column) return candidate;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    if (FindHeader(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
                return null;
            }
            try
            {
                window.Show(); DrainDispatcher(); grid.UpdateLayout();
                var header = FindHeader(grid); Assert.NotNull(header);
                var arrow = Assert.IsType<ShapePath>(header.Template.FindName("SortArrow", header));
                var innerGrid = Assert.IsType<Grid>(VisualTreeHelper.GetParent(arrow));
                Assert.Equal(0, innerGrid.ColumnDefinitions[1].ActualWidth);
                var textWidth = new FormattedText("Dateiwarnung", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(header.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 12, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
                Assert.InRange(header.ActualWidth, textWidth + 12, textWidth + 15);
                var unsortedWidth = header.ActualWidth;
                foreach (var direction in new[] { ListSortDirection.Ascending, ListSortDirection.Descending })
                {
                    column.SortDirection = direction; DrainDispatcher(); header.UpdateLayout();
                    Assert.Equal(Visibility.Visible, arrow.Visibility);
                    Assert.InRange(header.ActualWidth - unsortedWidth, 11, 13);
                }
                Assert.IsType<Thumb>(header.Template.FindName("PART_LeftHeaderGripper", header));
                Assert.IsType<Thumb>(header.Template.FindName("PART_RightHeaderGripper", header));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Filler_and_all_hidden_columns_offer_column_recovery_while_real_cells_keep_row_actions()
    {
        RunOnStaThread(() =>
        {
            var grid = new DataGrid { AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column };
            var column = new DataGridTextColumn { Header = "Codec", Binding = new System.Windows.Data.Binding("Codec"), Width = 80 };
            grid.Columns.Add(column); var track = new TrackInfo { Codec = "MP3" }; grid.Items.Add(track);
            var window = new Window { Content = grid, Width = 400, Height = 200, ShowInTaskbar = false };
            try
            {
                window.Show(); DrainDispatcher(); grid.UpdateLayout();
                var cellContent = column.GetCellContent(track); Assert.NotNull(cellContent);
                Assert.False(MainWindow.IsTrackGridColumnMenuOrigin(cellContent));
                Assert.True(MainWindow.IsTrackGridColumnMenuOrigin(grid.InputHitTest(new Point(300, 10)) as DependencyObject));
                Assert.True(MainWindow.IsTrackGridColumnMenuOrigin(grid.InputHitTest(new Point(300, 50)) as DependencyObject));
                column.Visibility = Visibility.Collapsed; DrainDispatcher(); grid.UpdateLayout();
                Assert.True(MainWindow.IsTrackGridColumnMenuOrigin(grid.InputHitTest(new Point(100, 10)) as DependencyObject));
                Assert.True(MainWindow.IsTrackGridColumnMenuOrigin(grid.InputHitTest(new Point(100, 50)) as DependencyObject));
            }
            finally { window.Close(); }
        });
    }
}
