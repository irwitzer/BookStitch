using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Pipeline;

public sealed record CoverImagePreviewCropState(
    int OrientedPixelWidth,
    int OrientedPixelHeight,
    double ViewportSize,
    double Scale,
    double DisplayWidth,
    double DisplayHeight,
    double OffsetX,
    double OffsetY,
    CoverImageCropRect CropRect);
