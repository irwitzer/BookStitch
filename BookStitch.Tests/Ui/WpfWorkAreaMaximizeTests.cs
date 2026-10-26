using BookStitch.Controls;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Xml.Linq;
using Xunit;
using static BookStitch.Services.WindowMaximizeGeometry;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Work_area_opt_in_is_only_on_the_shared_resizable_style()
    {
        var doc = RepositorySource.LoadXaml(["BookStitch", "Themes", "WindowChromeStyles.xaml"]);
        var setters = doc.Descendants().Where(e => e.Name.LocalName == "Setter" &&
            (string?)e.Attribute("Property") == "dialog:WorkAreaMaximizeBehavior.IsEnabled").ToArray();
        var setter = Assert.Single(setters);
        Assert.Equal("True", setter.Attribute("Value")!.Value);
        Assert.Equal("LargeResizableWindowStyle", setter.Parent!.Attributes().Single(a => a.Name.LocalName == "Key").Value);
        var main = doc.Descendants().Single(e => e.Name.LocalName == "Style" &&
            e.Attributes().Any(a => a.Name.LocalName == "Key" && a.Value == "MainWindowStyle"));
        Assert.Equal("{StaticResource LargeResizableWindowStyle}", main.Attribute("BasedOn")!.Value);
    }

    [Theory]
    [InlineData("LargeResizableWindowStyle")]
    [InlineData("MainWindowStyle")]
    public void Work_area_hook_preserves_Wpf_tracking_constraints_and_detaches_on_unload_disable_and_close(string style)
    {
        RunOnStaThread(() =>
        {
            var window = new Window { Style = (Style)Application.Current.FindResource(style),
                Width = 800, Height = 600, MinWidth = 321, MinHeight = 234, ShowInTaskbar = false };
            try
            {
                window.Show(); DrainDispatcher();
                var hwnd = new WindowInteropHelper(window).Handle;
                var bounds = ReadMonitor(hwnd);
                void AssertEnabled()
                {
                    var result = QueryMinMax(hwnd);
                    Assert.Equal(bounds.Work.Right - bounds.Work.Left, result.MaxSize.X);
                    Assert.Equal(bounds.Work.Bottom - bounds.Work.Top, result.MaxSize.Y);
                    Assert.Equal(bounds.Work.Left - bounds.Monitor.Left, result.MaxPosition.X);
                    Assert.Equal(bounds.Work.Top - bounds.Monitor.Top, result.MaxPosition.Y);
                    var scale = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformToDevice;
                    Assert.True(result.MinTrackSize.X >= Math.Floor(321 * scale.M11));
                    Assert.True(result.MinTrackSize.Y >= Math.Floor(234 * scale.M22));
                }
                AssertEnabled();
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                Assert.Equal(-123, QueryMinMax(hwnd).MaxPosition.X);
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                AssertEnabled();
                WorkAreaMaximizeBehavior.SetIsEnabled(window, false);
                Assert.Equal(-123, QueryMinMax(hwnd).MaxPosition.X);
                WorkAreaMaximizeBehavior.SetIsEnabled(window, true);
                WorkAreaMaximizeBehavior.SetIsEnabled(window, true);
                AssertEnabled();
                window.ResizeMode = ResizeMode.NoResize;
                Assert.Equal(-123, QueryMinMax(hwnd).MaxPosition.X);
                window.ResizeMode = ResizeMode.CanMinimize;
                Assert.Equal(-123, QueryMinMax(hwnd).MaxPosition.X);
            }
            finally { window.Close(); }
        });
    }

    // Isolated native-state smoke: avoid stealing focus from the regular modal suite.
    [Theory]
    [InlineData("main")]
    [InlineData("initial-main")]
    [InlineData("initial-metadata")]
    [InlineData("metadata")]
    [InlineData("mp3")]
    [InlineData("audio")]
    [InlineData("resume")]
    [InlineData("settings")]
    public void Work_area_native_maximize_smoke(string kind)
    {
        if (Environment.GetEnvironmentVariable("BOOKSTITCH_RUN_WINDOW_STATE_SMOKE_TEST") != "1") return;
        RunOnStaThread(() =>
        {
            var owner = new Window { Width = 600, Height = 400, ShowInTaskbar = false };
            owner.Show();
            void Check(Window window)
            {
                window.Owner = owner;
                try
                {
                    if (kind.StartsWith("initial-", StringComparison.Ordinal)) window.WindowState = WindowState.Maximized;
                    window.Show(); DrainDispatcher();
                    var normal = window.RestoreBounds;
                    var hwnd = new WindowInteropHelper(window).Handle;
                    window.WindowState = WindowState.Maximized; DrainDispatcher();
                    var monitor = ReadMonitor(hwnd);
                    Assert.True(ReadWindowRect(hwnd, out var actual));
                    Assert.Equal(monitor.Work, actual);
                    Assert.Equal(WindowState.Maximized, window.WindowState);
                    Assert.Equal(owner, window.Owner);
                    Assert.False(window.Topmost);
                    var origin = window.PointToScreen(new Point());
                    var header = FindVisualDescendant<LargeResizableWindowHeader>(window, _ => true);
                    var headerOrigin = header?.PointToScreen(new Point());
                    Assert.NotNull(headerOrigin);
                    Assert.InRange(headerOrigin.Value.Y - monitor.Work.Top, 0, 2);
                    Assert.InRange(headerOrigin.Value.X - monitor.Work.Left, 0, 2);
                    if (window is MetadataDialog)
                    {
                        var footer = FindVisualDescendant<System.Windows.Controls.Button>(window, button =>
                            System.Windows.Automation.AutomationProperties.GetName(button) == LocalizationText.Get("Metadata.Automation.CloseDialog"));
                        Assert.NotNull(footer);
                        Assert.True(footer.IsEnabled);
                        var footerBottom = footer.PointToScreen(new Point(footer.ActualWidth, footer.ActualHeight));
                        Assert.True(footerBottom.X <= monitor.Work.Right && footerBottom.Y <= monitor.Work.Bottom);
                    }
                    var evidence = $"{kind}: hwnd={hwnd}, work={actual.Left},{actual.Top},{actual.Right},{actual.Bottom}, client={origin.X},{origin.Y}, header={headerOrigin}, border={window.BorderThickness}";
                    var log = Environment.GetEnvironmentVariable("BOOKSTITCH_WORKAREA_LOG");
                    if (!string.IsNullOrEmpty(log)) File.AppendAllText(log, evidence + Environment.NewLine);
                    if (int.TryParse(Environment.GetEnvironmentVariable("BOOKSTITCH_WORKAREA_INSPECTION_SECONDS"), out var seconds) && seconds > 0)
                    {
                        var frame = new System.Windows.Threading.DispatcherFrame();
                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Min(90, seconds)) };
                        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                        EventHandler closed = (_, _) => frame.Continue = false;
                        window.Closed += closed;
                        try { timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); }
                        finally { timer.Stop(); window.Closed -= closed; }
                        if (!window.IsVisible) return;
                    }
                    window.WindowState = WindowState.Normal; DrainDispatcher();
                    Assert.Equal(normal.Width, window.Width, 1);
                    Assert.Equal(normal.Height, window.Height, 1);
                    Assert.Equal(WindowState.Normal, window.WindowState);
                }
                finally
                {
                    if (window is MetadataDialog metadata) metadata.WarnBeforeDiscardingConceptDrafts = false;
                    window.Close();
                }
            }
            try
            {
                switch (kind.Replace("initial-", "", StringComparison.Ordinal))
                {
                    case "main":
                        var panel = new System.Windows.Controls.DockPanel();
                        var mainHeader = new LargeResizableWindowHeader();
                        System.Windows.Controls.DockPanel.SetDock(mainHeader, System.Windows.Controls.Dock.Top);
                        panel.Children.Add(mainHeader);
                        panel.Children.Add(new System.Windows.Controls.Border());
                        Check(new Window { Style = (Style)Application.Current.FindResource("MainWindowStyle"),
                            Width = 1250, Height = 860, Content = panel, ShowInTaskbar = false }); break;
                    case "metadata": Check(CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences())); break;
                    case "mp3": Check(CreateProjectSetupDialog(ProjectSetupSourceKind.Mp3Disc)); break;
                    case "audio": Check(CreateProjectSetupDialog(ProjectSetupSourceKind.AudioDisc)); break;
                    case "resume":
                        using (var root = new BookStitch.Tests.TestHelpers.TemporaryFolder())
                            Check(new ResumeProjectDialog(root.Path, new ProjectIndexService(), new ProjectResumePlanService(), 180, AppearanceMode.Dark));
                        break;
                    case "settings": RunWithSettingsWindow(new AppSettings(), Check); break;
                }
            }
            finally { owner.Close(); }
        }, timeoutSeconds: Environment.GetEnvironmentVariable("BOOKSTITCH_WORKAREA_INSPECTION_SECONDS") is null ? 20 : 120);
    }

    private static MinMaxInfo QueryMinMax(IntPtr hwnd)
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
        try
        {
            Marshal.StructureToPtr(new MinMaxInfo { MaxPosition = new() { X = -123, Y = -456 },
                MaxSize = new() { X = 3000, Y = 2000 }, MaxTrackSize = new() { X = 9000, Y = 9000 } }, memory, false);
            SendWindowMessage(hwnd, 0x0024, IntPtr.Zero, memory);
            return Marshal.PtrToStructure<MinMaxInfo>(memory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TestMonitorInfo { public int Size; public PixelRect Monitor, Work; public uint Flags; }
    private static TestMonitorInfo ReadMonitor(IntPtr hwnd)
    {
        var result = new TestMonitorInfo { Size = Marshal.SizeOf<TestMonitorInfo>() };
        Assert.True(ReadMonitorInfo(FindMonitor(hwnd, 2), ref result));
        return result;
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static extern IntPtr FindMonitor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadMonitorInfo(IntPtr monitor, ref TestMonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadWindowRect(IntPtr hwnd, out PixelRect rect);
}
