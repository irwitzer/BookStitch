using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Normalization;
using CoverImageDrop.Core.Validation;

namespace CoverImageDrop.Diagnostics;

public sealed record CoverImageDiagnosticContext
{
    public string Profile { get; init; } = "Unknown";

    public string? ManualSourceOverride { get; init; }

    public DropSourceDetection? SourceDetection { get; init; }

    public IReadOnlyList<CoverImageDiagnosticCandidateAttempt> CandidateAttempts { get; init; } = [];

    public StandardCoverValidationResult? StandardCoverValidation { get; init; }

    public string? TargetSizeMode { get; init; }

    public string? RequestedOutputFormat { get; init; }

    public string? TransparencyMode { get; init; }

    public int? ResolvedTargetSizePixels { get; init; }

    public bool? RequiresUpscaling { get; init; }

    public CoverImageCropRect? Crop { get; init; }

    public string? PassThroughMaterializationKind { get; init; }

    public string? PassThroughFormat { get; init; }

    public int? PassThroughPixelWidth { get; init; }

    public int? PassThroughPixelHeight { get; init; }

    public string? PassThroughFileExtension { get; init; }

    public string? NamingStrategy { get; init; }

    public string? FinalFileName { get; init; }

    public string? PersistencePath { get; init; }

    public IReadOnlyList<CoverImageIssue> Issues { get; init; } = [];

    public string? TechnicalException { get; init; }
}

public sealed record CoverImageDiagnosticCandidateAttempt(
    int CandidateIndex,
    DropImageCandidateKind Kind,
    string Source,
    int Priority,
    int? WidthHint,
    string? Url,
    bool Materialized,
    bool Accepted,
    CoverImageInspectionResult? Inspection,
    StandardCoverValidationResult? Validation,
    IReadOnlyList<CoverImageIssue> Issues,
    IReadOnlyList<DropDownloadAttempt> DownloadAttempts,
    string? TechnicalCause);
