using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Core.Tests;

internal static class TestImageFactory
{
    public static byte[] CreateSolidPng(int width, int height, Color color)
    {
        return Encode(CreateSolidBitmap(width, height, color), new PngBitmapEncoder());
    }

    public static byte[] CreateTransparentPng(int width, int height)
    {
        return Encode(CreateSolidBitmap(width, height, Colors.Transparent), new PngBitmapEncoder());
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
        return Encode(CreateTwoColorBitmap(width, height, leftColor, rightColor), new PngBitmapEncoder());
    }

    public static BitmapSource CreateQuadrantBitmap(int width, int height)
    {
        if (width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new int[width * height];
        var splitX = width / 2;
        var splitY = height / 2;
        var topLeft = ToBgra(Colors.Red);
        var topRight = ToBgra(Colors.Lime);
        var bottomLeft = ToBgra(Colors.Blue);
        var bottomRight = ToBgra(Colors.Yellow);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[y * width + x] = (x < splitX, y < splitY) switch
                {
                    (true, true) => topLeft,
                    (false, true) => topRight,
                    (true, false) => bottomLeft,
                    _ => bottomRight
                };
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] CreateJpegWithExifOrientation(int width, int height, ushort orientation)
    {
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);

        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CreateSolidBitmap(width, height, Colors.Red), null, metadata, null));
        return Save(encoder);
    }

    public static byte[] CreateTwoColorJpegWithExifOrientation(
        int width,
        int height,
        Color leftColor,
        Color rightColor,
        ushort orientation)
    {
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);

        var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
        encoder.Frames.Add(BitmapFrame.Create(
            CreateTwoColorBitmap(width, height, leftColor, rightColor),
            null,
            metadata,
            null));
        return Save(encoder);
    }

    public static byte[] CreateJpegWithSensitiveMetadata(int width, int height)
    {
        var metadata = new BitmapMetadata("jpg")
        {
            Comment = "CoverImageDrop secret comment"
        };
        metadata.SetQuery("/app1/ifd/{ushort=270}", "CoverImageDrop secret description");
        metadata.SetQuery("/app1/ifd/{ushort=271}", "CoverImageDrop Test Camera");
        metadata.SetQuery("/app1/ifd/{ushort=272}", "CID-Model-1");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6);

        var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
        encoder.Frames.Add(BitmapFrame.Create(
            CreateSolidBitmap(width, height, Colors.CornflowerBlue),
            null,
            metadata,
            null));
        return Save(encoder);
    }

    public static byte[] CreateJpegWithSrgbColorContext(int width, int height)
    {
        var contexts = new ReadOnlyCollection<ColorContext>(
            new List<ColorContext> { new(PixelFormats.Bgra32) });
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(
            CreateSolidBitmap(width, height, Colors.MediumSeaGreen),
            null,
            null,
            contexts));
        return Save(encoder);
    }

    public static byte[] CreateTwoFrameTiff(Color firstFrameColor, Color secondFrameColor)
    {
        var encoder = new TiffBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(CreateSolidBitmap(160, 160, firstFrameColor)));
        encoder.Frames.Add(BitmapFrame.Create(CreateSolidBitmap(160, 160, secondFrameColor)));
        return Save(encoder);
    }

    public static (int Width, int Height) ReadDimensions(byte[] bytes)
    {
        var frame = DecodeFirstFrame(bytes);
        return (frame.PixelWidth, frame.PixelHeight);
    }

    public static Color ReadCenterPixel(byte[] bytes)
    {
        var frame = DecodeFirstFrame(bytes);
        return ReadPixel(frame, frame.PixelWidth / 2, frame.PixelHeight / 2);
    }

    public static Color ReadPixel(byte[] bytes, int x, int y)
    {
        var frame = DecodeFirstFrame(bytes);
        return ReadPixel(frame, x, y);
    }

    public static BitmapMetadata? ReadMetadata(byte[] bytes)
    {
        return DecodeFirstFrame(bytes).Metadata as BitmapMetadata;
    }

    public static bool MetadataContainsQuery(byte[] bytes, string query)
    {
        var metadata = ReadMetadata(bytes);
        if (metadata is null)
        {
            return false;
        }

        try
        {
            return metadata.ContainsQuery(query);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }

    public static Color ReadPixel(BitmapSource source, int x, int y)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        x = Math.Clamp(x, 0, converted.PixelWidth - 1);
        y = Math.Clamp(y, 0, converted.PixelHeight - 1);
        var offset = y * stride + x * 4;
        return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static BitmapSource DecodeFirstFrame(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static BitmapSource CreateSolidBitmap(int width, int height, Color color)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = Enumerable.Repeat(ToBgra(color), width * height).ToArray();
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CreateTwoColorBitmap(
        int width,
        int height,
        Color leftColor,
        Color rightColor)
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
        return bitmap;
    }

    private static int ToBgra(Color color)
    {
        return color.B | color.G << 8 | color.R << 16 | color.A << 24;
    }

    private static byte[] Encode(BitmapSource bitmap, BitmapEncoder encoder)
    {
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        return Save(encoder);
    }

    private static byte[] Save(BitmapEncoder encoder)
    {
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
