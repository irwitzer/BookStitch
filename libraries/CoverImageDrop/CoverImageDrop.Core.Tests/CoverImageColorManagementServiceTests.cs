using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImageColorManagementServiceTests
{
    [TestMethod]
    public void NormalizeToSrgb_WithoutSourceProfileUsesNormalSrgbFallbackPath()
    {
        var source = CreateSolidBitmap(4, 4, Colors.SteelBlue);
        var result = new CoverImageColorManagementService().NormalizeToSrgb(source, null);

        Assert.IsFalse(result.SourceProfileDetected);
        Assert.IsFalse(result.ConvertedToSrgb);
        Assert.IsNull(result.TechnicalCause);
        Assert.AreSame(source, result.Bitmap);
    }

    [TestMethod]
    public void NormalizeToSrgb_WithKnownRgbProfileUsesWicColorConversion()
    {
        var source = CreateSolidBitmap(4, 4, Colors.MediumSeaGreen);
        var sourceContext = new ColorContext(PixelFormats.Bgra32);
        var result = new CoverImageColorManagementService().NormalizeToSrgb(source, sourceContext);

        Assert.IsTrue(result.SourceProfileDetected);
        Assert.IsTrue(result.ConvertedToSrgb, result.TechnicalCause);
        Assert.IsNull(result.TechnicalCause);
        Assert.AreEqual(PixelFormats.Pbgra32, result.Bitmap.Format);
        Assert.AreEqual(source.PixelWidth, result.Bitmap.PixelWidth);
        Assert.AreEqual(source.PixelHeight, result.Bitmap.PixelHeight);
    }

    private static BitmapSource CreateSolidBitmap(int width, int height, Color color)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = color.A;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }
}
