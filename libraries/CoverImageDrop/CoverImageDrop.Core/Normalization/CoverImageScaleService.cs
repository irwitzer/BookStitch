using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Core.Normalization;

internal sealed class CoverImageScaleService
{
    public BitmapSource ScaleSquare(BitmapSource source, int targetSizePixels)
    {
        return Scale(source, targetSizePixels, targetSizePixels);
    }

    public BitmapSource ScaleToFit(BitmapSource source, int maxLongEdgePixels)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maxLongEdgePixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLongEdgePixels));
        }

        var longEdge = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longEdge <= maxLongEdgePixels)
        {
            return source;
        }

        var scale = maxLongEdgePixels / (double)longEdge;
        var targetWidth = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        return Scale(source, targetWidth, targetHeight);
    }

    private static BitmapSource Scale(BitmapSource source, int targetWidth, int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (targetWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth));
        }

        if (targetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetHeight));
        }

        if (source.PixelWidth == targetWidth && source.PixelHeight == targetHeight)
        {
            return source;
        }

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, targetWidth, targetHeight));
        }

        var rendered = new RenderTargetBitmap(
            targetWidth,
            targetHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }
}
