using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoverImageDrop.Core.Imaging;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Normalization;
using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Core.Tests;

[TestClass]
public sealed class BundledWebPTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "WebP", name));

    [TestMethod]
    [DataRow("lossy.webp")]
    [DataRow("lossless-alpha.webp")]
    [DataRow("oriented-icc.webp")]
    [DataRow("animated.webp")]
    public void WebP_always_uses_the_bundled_decoder_and_normalizes(string name)
    {
        var input = Fixture(name);
        var info = new CoverImageInspector().InspectBytes(input);
        Assert.IsTrue(info.IsSuccess, string.Join(";", info.Issues));
        Assert.AreEqual("Bundled libwebp 1.6.0", info.DecoderCodec);
        var output = new CoverImageNormalizer().NormalizeBytes(input);
        Assert.AreEqual(1000, output.Width);
        Assert.AreEqual(CoverOutputFormat.Jpeg, output.Format);
        Assert.IsTrue(output.Bytes.Length > 0);
    }

    [TestMethod]
    public void WebP_profile_and_orientation_reach_the_common_color_and_geometry_stages()
    {
        var input = Fixture("oriented-icc.webp");
        var info = new CoverImageInspector().InspectBytes(input);
        Assert.AreEqual(800, info.PixelWidth);
        Assert.AreEqual(600, info.PixelHeight);
        Assert.AreEqual(600, info.OrientedPixelWidth);
        Assert.AreEqual(800, info.OrientedPixelHeight);
        Assert.IsTrue(info.HasEmbeddedColorProfile);
        var decoded = new CoverImageDecoder().Decode(input);
        Assert.IsNotNull(decoded.SourceColorContext);
        var output = new CoverImageNormalizer().NormalizeBytes(input, null,
            new CoverImageNormalizationOptions { TargetSizePixels = 600, Format = CoverOutputFormat.Png });
        Assert.IsFalse(output.Issues.Any(i => i.Code == Issues.CoverImageIssueCode.ColorProfileConversionFailed));
        using var stream = new MemoryStream(output.Bytes);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        bgra.CopyPixels(new System.Windows.Int32Rect(50, 50, 1, 1), pixel, 4, 0);
        Assert.IsGreaterThan((byte)240, pixel[0]); // blue quadrant rotated to top-left
        Assert.IsLessThan((byte)20, pixel[2]);
    }

    [TestMethod]
    public void Animated_WebP_uses_the_first_composited_canvas_only()
    {
        var image = BundledWebPDecoder.Read(Fixture("animated.webp"), decode: true);
        Assert.AreEqual(2, image.Frames);
        Assert.AreEqual((byte)0, image.Pixels[0]);
        Assert.AreEqual((byte)255, image.Pixels[2]);
        Assert.AreEqual((byte)255, image.Pixels[3]);
    }

    [TestMethod]
    public void WebP_alpha_survives_decoding_and_PNG_output()
    {
        var output = new CoverImageNormalizer().NormalizeBytes(Fixture("lossless-alpha.webp"), null,
            new CoverImageNormalizationOptions { TargetSizePixels = 600, Format = CoverOutputFormat.Png });
        using var stream = new MemoryStream(output.Bytes);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        bgra.CopyPixels(new System.Windows.Int32Rect(500, 500, 1, 1), pixel, 4, 0);
        Assert.IsTrue(pixel[3] is >= 126 and <= 130);
    }

    [TestMethod]
    public void Truncated_WebP_is_rejected_without_replacing_the_host_decoder()
    {
        var input = Fixture("lossless-alpha.webp");
        var info = new CoverImageInspector().InspectBytes(input[..(input.Length / 2)]);
        Assert.IsFalse(info.IsSuccess);
        Assert.IsTrue(new CoverImageInspector().InspectBytes(input).IsSuccess);
    }

    [TestMethod]
    public void Invalid_WebP_profile_keeps_the_image_with_an_explicit_warning()
    {
        var output = new CoverImageNormalizer().NormalizeBytes(Fixture("invalid-icc.webp"));
        Assert.IsTrue(output.Issues.Any(i => i.Code == Issues.CoverImageIssueCode.ColorProfileConversionFailed));
        Assert.AreEqual(1000, output.Width);
    }

    [TestMethod]
    public void Hostile_Exif_offsets_and_counts_are_ignored_without_out_of_bounds_reads()
    {
        byte[] header = [73, 73, 42, 0, 255, 255, 255, 255];
        Assert.IsNull(BundledWebPDecoder.ReadOrientation(header));
        byte[] count = [73, 73, 42, 0, 8, 0, 0, 0, 255, 255];
        Assert.IsNull(BundledWebPDecoder.ReadOrientation(count));
    }

    [TestMethod]
    public void Unprofiled_CMYK_JPEG_can_be_normalized()
    {
        var output = new CoverImageNormalizer().NormalizeBytes(Fixture("cmyk.jpg"));
        Assert.AreEqual(1000, output.Width);
    }
}
