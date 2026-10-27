using CoverImageDrop.Core.Issues;
using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Pipeline;

public sealed record CoverImagePipelineFinalizeResult(
    bool IsSuccess,
    CoverImageOutputResult? Output,
    IReadOnlyList<CoverImageIssue> Issues,
    string? DiagnosticsFolder)
{
    public static CoverImagePipelineFinalizeResult Success(
        CoverImageOutputResult output,
        string? diagnosticsFolder = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new CoverImagePipelineFinalizeResult(true, output, output.Issues, diagnosticsFolder);
    }

    public static CoverImagePipelineFinalizeResult Failed(
        IReadOnlyList<CoverImageIssue> issues,
        string? diagnosticsFolder = null)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return new CoverImagePipelineFinalizeResult(false, null, issues, diagnosticsFolder);
    }
}
