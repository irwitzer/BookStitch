using CoverImageDrop.Core.Issues;

namespace CoverImageDrop.Core.Validation;

public sealed record StandardCoverValidationResult(
    int UsableSquarePixels,
    long TotalPixels,
    IReadOnlyList<CoverImageIssue> Issues)
{
    public bool IsValid => Issues.All(issue => issue.Severity != CoverImageIssueSeverity.Error);
}
