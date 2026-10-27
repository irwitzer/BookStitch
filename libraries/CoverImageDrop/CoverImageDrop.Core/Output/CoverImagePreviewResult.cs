using CoverImageDrop.Core.Issues;

namespace CoverImageDrop.Core.Output;

public sealed record CoverImagePreviewResult(
    byte[] Bytes,
    int Width,
    int Height,
    IReadOnlyList<CoverImageIssue> Issues)
{
    public string MimeType => "image/png";
    public string FileExtension => ".png";
    public long ByteLength => Bytes.LongLength;
}
