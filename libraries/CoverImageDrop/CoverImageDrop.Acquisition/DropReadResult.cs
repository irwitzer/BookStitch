namespace CoverImageDrop.Acquisition;

public sealed record DropReadResult(
    DropImageData? ImageData,
    string AcquisitionMethod,
    string? InferredSource,
    IReadOnlyList<string> OfferedFormats,
    IReadOnlyDictionary<string, string> TextArtifacts,
    IReadOnlyList<DropUrlCandidate> UrlCandidates,
    DropDownloadInfo? DownloadInfo,
    IReadOnlyList<DropDownloadAttempt> DownloadAttempts)
{
    public DropSourceDetection SourceDetection { get; init; } = new(
        DropSourceKind.Unknown,
        DropSourceConfidence.Low,
        []);

    public IReadOnlyList<DropImageCandidate> Candidates { get; init; } = [];

    public IReadOnlyDictionary<string, string> DiagnosticTextArtifacts { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record DropUrlCandidate(
    string Url,
    string Source,
    int Priority,
    int? Width,
    DropImageCandidateKind Kind = DropImageCandidateKind.DirectUrl);

public sealed record DropDownloadInfo(
    string RequestedUrl,
    string? FinalUrl,
    string? ContentType,
    long? ContentLength,
    bool LooksLikeHtml);

public sealed record DropDownloadAttempt(
    string Url,
    string? FinalUrl,
    string? ContentType,
    long? ContentLength,
    bool LooksLikeHtml,
    bool Accepted,
    string? Error);
