using BookStitch.Services;
using System.Runtime.InteropServices;
using Xunit;
using static BookStitch.Services.WindowMaximizeGeometry;

namespace BookStitch.Tests.Services;

public sealed class WindowMaximizeGeometryTests
{
    [Theory]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1040, 0, 0, 1920, 1040)]
    [InlineData(0, 0, 1920, 1080, 0, 40, 1920, 1080, 0, 40, 1920, 1040)]
    [InlineData(0, 0, 1920, 1080, 40, 0, 1920, 1080, 40, 0, 1880, 1080)]
    [InlineData(0, 0, 1920, 1080, 0, 0, 1880, 1080, 0, 0, 1880, 1080)]
    [InlineData(-1920, -1080, 0, 0, -1880, -1050, 0, -40, 40, 30, 1880, 1010)]
    [InlineData(1920, 0, 5760, 2160, 1920, 0, 5760, 2100, 0, 0, 3840, 2100)]
    [InlineData(0, -1440, 2560, 0, 0, -1400, 2560, 0, 0, 40, 2560, 1400)]
    [InlineData(0, 0, 800, 600, 0, 0, 800, 560, 0, 0, 800, 560)]
    public void Work_area_maps_to_monitor_relative_physical_pixels_and_preserves_tracking(
        int ml, int mt, int mr, int mb, int wl, int wt, int wr, int wb, int x, int y, int width, int height)
    {
        var info = Seed();
        Assert.True(TryApply(true, Rect(ml, mt, mr, mb), Rect(wl, wt, wr, wb), ref info));
        Assert.Equal((x, y), (info.MaxPosition.X, info.MaxPosition.Y));
        Assert.Equal((width, height), (info.MaxSize.X, info.MaxSize.Y));
        Assert.Equal(Seed().Reserved, info.Reserved);
        Assert.Equal(Seed().MinTrackSize, info.MinTrackSize);
        Assert.Equal(Seed().MaxTrackSize, info.MaxTrackSize);
    }

    [Theory]
    [InlineData(false, 0, 0, 1920, 1080, 0, 0, 1920, 1040)]
    [InlineData(true, 0, 0, 0, 1080, 0, 0, 0, 1040)]
    [InlineData(true, 0, 1080, 1920, 0, 0, 0, 1920, 1040)]
    [InlineData(true, 0, 0, 1920, 1080, 0, 40, 1920, 40)]
    [InlineData(true, 0, 0, 1920, 1080, -1, 0, 1920, 1040)]
    [InlineData(true, 0, 0, 1920, 1080, 0, 0, 1921, 1040)]
    [InlineData(true, int.MinValue, 0, int.MaxValue, 1080, int.MinValue, 0, int.MaxValue, 1040)]
    public void Failed_lookup_invalid_or_overflowing_rectangles_leave_native_values_untouched(
        bool success, int ml, int mt, int mr, int mb, int wl, int wt, int wr, int wb)
    {
        var info = Seed();
        Assert.False(TryApply(success, Rect(ml, mt, mr, mb), Rect(wl, wt, wr, wb), ref info));
        Assert.Equal(Seed(), info);
    }

    [Theory]
    [InlineData(96, 1920, 1080, 40)]
    [InlineData(120, 2400, 1350, 50)]
    [InlineData(144, 2880, 1620, 60)]
    [InlineData(192, 3840, 2160, 80)]
    public void Native_pixel_geometry_is_not_scaled_a_second_time(int dpi, int width, int height, int taskbar)
    {
        Assert.True(dpi >= 96);
        var info = Seed();
        Assert.True(TryApply(true, Rect(-width, 0, 0, height), Rect(-width, taskbar, 0, height), ref info));
        Assert.Equal(width, info.MaxSize.X);
        Assert.Equal(height - taskbar, info.MaxSize.Y);
        Assert.Equal(taskbar, info.MaxPosition.Y);
    }

    [Fact]
    public void Native_minmax_layout_is_five_32_bit_point_pairs_on_both_architectures()
    {
        Assert.Equal(16, Marshal.SizeOf<PixelRect>());
        Assert.Equal(40, Marshal.SizeOf<MinMaxInfo>());
        Assert.Equal(32, Marshal.OffsetOf<MinMaxInfo>(nameof(MinMaxInfo.MaxTrackSize)).ToInt32());
    }

    private static PixelRect Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };
    private static MinMaxInfo Seed() => new()
    {
        Reserved = new() { X = 17, Y = 19 }, MaxPosition = new() { X = -9, Y = -8 },
        MaxSize = new() { X = 123, Y = 456 }, MinTrackSize = new() { X = 1170, Y = 760 },
        MaxTrackSize = new() { X = 9000, Y = 6000 }
    };
}
