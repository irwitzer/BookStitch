using CoverImageDrop.Core.Issues;

namespace CoverImageDrop.Core.Naming;

public sealed record CoverImageNamingResult(
    string FileName,
    string BaseName,
    string Extension,
    IReadOnlyList<CoverImageIssue> Issues)
{
    public bool WasAdjusted => Issues.Any(issue => issue.Code == CoverImageIssueCode.FileNameAdjusted);
}
