using CoverImageDrop.Core.Issues;

namespace CoverImageDrop.Pipeline.Persistence;

public sealed record CoverImagePersistenceResult(
    bool IsSuccess,
    string? FileName,
    string? FilePath,
    long BytesWritten,
    bool WasRenamedForCollision,
    IReadOnlyList<CoverImageIssue> Issues)
{
    public static CoverImagePersistenceResult Success(
        string filePath,
        long bytesWritten,
        bool wasRenamedForCollision,
        IReadOnlyList<CoverImageIssue> issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(issues);

        return new CoverImagePersistenceResult(
            true,
            Path.GetFileName(filePath),
            filePath,
            bytesWritten,
            wasRenamedForCollision,
            issues);
    }

    public static CoverImagePersistenceResult Failed(
        string? filePath,
        IReadOnlyList<CoverImageIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        return new CoverImagePersistenceResult(
            false,
            string.IsNullOrWhiteSpace(filePath) ? null : Path.GetFileName(filePath),
            filePath,
            0,
            false,
            issues);
    }
}