using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Core.Normalization;

internal sealed class CoverImageColorAlphaService
{
    private readonly CoverImageAlphaAnalyzer _alphaAnalyzer = new();

    public BitmapSource PrepareForScaling(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Format == PixelFormats.Pbgra32)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    public CoverImagePreparedOutput PrepareForOutput(
        BitmapSource source,
        CoverOutputFormat format,
        CoverImageMatte? requestedJpegMatte)
    {
        ArgumentNullException.ThrowIfNull(source);

        var alphaAnalysis = _alphaAnalyzer.Analyze(source);
        if (format == CoverOutputFormat.Png)
        {
            return new CoverImagePreparedOutput(
                EnsureBgra32(source),
                alphaAnalysis.HasTransparency,
                null,
                alphaAnalysis);
        }

        if (format != CoverOutputFormat.Jpeg)
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unbekanntes Outputformat.");
        }

        if (!alphaAnalysis.HasTransparency)
        {
            return new CoverImagePreparedOutput(
                EnsureBgr24(source),
                false,
                null,
                alphaAnalysis);
        }

        var resolvedMatte = requestedJpegMatte ?? alphaAnalysis.SuggestedMatte;
        var flattened = Flatten(source, resolvedMatte);
        return new CoverImagePreparedOutput(
            flattened,
            true,
            resolvedMatte,
            alphaAnalysis);
    }

    private static BitmapSource Flatten(BitmapSource source, CoverImageMatte matte)
    {
        var matteColor = matte == CoverImageMatte.Black
            ? Colors.Black
            : Colors.White;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var brush = new SolidColorBrush(matteColor);
            brush.Freeze();
            context.DrawRectangle(brush, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
        }

        var rendered = new RenderTargetBitmap(
            source.PixelWidth,
            source.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return EnsureBgr24(rendered);
    }

    private static BitmapSource EnsureBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static BitmapSource EnsureBgr24(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgr24)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
        converted.Freeze();
        return converted;
    }
}

internal sealed record CoverImagePreparedOutput(
    BitmapSource Bitmap,
    bool HadTransparency,
    CoverImageMatte? ResolvedJpegMatte,
    CoverImageAlphaAnalysis AlphaAnalysis);
