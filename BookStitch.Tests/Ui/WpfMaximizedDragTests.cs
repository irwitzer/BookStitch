using BookStitch.Controls;
using BookStitch.Dialog;
using BookStitch.Tests.TestHelpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BookStitch.Models;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

// Windows/WPF only: deterministic placement and temporary capture lifecycle.
// These tests do not simulate or certify Windows' native drag/Snap or mixed-DPI behavior.
public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Maximized_drag_keeps_real_metadata_header_inputs_interactive()
    {
        RunOnStaThread(() =>
        {
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences());
            try
            {
                dialog.Show();
                DrainDispatcher();
                var header = FindVisualDescendant<LargeResizableWindowHeader>(dialog, _ => true)!;
                Assert.NotNull(header);
                Assert.True(DialogWindowBehavior.GetRestoreOnMaximizedDrag(header));
                var search = (TextBox)dialog.FindName("ConceptSearchTextBox");
                Assert.True(DialogWindowBehavior.IsInteractiveDragSource(search, header));
                static IEnumerable<DependencyObject> Descendants(DependencyObject node)
                {
                    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
                    {
                        var child = VisualTreeHelper.GetChild(node, index);
                        yield return child;
                        foreach (var nested in Descendants(child)) yield return nested;
                    }
                }
                var buttons = Descendants(header).OfType<Button>().ToArray();
                Assert.True(buttons.Length >= 5); // Search/clear, settings and the three caption buttons.
                Assert.All(buttons, button => Assert.True(DialogWindowBehavior.IsInteractiveDragSource(button, header)));
                Assert.False(DialogWindowBehavior.HasPendingMaximizedDrag(header));
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void Maximized_drag_is_enabled_centrally_without_per_window_overrides()
    {
        var root = Path.GetDirectoryName(RepositorySource.Find(["BookStitch", "MainWindow.xaml"]))!;
        var optedIn = Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element => element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "DialogWindowBehavior.RestoreOnMaximizedDrag" && attribute.Value == "True"))
                .Select(element => (Path.GetRelativePath(root, path).Replace('\\', '/'), element.Name.LocalName)))
            .OrderBy(item => item.Item1).ToArray();
        Assert.Empty(optedIn);
        RunOnStaThread(() =>
        {
            Assert.True(DialogWindowBehavior.GetRestoreOnMaximizedDrag(new LargeResizableWindowHeader()));
            Assert.True(DialogWindowBehavior.GetRestoreOnMaximizedDrag(new Border()));
        });
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(1.01, 0, true)]
    [InlineData(0, -1.01, true)]
    [InlineData(-1.01, 0, true)]
    public void Maximized_drag_waits_for_horizontal_or_vertical_system_threshold(double x, double y, bool expected)
    {
        Assert.Equal(expected, DialogWindowBehavior.ExceedsDragThreshold(new Point(10, 20),
            new Point(10 + x * SystemParameters.MinimumHorizontalDragDistance,
                20 + y * SystemParameters.MinimumVerticalDragDistance)));
        Assert.False(DialogWindowBehavior.ExceedsDragThreshold(new Point(), new Point(double.NaN, 20)));
    }

    [Theory]
    [InlineData(1, -3000, -1000)]
    [InlineData(1.25, -3000, -1000)]
    [InlineData(1.5, -3000, -1000)]
    [InlineData(2, -3000, -1000)]
    [InlineData(1, 2000, 500)]
    [InlineData(1.25, 2000, 500)]
    [InlineData(1.5, 2000, 500)]
    [InlineData(2, 2000, 500)]
    public void Maximized_drag_placement_preserves_proportional_grip_in_native_coordinates(double scale, double originX, double originY)
    {
        // Negative origin, top/left taskbar inset and stale restore position on a removed monitor.
        var cursor = new Point(originX + 1600, originY + 500);
        var bounds = new Rect(9000, 7000, 800 * scale, 600 * scale);
        var anchor = new Point(bounds.Left + 0.75 * bounds.Width, bounds.Top + 20 * scale);
        var work = new Rect(originX, originY, 2800, 1800);
        Assert.True(DialogWindowBehavior.TryGetRestorePlacement(cursor, bounds, anchor, work, 46 * scale, out var position));
        Assert.Equal(cursor.X - 0.75 * bounds.Width, position.X, 5);
        Assert.Equal(cursor.Y - 20 * scale, position.Y, 5);
    }

    [Theory]
    [InlineData(1920, 1080, 800, 600)]
    [InlineData(1280, 720, 1170, 760)]
    [InlineData(800, 600, 1170, 760)]
    public void Maximized_drag_placement_keeps_title_reachable_without_shrinking(double width, double height, double windowWidth, double windowHeight)
    {
        var work = new Rect(-width + 40, -height + 30, width - 40, height - 70);
        var bounds = new Rect(10000, 10000, windowWidth, windowHeight);
        Assert.True(DialogWindowBehavior.TryGetRestorePlacement(new Point(10000, 10000), bounds,
            new Point(10000 + windowWidth / 2, 10020), work, 46, out var position));
        Assert.InRange(position.Y, work.Top, work.Bottom - 46);
        Assert.True(position.X < work.Right && position.X + windowWidth > work.Left);
        Assert.Equal(windowWidth, bounds.Width);
        Assert.Equal(windowHeight, bounds.Height);
        Assert.True(DialogWindowBehavior.TryGetRestorePlacement(new Point(-10000, -10000), bounds,
            new Point(10000 + windowWidth / 2, 10020), work, 46, out position));
        Assert.Equal(work.Top, position.Y);
        Assert.Equal(Math.Min(work.Left, work.Right - windowWidth), position.X);
    }

    [Fact]
    public void Maximized_drag_rejects_invalid_geometry_before_native_placement()
    {
        var valid = new Rect(0, 0, 800, 600);
        Assert.False(DialogWindowBehavior.TryGetRestorePlacement(new Point(double.NaN, 0), valid, new Point(), valid, 46, out _));
        Assert.False(DialogWindowBehavior.TryGetRestorePlacement(new Point(), Rect.Empty, new Point(), valid, 46, out _));
        Assert.False(DialogWindowBehavior.TryGetRestorePlacement(new Point(), valid, new Point(), new Rect(0, 0, 0, 600), 46, out _));
        Assert.False(DialogWindowBehavior.TryGetRestorePlacement(new Point(), valid, new Point(), valid, double.PositiveInfinity, out _));
    }

    [Theory]
    [InlineData("mouse-up")]
    [InlineData("capture-lost")]
    [InlineData("disabled")]
    [InlineData("region-disabled")]
    [InlineData("hidden")]
    [InlineData("escape")]
    [InlineData("second-click")]
    [InlineData("opt-out")]
    [InlineData("drag-region-off")]
    [InlineData("state-changed")]
    [InlineData("closed")]
    [InlineData("unloaded")]
    [InlineData("released-move")]
    public void Maximized_drag_pending_click_preserves_state_and_cleans_up_on_abort(string abort)
    {
        RunOnStaThread(() =>
        {
            var header = new LargeResizableWindowHeader();
            DialogWindowBehavior.SetRestoreOnMaximizedDrag(header, true);
            var window = new Window { Width = 720, Height = 480, ShowInTaskbar = false,
                Style = (Style)Application.Current.FindResource("LargeResizableWindowStyle"), Content = header };
            var closed = false;
            try
            {
                window.Show();
                window.WindowState = WindowState.Maximized;
                DrainDispatcher();
                // Test listener/state ownership without synthesizing a physical button press or
                // claiming the desktop's native capture/move loop has been exercised.
                var session = new DialogWindowBehavior.MaximizedDragSession(window, header,
                    new Point(100, 20), new Point(100, 20));
                session.Track();
                Assert.True(DialogWindowBehavior.HasPendingMaximizedDrag(header));
                Assert.Equal(WindowState.Maximized, window.WindowState); // A click alone never restores.
                switch (abort)
                {
                    case "mouse-up":
                        header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                            { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent }); break;
                    case "capture-lost": header.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                        { RoutedEvent = Mouse.LostMouseCaptureEvent }); break;
                    case "disabled": window.IsEnabled = false; break;
                    case "region-disabled": header.IsEnabled = false; break;
                    case "hidden": header.Visibility = Visibility.Collapsed; break;
                    case "escape": window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                        PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent }); break;
                    case "second-click":
                        header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                            { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent }); break;
                    case "opt-out": DialogWindowBehavior.SetRestoreOnMaximizedDrag(header, false); break;
                    case "drag-region-off": DialogWindowBehavior.SetIsDragRegion(header, false); break;
                    case "state-changed": window.WindowState = WindowState.Normal; break;
                    case "closed": window.Close(); closed = true; break;
                    case "unloaded": header.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
                    case "released-move": header.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                        { RoutedEvent = Mouse.PreviewMouseMoveEvent }); break;
                }
                Assert.False(DialogWindowBehavior.HasPendingMaximizedDrag(header));
                Assert.NotSame(header, Mouse.Captured);
                if (!closed)
                {
                    window.IsEnabled = true;
                    header.IsEnabled = true;
                    header.Visibility = Visibility.Visible;
                    DialogWindowBehavior.SetRestoreOnMaximizedDrag(header, true);
                    DialogWindowBehavior.SetIsDragRegion(header, true);
                    window.WindowState = WindowState.Maximized;
                    DrainDispatcher();
                    var nextSession = new DialogWindowBehavior.MaximizedDragSession(window, header,
                        new Point(100, 20), new Point(100, 20));
                    nextSession.Track();
                    Assert.True(DialogWindowBehavior.HasPendingMaximizedDrag(header));
                    nextSession.Cancel();
                    Assert.False(DialogWindowBehavior.HasPendingMaximizedDrag(header));
                }
            }
            finally { if (!closed) window.Close(); }
        });
    }

    [Fact]
    public void Maximized_drag_does_not_arm_normal_fixed_or_out_of_region_windows()
    {
        RunOnStaThread(() =>
        {
            var header = new LargeResizableWindowHeader();
            var window = new Window { Width = 720, Height = 480, ShowInTaskbar = false,
                Style = (Style)Application.Current.FindResource("LargeResizableWindowStyle"), Content = header };
            try
            {
                window.Show();
                DrainDispatcher();
                DialogWindowBehavior.SetRestoreOnMaximizedDrag(header, true);
                Assert.False(DialogWindowBehavior.TryBeginMaximizedDrag(window, header, new Point(100, 20), new Point(100, 20)));
                window.WindowState = WindowState.Maximized;
                DrainDispatcher();
                DialogWindowBehavior.SetRestoreOnMaximizedDrag(header, false);
                Assert.False(DialogWindowBehavior.TryBeginMaximizedDrag(window, header, new Point(100, 20), new Point(100, 20)));
                DialogWindowBehavior.SetRestoreOnMaximizedDrag(header, true);
                DialogWindowBehavior.SetDragRegionHeight(header, 10);
                Assert.False(DialogWindowBehavior.TryBeginMaximizedDrag(window, header, new Point(100, 20), new Point(100, 20)));
                DialogWindowBehavior.SetDragRegionHeight(header, double.PositiveInfinity);
                window.ResizeMode = ResizeMode.NoResize;
                Assert.False(DialogWindowBehavior.TryBeginMaximizedDrag(window, header, new Point(100, 20), new Point(100, 20)));
                Assert.False(DialogWindowBehavior.HasPendingMaximizedDrag(header));
            }
            finally { window.Close(); }
        });
    }
}
