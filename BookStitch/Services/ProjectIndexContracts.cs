namespace BookStitch.Services;

public sealed record ProjectCleanupResult(int MatchedCount, int DeletedCount, IReadOnlyList<string> Failures)
{
    public bool HasFailures => Failures.Count > 0;
}

public sealed record ProjectDeletionResult(bool Deleted, string? ErrorMessage);


public sealed record DamagedProjectInfo(
    string ProjectFolder,
    string ProjectType,
    string DisplayName,
    string RequiredManifestFileName,
    string Reason);
