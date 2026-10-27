using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Core.Normalization;

internal sealed class CoverImageColorManagementService
{
    private readonly ColorContext _srgbContext = new(PixelFormats.Bgra32);

    public CoverImageColorManagementResult NormalizeToSrgb(
        BitmapSource source,
        ColorContext? sourceColorContext)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (sourceColorContext is null)
        {
            return new CoverImageColorManagementResult(
                source,
                SourceProfileDetected: false,
                ConvertedToSrgb: false,
                TechnicalCause: null);
        }

        try
        {
            var converted = new ColorConvertedBitmap(
                source,
                sourceColorContext,
                _srgbContext,
                PixelFormats.Pbgra32);
            converted.Freeze();

            return new CoverImageColorManagementResult(
                converted,
                SourceProfileDetected: true,
                ConvertedToSrgb: true,
                TechnicalCause: null);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            InvalidOperationException or
            NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            // A malformed/unsupported embedded profile must not make an otherwise
            // readable image unusable. The normal StandardCover path assumes sRGB
            // when WIC cannot perform the profile conversion.
            return new CoverImageColorManagementResult(
                source,
                SourceProfileDetected: true,
                ConvertedToSrgb: false,
                TechnicalCause: ex.Message);
        }
    }
}

internal sealed record CoverImageColorManagementResult(
    BitmapSource Bitmap,
    bool SourceProfileDetected,
    bool ConvertedToSrgb,
    string? TechnicalCause);
