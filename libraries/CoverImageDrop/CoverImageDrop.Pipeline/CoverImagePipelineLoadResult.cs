using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Inspection;
using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Validation;

namespace CoverImageDrop.Pipeline;

public sealed record CoverImagePipelineLoadResult(
    CoverImagePipelineSession? Session,
    CoverImageInspectionResult? Inspection,
    StandardCoverValidationResult? Validation,
    IReadOnlyList<CoverImageIssue> Issues,
    string? DiagnosticsFolder)
{
    public bool IsSuccess => Session is not null;

    public DropSourceDetection? SourceDetection { get; init; }

    public IReadOnlyList<CoverImageCandidateAttempt> CandidateAttempts { get; init; } = [];

    public static CoverImagePipelineLoadResult Success(
        CoverImagePipelineSession session,
        IReadOnlyList<CoverImageIssue>? issues = null,
        string? diagnosticsFolder = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new CoverImagePipelineLoadResult(
            session,
            session.Inspection,
            session.Validation,
            issues ?? session.Issues,
            diagnosticsFolder);
    }

    public static CoverImagePipelineLoadResult Failed(
        CoverImageInspectionResult? inspection,
        StandardCoverValidationResult? validation,
        IReadOnlyList<CoverImageIssue> issues,
        string? diagnosticsFolder = null)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return new CoverImagePipelineLoadResult(null, inspection, validation, issues, diagnosticsFolder);
    }
}
