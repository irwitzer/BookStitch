using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using BookStitch.Services;

namespace BookStitch.Dialog;

// Opt-in for the shared resizable, frameless WindowChrome style only.
public static class WorkAreaMaximizeBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkAreaMaximizeBehavior), new PropertyMetadata(false, OnEnabledChanged));
    private static readonly DependencyProperty SubscriptionProperty = DependencyProperty.RegisterAttached(
        "Subscription", typeof(Subscription), typeof(WorkAreaMaximizeBehavior));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Window window) return;
        (window.GetValue(SubscriptionProperty) as Subscription)?.Dispose();
        window.ClearValue(SubscriptionProperty);
        if (e.NewValue is true) window.SetValue(SubscriptionProperty, new Subscription(window));
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Window _window;
        private HwndSource? _source;

        public Subscription(Window window)
        {
            _window = window;
            window.SourceInitialized += OnSourceInitialized;
            window.Loaded += OnLoaded;
            window.Unloaded += OnUnloaded;
            window.Closed += OnClosed;
            Attach();
        }

        private void OnSourceInitialized(object? sender, EventArgs e) => Attach();
        private void OnLoaded(object sender, RoutedEventArgs e) => Attach();
        private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();
        private void OnClosed(object? sender, EventArgs e) => Dispose();

        private void Attach()
        {
            if (_source is not null) return;
            var handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero) return;
            var source = HwndSource.FromHwnd(handle);
            if (source is null || source.IsDisposed) return;
            source.AddHook(WindowProc);
            _source = source;
        }

        private void Detach()
        {
            var source = _source;
            _source = null;
            if (source is not null && !source.IsDisposed) source.RemoveHook(WindowProc);
        }

        private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != 0x0024 || lParam == IntPtr.Zero || _window.WindowStyle != WindowStyle.None ||
                _window.ResizeMode is not (ResizeMode.CanResize or ResizeMode.CanResizeWithGrip) ||
                WindowChrome.GetWindowChrome(_window) is null)
                return IntPtr.Zero;

            var monitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
            if (monitor == IntPtr.Zero) return IntPtr.Zero;
            var bounds = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref bounds)) return IntPtr.Zero;
            var info = Marshal.PtrToStructure<WindowMaximizeGeometry.MinMaxInfo>(lParam);
            if (WindowMaximizeGeometry.TryApply(true, bounds.Monitor, bounds.Work, ref info))
                Marshal.StructureToPtr(info, lParam, false);

            // Deliberately continue to WPF's WindowFilterMessage: it caches MaxSize and
            // applies MinWidth/MinHeight/MaxWidth/MaxHeight to tracking limits without
            // replacing MaxPosition/MaxSize. Consuming this message here would bypass
            // those normal resize constraints. WindowChrome does not handle this message.
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Detach();
            _window.SourceInitialized -= OnSourceInitialized;
            _window.Loaded -= OnLoaded;
            _window.Unloaded -= OnUnloaded;
            _window.Closed -= OnClosed;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public WindowMaximizeGeometry.PixelRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
