using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace BookStitch.Dialog;

public static partial class DialogWindowBehavior
{
    public static readonly DependencyProperty RestoreOnMaximizedDragProperty = DependencyProperty.RegisterAttached(
        "RestoreOnMaximizedDrag", typeof(bool), typeof(DialogWindowBehavior),
        new PropertyMetadata(true, (element, _) =>
        {
            if (element is UIElement region) CancelPendingDrag(region);
        }));

    private static readonly DependencyProperty PendingDragProperty = DependencyProperty.RegisterAttached(
        "PendingDrag", typeof(MaximizedDragSession), typeof(DialogWindowBehavior));

    public static bool GetRestoreOnMaximizedDrag(DependencyObject element) =>
        (bool)element.GetValue(RestoreOnMaximizedDragProperty);

    public static void SetRestoreOnMaximizedDrag(DependencyObject element, bool value) =>
        element.SetValue(RestoreOnMaximizedDragProperty, value);

    private static void CancelPendingDrag(UIElement element) =>
        (element.GetValue(PendingDragProperty) as MaximizedDragSession)?.Cancel();

    internal static bool HasPendingMaximizedDrag(UIElement element) =>
        element.GetValue(PendingDragProperty) is MaximizedDragSession;

    internal static bool ExceedsDragThreshold(Point start, Point current) =>
        IsFinite(start.X, start.Y, current.X, current.Y) &&
        (Math.Abs(current.X - start.X) > SystemParameters.MinimumHorizontalDragDistance ||
         Math.Abs(current.Y - start.Y) > SystemParameters.MinimumVerticalDragDistance);

    internal static bool TryBeginMaximizedDrag(Window window, UIElement region, Point start, Point grip)
    {
        CancelPendingDrag(region);
        if (!GetIsDragRegion(region) || !GetRestoreOnMaximizedDrag(region) ||
            window.WindowState != WindowState.Maximized || !window.IsVisible || !window.IsEnabled ||
            !region.IsVisible || !region.IsEnabled ||
            window.ResizeMode is not (ResizeMode.CanResize or ResizeMode.CanResizeWithGrip) ||
            !IsFinite(start.X, start.Y, grip.X, grip.Y, window.ActualWidth) || window.ActualWidth <= 0 ||
            start.Y < 0 || start.Y > GetDragRegionHeight(region) ||
            (Mouse.Captured is not null && !ReferenceEquals(Mouse.Captured, region)))
            return false;

        try
        {
            var session = new MaximizedDragSession(window, region, start, grip);
            return session.Begin();
        }
        catch (Exception)
        {
            CancelPendingDrag(region);
            return false;
        }
    }

    // Every input/output here is in the same native screen coordinate space, never WPF DIPs.
    // Ignore the old restore position; only the current cursor monitor constrains the new position.
    internal static bool TryGetRestorePlacement(Point cursor, Rect bounds, Point anchor, Rect workArea,
        double captionHeight, out Point position)
    {
        position = default;
        if (bounds.IsEmpty || workArea.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 ||
            workArea.Width <= 0 || workArea.Height <= 0 || captionHeight <= 0 ||
            !IsFinite(cursor.X, cursor.Y, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                anchor.X, anchor.Y, workArea.X, workArea.Y, workArea.Width, workArea.Height, captionHeight))
            return false;

        var x = cursor.X - Math.Clamp(anchor.X - bounds.Left, 0, bounds.Width);
        var y = cursor.Y - Math.Clamp(anchor.Y - bounds.Top, 0, Math.Min(captionHeight, bounds.Height));
        // Preserve oversized windows (including MinWidth/MinHeight). Keep a usable title strip
        // on the cursor monitor without imposing a primary-monitor origin or resizing anything.
        x = Math.Clamp(x, Math.Min(workArea.Left, workArea.Right - bounds.Width),
            Math.Max(workArea.Left, workArea.Right - bounds.Width));
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top,
            workArea.Bottom - Math.Min(captionHeight, bounds.Height)));
        if (!IsFinite(x, y) || x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
            return false;
        position = new Point(x, y);
        return true;
    }

    private static bool IsFinite(params double[] values) => values.All(double.IsFinite);

    internal sealed class MaximizedDragSession(Window window, UIElement region, Point start, Point grip)
    {
        private bool _ended;
        private readonly double _horizontalGrip = Math.Clamp(grip.X / window.ActualWidth, 0, 1);
        private readonly double _captionBottom = Math.Max(1,
            region.TranslatePoint(new Point(0, region.RenderSize.Height), window).Y);

        public bool Begin()
        {
            Track();
            try
            {
                if (region.CaptureMouse() && !_ended && ReferenceEquals(Mouse.Captured, region)) return true;
            }
            catch (InvalidOperationException) { }
            Cancel();
            return false;
        }

        internal void Track()
        {
            region.SetValue(PendingDragProperty, this);
            region.PreviewMouseMove += MouseMove;
            region.PreviewMouseLeftButtonUp += MouseUp;
            region.LostMouseCapture += LostCapture;
            region.IsEnabledChanged += AvailabilityChanged;
            region.IsVisibleChanged += AvailabilityChanged;
            if (region is FrameworkElement frameworkElement) frameworkElement.Unloaded += Unloaded;
            window.Deactivated += Aborted;
            window.Closed += Aborted;
            window.StateChanged += Aborted;
            window.IsEnabledChanged += AvailabilityChanged;
            window.PreviewKeyDown += KeyDown;
        }

        public void Cancel()
        {
            if (_ended) return;
            _ended = true;
            region.ClearValue(PendingDragProperty);
            region.PreviewMouseMove -= MouseMove;
            region.PreviewMouseLeftButtonUp -= MouseUp;
            region.LostMouseCapture -= LostCapture;
            region.IsEnabledChanged -= AvailabilityChanged;
            region.IsVisibleChanged -= AvailabilityChanged;
            if (region is FrameworkElement frameworkElement) frameworkElement.Unloaded -= Unloaded;
            window.Deactivated -= Aborted;
            window.Closed -= Aborted;
            window.StateChanged -= Aborted;
            window.IsEnabledChanged -= AvailabilityChanged;
            window.PreviewKeyDown -= KeyDown;
            if (ReferenceEquals(Mouse.Captured, region)) region.ReleaseMouseCapture();
        }

        private void MouseUp(object sender, MouseButtonEventArgs e) => Cancel();
        private void LostCapture(object sender, MouseEventArgs e) => Cancel();
        private void AvailabilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Cancel();
        private void Unloaded(object sender, RoutedEventArgs e) => Cancel();
        private void Aborted(object? sender, EventArgs e) => Cancel();
        private void KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Cancel();
        }

        private void MouseMove(object sender, MouseEventArgs e)
        {
            if (_ended) return;
            if (e.LeftButton != MouseButtonState.Pressed || Mouse.LeftButton != MouseButtonState.Pressed)
            {
                Cancel();
                return;
            }
            if (!ExceedsDragThreshold(start, e.GetPosition(region))) return;
            e.Handled = true;
            // Release our temporary capture before WPF enters its native, blocking move loop.
            Cancel();
            RestoreAndDrag();
        }

        private void RestoreAndDrag()
        {
            var restored = false;
            try
            {
                var restore = window.RestoreBounds;
                if (window.WindowState != WindowState.Maximized || !window.IsVisible || !window.IsEnabled ||
                    window.ResizeMode is not (ResizeMode.CanResize or ResizeMode.CanResizeWithGrip) ||
                    restore.IsEmpty || restore.Width <= 0 || restore.Height <= 0 ||
                    !IsFinite(restore.Width, restore.Height) || Mouse.LeftButton != MouseButtonState.Pressed ||
                    !GetCursorPos(out var cursor)) return;

                var work = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
                if (work.Width <= 0 || work.Height <= 0) return;
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                window.WindowState = WindowState.Normal; // WPF owns the previous DIP size and size constraints.
                restored = true;
                window.UpdateLayout();
                if (!GetWindowRect(handle, out var nativeBounds)) throw new InvalidOperationException("Window bounds unavailable.");
                var bounds = new Rect(nativeBounds.Left, nativeBounds.Top,
                    nativeBounds.Right - nativeBounds.Left, nativeBounds.Bottom - nativeBounds.Top);
                // PointToScreen converts local DIPs using this HWND's actual DPI. Never scale a
                // global desktop origin, which would be incorrect on mixed-DPI/negative monitors.
                var captionBottom = window.PointToScreen(new Point(0, _captionBottom));
                var captionTop = window.PointToScreen(new Point());
                var anchor = window.PointToScreen(new Point(window.ActualWidth * _horizontalGrip,
                    Math.Clamp(grip.Y, 0, _captionBottom)));
                if (!TryGetRestorePlacement(new Point(cursor.X, cursor.Y), bounds, anchor,
                    new Rect(work.X, work.Y, work.Width, work.Height),
                    Math.Max(1, captionBottom.Y - captionTop.Y), out var position) ||
                    !SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(position.X), (int)Math.Round(position.Y),
                        0, 0, 0x0001 | 0x0004 | 0x0010)) // NOSIZE | NOZORDER | NOACTIVATE
                    throw new InvalidOperationException("Window placement unavailable.");

                if (Mouse.LeftButton == MouseButtonState.Pressed && window.IsVisible && window.IsEnabled)
                    window.DragMove();
            }
            catch (Exception)
            {
                // A disappearing HWND/display or a button release must never escape to the dispatcher.
                // Roll back a failed placement; neither normal size nor persisted settings are rewritten.
                if (restored && window.IsVisible)
                {
                    try { window.WindowState = WindowState.Maximized; }
                    catch (InvalidOperationException) { }
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
