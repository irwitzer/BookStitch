using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Pipeline;

public static class CoverImagePreviewCropMapper
{
    public static CoverImagePreviewCropState CreateCentered(
        int orientedPixelWidth,
        int orientedPixelHeight,
        double viewportSize)
    {
        Validate(orientedPixelWidth, orientedPixelHeight, viewportSize);

        var crop = CoverImageCropRect.CreateCenteredSquare(
            orientedPixelWidth,
            orientedPixelHeight);
        return CreateFromCrop(
            orientedPixelWidth,
            orientedPixelHeight,
            crop,
            viewportSize);
    }

    public static CoverImagePreviewCropState CreateFromCrop(
        int orientedPixelWidth,
        int orientedPixelHeight,
        CoverImageCropRect cropRect,
        double viewportSize)
    {
        Validate(orientedPixelWidth, orientedPixelHeight, viewportSize);

        var expectedSize = Math.Min(
            orientedPixelWidth,
            orientedPixelHeight);
        var clamped = cropRect.ClampTo(
            orientedPixelWidth,
            orientedPixelHeight);
        if (clamped.Width != expectedSize ||
            clamped.Height != expectedSize ||
            clamped.Width != clamped.Height)
        {
            throw new ArgumentException(
                "Preview-Crop muss dem festen maximalen StandardCover-Quadrat entsprechen.",
                nameof(cropRect));
        }

        var scale = viewportSize / expectedSize;
        var displayWidth = orientedPixelWidth * scale;
        var displayHeight = orientedPixelHeight * scale;
        return CreateFromOffset(
            orientedPixelWidth,
            orientedPixelHeight,
            viewportSize,
            scale,
            displayWidth,
            displayHeight,
            -clamped.X * scale,
            -clamped.Y * scale);
    }

    public static CoverImagePreviewCropState MoveBy(
        CoverImagePreviewCropState state,
        double deltaX,
        double deltaY)
    {
        ArgumentNullException.ThrowIfNull(state);

        return CreateFromOffset(
            state.OrientedPixelWidth,
            state.OrientedPixelHeight,
            state.ViewportSize,
            state.Scale,
            state.DisplayWidth,
            state.DisplayHeight,
            state.OffsetX + deltaX,
            state.OffsetY + deltaY);
    }

    public static CoverImagePreviewCropState CreateFromOffset(
        int orientedPixelWidth,
        int orientedPixelHeight,
        double viewportSize,
        double offsetX,
        double offsetY)
    {
        Validate(orientedPixelWidth, orientedPixelHeight, viewportSize);

        var scale = Math.Max(
            viewportSize / orientedPixelWidth,
            viewportSize / orientedPixelHeight);
        return CreateFromOffset(
            orientedPixelWidth,
            orientedPixelHeight,
            viewportSize,
            scale,
            orientedPixelWidth * scale,
            orientedPixelHeight * scale,
            offsetX,
            offsetY);
    }

    private static CoverImagePreviewCropState CreateFromOffset(
        int orientedPixelWidth,
        int orientedPixelHeight,
        double viewportSize,
        double scale,
        double displayWidth,
        double displayHeight,
        double offsetX,
        double offsetY)
    {
        var clampedOffsetX = Math.Clamp(
            offsetX,
            Math.Min(0, viewportSize - displayWidth),
            0);
        var clampedOffsetY = Math.Clamp(
            offsetY,
            Math.Min(0, viewportSize - displayHeight),
            0);
        var cropSize = Math.Min(
            (int)Math.Round(viewportSize / scale),
            Math.Min(orientedPixelWidth, orientedPixelHeight));
        var cropX = (int)Math.Round(-clampedOffsetX / scale);
        var cropY = (int)Math.Round(-clampedOffsetY / scale);
        var cropRect = new CoverImageCropRect(
            cropX,
            cropY,
            cropSize,
            cropSize)
            .ClampTo(orientedPixelWidth, orientedPixelHeight);

        return new CoverImagePreviewCropState(
            orientedPixelWidth,
            orientedPixelHeight,
            viewportSize,
            scale,
            displayWidth,
            displayHeight,
            clampedOffsetX,
            clampedOffsetY,
            cropRect);
    }

    private static void Validate(
        int orientedPixelWidth,
        int orientedPixelHeight,
        double viewportSize)
    {
        if (orientedPixelWidth <= 0 ||
            orientedPixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(orientedPixelWidth),
                "Bildbreite und Bildhoehe muessen positiv sein.");
        }

        if (viewportSize <= 0 ||
            double.IsNaN(viewportSize) ||
            double.IsInfinity(viewportSize))
        {
            throw new ArgumentOutOfRangeException(
                nameof(viewportSize),
                "Viewport-Groesse muss positiv und endlich sein.");
        }
    }
}
