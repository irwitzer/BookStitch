using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BookStitch.Tests.TestHelpers;

internal static class CoverTestImages
{
    public static byte[] Create(int width, int height, bool jpeg = false, byte alpha = 255)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var i = 0; i < pixels.Length; i += 4)
        { pixels[i] = 50; pixels[i + 1] = 120; pixels[i + 2] = 200; pixels[i + 3] = alpha; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder() : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
