using BookStitch.Models;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BookStitch.Services;

public sealed class WindowAttentionService
{
    private const uint FlashTray = 0x00000002;

    private readonly Func<AppSettings> _settingsProvider;
    private readonly Func<Window?> _windowProvider;

    public WindowAttentionService(Func<AppSettings> settingsProvider, Func<Window?> windowProvider)
    {
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
    }

    public void RequestAttention(NotificationEvent notificationEvent)
    {
        var settings = _settingsProvider();
        var profile = FocusSettingsService.NormalizeProfile(settings.FocusProfile);
        var plan = FocusSettingsService.GetAttentionPlan(profile, notificationEvent);
        if (!plan.IsEnabled)
            return;

        var application = Application.Current;
        if (application is null)
            return;

        void Apply()
        {
            var window = _windowProvider();
            if (window is null)
                return;

            if (plan.BringToForeground)
                BringWindowToForeground(window, plan.UseTemporaryTopmost);

            FlashTaskbar(window, plan.FlashCount);
        }

        if (application.Dispatcher.CheckAccess())
            Apply();
        else
            application.Dispatcher.BeginInvoke(Apply);
    }

    private static void BringWindowToForeground(Window window, bool useTemporaryTopmost)
    {
        if (!window.IsVisible)
            window.Show();

        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        if (useTemporaryTopmost)
        {
            var wasTopmost = window.Topmost;
            window.Topmost = true;
            window.Activate();
            SetForegroundWindow(handle);
            window.Topmost = wasTopmost;
            return;
        }

        window.Activate();
        SetForegroundWindow(handle);
    }

    private static void FlashTaskbar(Window window, int count)
    {
        if (count <= 0)
            return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            WindowHandle = handle,
            Flags = FlashTray,
            Count = (uint)count,
            TimeoutMilliseconds = 0
        };

        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Flags;
        public uint Count;
        public uint TimeoutMilliseconds;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
