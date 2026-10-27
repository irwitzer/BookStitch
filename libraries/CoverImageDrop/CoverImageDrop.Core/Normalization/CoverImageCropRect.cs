namespace CoverImageDrop.Core.Normalization;

public sealed record CoverImageCropRect(int X, int Y, int Width, int Height)
{
    public bool IsSquare => Width == Height;

    public static CoverImageCropRect CreateCenteredSquare(int imageWidth, int imageHeight)
    {
        ValidateImageSize(imageWidth, imageHeight);

        var size = Math.Min(imageWidth, imageHeight);
        return new CoverImageCropRect(
            (imageWidth - size) / 2,
            (imageHeight - size) / 2,
            size,
            size);
    }

    public CoverImageCropRect ClampTo(int imageWidth, int imageHeight)
    {
        ValidateImageSize(imageWidth, imageHeight);

        if (Width <= 0 || Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "Crop-Breite und Crop-Hoehe muessen positiv sein.");
        }

        var width = Math.Min(Width, imageWidth);
        var height = Math.Min(Height, imageHeight);
        var x = Math.Clamp(X, 0, imageWidth - width);
        var y = Math.Clamp(Y, 0, imageHeight - height);
        return new CoverImageCropRect(x, y, width, height);
    }

    private static void ValidateImageSize(int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Bildbreite und Bildhoehe muessen positiv sein.");
        }
    }
}
