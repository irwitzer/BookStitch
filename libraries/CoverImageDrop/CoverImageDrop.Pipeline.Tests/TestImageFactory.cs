using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Pipeline.Tests;

internal static class TestImageFactory
{
    public static byte[] CreateSolidPng(int width, int height, Color color)
    {
        return Encode(CreateSolidBitmap(width, height, color), new PngBitmapEncoder());
    }

    public static byte[] CreateHalfTransparentPng(int width, int height, Color visibleColor)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new int[width * height];
        var splitX = width / 2;
        var transparentPixel = ToBgra(Colors.Transparent);
        var visiblePixel = ToBgra(visibleColor);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[y * width + x] = x < splitX ? transparentPixel : visiblePixel;
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return Encode(bitmap, new PngBitmapEncoder());
    }

    public static byte[] CreateTwoColorPng(int width, int height, Color leftColor, Color rightColor)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new int[width * height];
        var splitX = width / 2;
        var leftPixel = ToBgra(leftColor);
        var rightPixel = ToBgra(rightColor);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[y * width + x] = x < splitX ? leftPixel : rightPixel;
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return Encode(bitmap, new PngBitmapEncoder());
    }

    public static Color ReadCenterPixel(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var x = converted.PixelWidth / 2;
        var y = converted.PixelHeight / 2;
        var offset = y * stride + x * 4;
        return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static BitmapSource CreateSolidBitmap(int width, int height, Color color)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = Enumerable.Repeat(ToBgra(color), width * height).ToArray();
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private static int ToBgra(Color color)
    {
        return color.B | color.G << 8 | color.R << 16 | color.A << 24;
    }

    private static byte[] Encode(BitmapSource bitmap, BitmapEncoder encoder)
    {
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
