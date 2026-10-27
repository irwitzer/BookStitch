using System.Windows;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Core.Normalization;

internal sealed class CoverImageCropService
{
    public CoverImageCropRect ResolveSquareRect(
        BitmapSource source,
        CoverImageCropRect? requestedCrop)
    {
        ArgumentNullException.ThrowIfNull(source);

        var crop = (requestedCrop ?? CoverImageCropRect.CreateCenteredSquare(source.PixelWidth, source.PixelHeight))
            .ClampTo(source.PixelWidth, source.PixelHeight);
        if (!crop.IsSquare)
        {
            throw new ArgumentException("StandardCover-Crop muss quadratisch sein.", nameof(requestedCrop));
        }

        return crop;
    }

    public BitmapSource Crop(BitmapSource source, CoverImageCropRect crop)
    {
        ArgumentNullException.ThrowIfNull(source);

        var effectiveCrop = ResolveSquareRect(source, crop);
        var cropped = new CroppedBitmap(
            source,
            new Int32Rect(
                effectiveCrop.X,
                effectiveCrop.Y,
                effectiveCrop.Width,
                effectiveCrop.Height));
        cropped.Freeze();
        return cropped;
    }
}
