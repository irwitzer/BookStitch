using CoverImageDrop.Core.Issues;

namespace CoverImageDrop.Core.Output;

public sealed record CoverImageOutputResult(
    byte[] Bytes,
    int Width,
    int Height,
    CoverOutputFormat Format,
    string MimeType,
    string FileExtension,
    IReadOnlyList<CoverImageIssue> Issues)
{
    public long ByteLength => Bytes.LongLength;
    public int ResolvedTargetSizePixels { get; init; }
    public bool WasUpscaled { get; init; }
    public int? JpegQuality { get; init; }
    public CoverImageMatte? JpegMatte { get; init; }
}
