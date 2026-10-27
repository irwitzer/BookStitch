namespace CoverImageDrop.Core.Issues;

public sealed record CoverImageIssue(
    CoverImageIssueCode Code,
    CoverImageIssueSeverity Severity,
    IReadOnlyDictionary<string, object?>? Data = null,
    string? TechnicalCause = null,
    string? SuggestedAction = null);
