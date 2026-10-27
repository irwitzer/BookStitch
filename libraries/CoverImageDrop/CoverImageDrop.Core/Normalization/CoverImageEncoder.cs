using System.IO;
using System.Windows.Media.Imaging;
using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Core.Normalization;

internal sealed class CoverImageEncoder
{
    internal const int Quality94MaxBytes = 1 * 1024 * 1024;
    internal const int Quality92MaxBytes = 4 * 1024 * 1024;

    public CoverImageEncodingResult Encode(BitmapSource source, CoverOutputFormat format)
    {
        ArgumentNullException.ThrowIfNull(source);

        return format switch
        {
            CoverOutputFormat.Jpeg => EncodeJpegAdaptive(
                quality => EncodeSingle(source, new JpegBitmapEncoder { QualityLevel = quality })),
            CoverOutputFormat.Png => new CoverImageEncodingResult(
                EncodeSingle(source, new PngBitmapEncoder()),
                null,
                1),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unbekanntes Outputformat.")
        };
    }

    internal static CoverImageEncodingResult EncodeJpegAdaptive(Func<int, byte[]> encodeAtQuality)
    {
        ArgumentNullException.ThrowIfNull(encodeAtQuality);

        var quality94 = encodeAtQuality(94);
        if (quality94.LongLength <= Quality94MaxBytes)
        {
            return new CoverImageEncodingResult(quality94, 94, 1);
        }

        var quality92 = encodeAtQuality(92);
        if (quality92.LongLength <= Quality92MaxBytes)
        {
            return new CoverImageEncodingResult(quality92, 92, 2);
        }

        var quality90 = encodeAtQuality(90);
        return new CoverImageEncodingResult(quality90, 90, 3);
    }

    private static byte[] EncodeSingle(BitmapSource source, BitmapEncoder encoder)
    {
        // Build a fresh frame without source thumbnail, metadata or source color-context
        // objects. Pixel values have already gone through the StandardCover color path.
        // This prevents EXIF/GPS/camera/comment/application metadata from leaking into output.
        encoder.Frames.Add(BitmapFrame.Create(source, null!, null!, null!));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}

internal sealed record CoverImageEncodingResult(
    byte[] Bytes,
    int? JpegQuality,
    int EncodeAttempts);
