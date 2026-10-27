using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Pipeline.Persistence;

namespace CoverImageDrop.Pipeline.PassThrough;

public enum CoverImagePassThroughFormatSource
{
    Unknown,
    Signature,
    ContentType,
    FileExtension,
    BitmapEncoding
}

public enum CoverImagePassThroughFormatConfidence
{
    Unknown,
    Verified,
    Unverified
}

public sealed record CoverImagePassThroughResult(
    bool IsSuccess,
    DropImageData? Source,
    CoverImagePassThroughMaterializationKind MaterializationKind,
    DetectedImageFormat Format,
    int? PixelWidth,
    int? PixelHeight,
    string? FileExtension,
    string? SuggestedFileName,
    CoverImagePersistenceResult? Persistence,
    IReadOnlyList<CoverImageIssue> Issues)
{
    public bool WasPersisted => Persistence?.IsSuccess == true;

    public CoverImagePassThroughFormatSource FormatSource { get; init; }

    public CoverImagePassThroughFormatConfidence FormatConfidence { get; init; }

    public string? DiagnosticsFolder { get; init; }

    public string? DebugReport { get; init; }
}
