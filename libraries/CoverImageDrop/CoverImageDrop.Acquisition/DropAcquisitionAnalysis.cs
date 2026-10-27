using System.Windows.Media.Imaging;

namespace CoverImageDrop.Acquisition;

public enum DropImageCandidateKind
{
    LocalFile,
    EncodedDataUrl,
    DirectUrl,
    HtmlDerivedUrl,
    BitmapFallback
}

public enum DropSourceKind
{
    WindowsExplorer,
    Firefox,
    Chromium,
    Unknown
}

public enum DropSourceConfidence
{
    Low,
    Medium,
    High
}

public sealed record DropSourceDetection(
    DropSourceKind Kind,
    DropSourceConfidence Confidence,
    IReadOnlyList<string> Reasons)
{
    public string DisplayName => Kind switch
    {
        DropSourceKind.WindowsExplorer => "Explorer oder lokaler Dateidrop",
        DropSourceKind.Firefox => "Firefox",
        DropSourceKind.Chromium => "Chromium-basiert",
        _ => "Unbekannt"
    };
}

public sealed record DropImageCandidate(
    DropImageCandidateKind Kind,
    string Source,
    int Priority,
    int? Width,
    DropImageData? SourceData,
    string? Url)
{
    internal DropDeferredPayload? DeferredPayload { get; init; }
}

internal abstract record DropDeferredPayload;

internal sealed record DropDataUrlPayloadReference(
    string ArtifactName,
    int PayloadOffset,
    int PayloadLength)
    : DropDeferredPayload;

internal sealed record DropBitmapPayloadReference(BitmapSource Bitmap)
    : DropDeferredPayload;

public sealed record DropImageVariantReference(
    string Url,
    int PixelWidth,
    int PixelHeight)
{
    private const double MaxAspectRatioFactor = 1.20;

    public bool IsGeometryCompatible(int pixelWidth, int pixelHeight)
    {
        if (PixelWidth <= 0 || PixelHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
        {
            return false;
        }

        var referenceOrientation = Math.Sign(PixelWidth - PixelHeight);
        var candidateOrientation = Math.Sign(pixelWidth - pixelHeight);
        if (referenceOrientation != candidateOrientation)
        {
            return false;
        }

        var referenceAspectRatio = (double)PixelWidth / PixelHeight;
        var candidateAspectRatio = (double)pixelWidth / pixelHeight;
        var factor = candidateAspectRatio / referenceAspectRatio;
        if (factor < 1d)
        {
            factor = 1d / factor;
        }

        return factor <= MaxAspectRatioFactor;
    }
}

public sealed record DropAcquisitionAnalysis(
    DropSourceDetection SourceDetection,
    IReadOnlyList<string> OfferedFormats,
    IReadOnlyDictionary<string, string> ProcessingTextArtifacts,
    IReadOnlyDictionary<string, string> DiagnosticTextArtifacts,
    IReadOnlyList<DropImageCandidate> Candidates,
    IReadOnlyList<DropUrlCandidate> UrlCandidates);

public sealed record DropCandidateMaterializationResult(
    DropImageCandidate Candidate,
    DropReadResult ReadResult,
    string? FailureReason)
{
    public bool IsSuccess => ReadResult.ImageData is not null;
    public DropImageData? ImageData => ReadResult.ImageData;
}
