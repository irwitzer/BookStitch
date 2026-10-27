using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Pipeline;

public sealed record CoverImageCropState(
    CoverImageCropRect Rect,
    int OrientedPixelWidth,
    int OrientedPixelHeight,
    int TargetSizePixels)
{
    public CoverImageCropState Clamp()
    {
        ValidateImageSize();

        var size = Math.Min(OrientedPixelWidth, OrientedPixelHeight);
        var x = Math.Clamp(Rect.X, 0, OrientedPixelWidth - size);
        var y = Math.Clamp(Rect.Y, 0, OrientedPixelHeight - size);
        return this with { Rect = new CoverImageCropRect(x, y, size, size) };
    }

    public CoverImageCropState MoveTo(int x, int y)
    {
        return (this with { Rect = Rect with { X = x, Y = y } }).Clamp();
    }

    public static CoverImageCropState CreateCentered(int orientedPixelWidth, int orientedPixelHeight, int targetSizePixels)
    {
        return new CoverImageCropState(
            CoverImageCropRect.CreateCenteredSquare(orientedPixelWidth, orientedPixelHeight),
            orientedPixelWidth,
            orientedPixelHeight,
            targetSizePixels);
    }

    private void ValidateImageSize()
    {
        if (OrientedPixelWidth <= 0 || OrientedPixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OrientedPixelWidth), "Bildbreite und Bildhoehe muessen positiv sein.");
        }
    }
}
