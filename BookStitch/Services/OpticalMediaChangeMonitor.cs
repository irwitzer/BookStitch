using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BookStitch.Services;

public sealed class OpticalMediaChangedEventArgs(string driveRoot, bool isArrival) : EventArgs
{
    public string DriveRoot { get; } = driveRoot;
    public bool IsArrival { get; } = isArrival;
}

public sealed class OpticalMediaChangeMonitor : IDisposable
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevTypeVolume = 0x00000002;
    private const ushort DbtfMedia = 0x0001;

    private readonly Window _window;
    private HwndSource? _source;
    private bool _disposed;

    public OpticalMediaChangeMonitor(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _window.SourceInitialized += Window_SourceInitialized;

        if (new WindowInteropHelper(_window).Handle != IntPtr.Zero)
            AttachHook();
    }

    public event EventHandler<OpticalMediaChangedEventArgs>? MediaChanged;

    private void Window_SourceInitialized(object? sender, EventArgs e) => AttachHook();

    private void AttachHook()
    {
        if (_disposed || _source is not null)
            return;

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero)
            return;

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmDeviceChange || lParam == IntPtr.Zero)
            return IntPtr.Zero;

        var change = unchecked((int)wParam.ToInt64());
        if (change is not (DbtDeviceArrival or DbtDeviceRemoveComplete))
            return IntPtr.Zero;

        var header = Marshal.PtrToStructure<DeviceBroadcastHeader>(lParam);
        if (header.DeviceType != DbtDevTypeVolume)
            return IntPtr.Zero;

        var volume = Marshal.PtrToStructure<DeviceBroadcastVolume>(lParam);
        if ((volume.Flags & DbtfMedia) == 0)
            return IntPtr.Zero;

        var isArrival = change == DbtDeviceArrival;
        foreach (var driveRoot in DecodeDriveRoots(volume.UnitMask))
            MediaChanged?.Invoke(this, new OpticalMediaChangedEventArgs(driveRoot, isArrival));

        return IntPtr.Zero;
    }

    internal static IReadOnlyList<string> DecodeDriveRoots(uint unitMask)
    {
        var roots = new List<string>();
        for (var bit = 0; bit < 26; bit++)
        {
            if ((unitMask & (1u << bit)) == 0)
                continue;

            roots.Add($"{(char)('A' + bit)}:\\");
        }

        return roots;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.SourceInitialized -= Window_SourceInitialized;
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceBroadcastHeader
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceBroadcastVolume
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public uint UnitMask;
        public ushort Flags;
    }
}
