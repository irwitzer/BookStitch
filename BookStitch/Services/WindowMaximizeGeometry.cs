using System.Runtime.InteropServices;

namespace BookStitch.Services;

// Win32 monitor coordinates and MINMAXINFO are physical pixels, never WPF DIPs.
internal static class WindowMaximizeGeometry
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PixelPoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PixelRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MinMaxInfo
    {
        public PixelPoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    internal static bool TryApply(bool lookupSucceeded, PixelRect monitor, PixelRect work, ref MinMaxInfo info)
    {
        if (!lookupSucceeded || monitor.Right <= monitor.Left || monitor.Bottom <= monitor.Top ||
            work.Right <= work.Left || work.Bottom <= work.Top ||
            work.Left < monitor.Left || work.Top < monitor.Top ||
            work.Right > monitor.Right || work.Bottom > monitor.Bottom)
            return false;

        var x = (long)work.Left - monitor.Left;
        var y = (long)work.Top - monitor.Top;
        var width = (long)work.Right - work.Left;
        var height = (long)work.Bottom - work.Top;
        if (x > int.MaxValue || y > int.MaxValue || width > int.MaxValue || height > int.MaxValue)
            return false;

        info.MaxPosition = new PixelPoint { X = (int)x, Y = (int)y };
        info.MaxSize = new PixelPoint { X = (int)width, Y = (int)height };
        return true; // Reserved and both tracking limits belong to Windows/WPF.
    }
}
