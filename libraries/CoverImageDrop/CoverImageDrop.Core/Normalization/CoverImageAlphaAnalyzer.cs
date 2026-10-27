using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Core.Normalization;

internal sealed class CoverImageAlphaAnalyzer
{
    private const int MaxBrightnessSamples = 8192;
    private const double DarkLuminanceThreshold = 96d;
    private const double LightLuminanceThreshold = 160d;

    public CoverImageAlphaAnalysis Analyze(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var prepared = EnsurePbgra32(source);
        var width = prepared.PixelWidth;
        var height = prepared.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return new CoverImageAlphaAnalysis(false, CoverImageMatte.White, 0, null);
        }

        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        prepared.CopyPixels(pixels, stride, 0);

        var hasTransparency = false;
        long luminanceSum = 0;
        var sampleCount = 0;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + (x * 4);
                var alpha = pixels[offset + 3];
                if (alpha == byte.MaxValue)
                {
                    continue;
                }

                hasTransparency = true;
                if (sampleCount >= MaxBrightnessSamples)
                {
                    continue;
                }

                if (alpha > 0)
                {
                    luminanceSum += CalculateUnpremultipliedLuminance(pixels, offset, alpha);
                    sampleCount++;
                    continue;
                }

                SampleVisibleNeighbours(
                    pixels,
                    width,
                    height,
                    stride,
                    x,
                    y,
                    ref luminanceSum,
                    ref sampleCount);
            }
        }

        if (!hasTransparency)
        {
            return new CoverImageAlphaAnalysis(false, CoverImageMatte.White, 0, null);
        }

        if (sampleCount == 0)
        {
            return new CoverImageAlphaAnalysis(true, CoverImageMatte.White, 0, null);
        }

        var averageLuminance = (double)luminanceSum / sampleCount;
        var matte = averageLuminance >= LightLuminanceThreshold
            ? CoverImageMatte.Black
            : CoverImageMatte.White;

        // Ambiguous mid tones deliberately fall back to white. The explicit dark
        // threshold is kept as part of the contract and documents that dark edge
        // content also resolves to white rather than inventing another color.
        if (averageLuminance > DarkLuminanceThreshold && averageLuminance < LightLuminanceThreshold)
        {
            matte = CoverImageMatte.White;
        }

        return new CoverImageAlphaAnalysis(true, matte, sampleCount, averageLuminance);
    }

    private static void SampleVisibleNeighbours(
        byte[] pixels,
        int width,
        int height,
        int stride,
        int x,
        int y,
        ref long luminanceSum,
        ref int sampleCount)
    {
        for (var offsetY = -1; offsetY <= 1 && sampleCount < MaxBrightnessSamples; offsetY++)
        {
            var neighbourY = y + offsetY;
            if (neighbourY < 0 || neighbourY >= height)
            {
                continue;
            }

            for (var offsetX = -1; offsetX <= 1 && sampleCount < MaxBrightnessSamples; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0)
                {
                    continue;
                }

                var neighbourX = x + offsetX;
                if (neighbourX < 0 || neighbourX >= width)
                {
                    continue;
                }

                var neighbourOffset = (neighbourY * stride) + (neighbourX * 4);
                var alpha = pixels[neighbourOffset + 3];
                if (alpha == 0)
                {
                    continue;
                }

                luminanceSum += CalculateUnpremultipliedLuminance(pixels, neighbourOffset, alpha);
                sampleCount++;
            }
        }
    }

    private static int CalculateUnpremultipliedLuminance(byte[] pixels, int offset, byte alpha)
    {
        var blue = Unpremultiply(pixels[offset], alpha);
        var green = Unpremultiply(pixels[offset + 1], alpha);
        var red = Unpremultiply(pixels[offset + 2], alpha);

        return (2126 * red + 7152 * green + 722 * blue + 5000) / 10000;
    }

    private static int Unpremultiply(byte component, byte alpha)
    {
        if (alpha == 0)
        {
            return 0;
        }

        return Math.Min(255, (component * 255 + (alpha / 2)) / alpha);
    }

    private static BitmapSource EnsurePbgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Pbgra32)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        converted.Freeze();
        return converted;
    }
}

internal sealed record CoverImageAlphaAnalysis(
    bool HasTransparency,
    CoverImageMatte SuggestedMatte,
    int BrightnessSampleCount,
    double? AverageEdgeLuminance);
