using CoverImageDrop.Core.Issues;

namespace CoverImageDrop.Core.Inspection;

public sealed record CoverImageInspectionResult(
    string SourceName,
    DetectedImageFormat SignatureFormat,
    string DecoderCodec,
    int PixelWidth,
    int PixelHeight,
    int OrientedPixelWidth,
    int OrientedPixelHeight,
    int FrameCount,
    bool MayContainTransparency,
    IReadOnlyList<CoverImageIssue> Issues)
{
    public bool IsSuccess => Issues.All(issue => issue.Severity != CoverImageIssueSeverity.Error);

    public int ColorProfileCount { get; init; }

    public bool HasEmbeddedColorProfile => ColorProfileCount > 0;
}
