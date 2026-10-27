using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class CoverImageScaleServiceTests
{
    [TestMethod]
    public void ScaleSquare_HighQualityDownscaleBlendsSharpCheckerboard()
    {
        const int size = 8;
        var stride = size * 4;
        var pixels = new byte[stride * size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var value = (x + y) % 2 == 0 ? byte.MinValue : byte.MaxValue;
                var offset = y * stride + x * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        var source = BitmapSource.Create(
            size,
            size,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        source.Freeze();

        var scaled = new CoverImageScaleService().ScaleSquare(source, 1);
        var output = new byte[4];
        scaled.CopyPixels(output, 4, 0);

        Assert.AreEqual(1, scaled.PixelWidth);
        Assert.AreEqual(1, scaled.PixelHeight);
        Assert.AreEqual(byte.MaxValue, output[3]);
        Assert.IsGreaterThan((byte)40, output[0], $"Expected blended blue channel, got {output[0]}.");
        Assert.IsLessThan((byte)215, output[0], $"Expected blended blue channel, got {output[0]}.");
        Assert.IsGreaterThan((byte)40, output[1], $"Expected blended green channel, got {output[1]}.");
        Assert.IsLessThan((byte)215, output[1], $"Expected blended green channel, got {output[1]}.");
        Assert.IsGreaterThan((byte)40, output[2], $"Expected blended red channel, got {output[2]}.");
        Assert.IsLessThan((byte)215, output[2], $"Expected blended red channel, got {output[2]}.");
    }
}
