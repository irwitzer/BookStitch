using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Validation;

namespace CoverImageDrop.Pipeline;

public sealed record CoverImageCandidateAttempt(
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
