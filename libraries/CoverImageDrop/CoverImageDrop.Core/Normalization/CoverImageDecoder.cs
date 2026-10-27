using CoverImageDrop.Core.Imaging;
using CoverImageDrop.Core.Inspection;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Core.Normalization;

internal sealed class CoverImageDecoder
{
    public CoverImageDecodedImage Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (ImageFormatSignatureDetector.Detect(bytes) == DetectedImageFormat.WebP)
        {
            var image = BundledWebPDecoder.Read(bytes, decode: true);
            return new CoverImageDecodedImage(BundledWebPDecoder.CreateFrame(image), image.Frames,
                BundledWebPDecoder.CreateColorContext(image.Icc), image.Icc.Length > 0);
        }
        using var input = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnDemand);
        var frame = decoder.Frames.FirstOrDefault()
            ?? throw new InvalidOperationException("Bild enthaelt keinen lesbaren Frame.");
        var colorContext = TryGetSourceColorContext(frame, decoder);
        // Materialize only the selected frame while the stream is alive. OnLoad on
        // the whole decoder can retain every frame of a multi-frame source.
        var cached = new CachedBitmap(frame, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        cached.Freeze();
        var metadata = (frame.Metadata as BitmapMetadata)?.Clone();
        var selected = BitmapFrame.Create(cached, null, metadata, null);
        selected.Freeze();
        return new CoverImageDecodedImage(selected, decoder.Frames.Count, colorContext);
    }

    private static ColorContext? TryGetSourceColorContext(BitmapFrame frame, BitmapDecoder decoder)
    {
        try
        {
            var frameContext = frame.ColorContexts?.FirstOrDefault();
            if (frameContext is not null)
            {
                return frameContext;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
        }

        try
        {
            return decoder.ColorContexts?.FirstOrDefault();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}

internal sealed record CoverImageDecodedImage(
    BitmapFrame FirstFrame,
    int FrameCount,
    ColorContext? SourceColorContext,
    bool SourceProfilePresent = false);
