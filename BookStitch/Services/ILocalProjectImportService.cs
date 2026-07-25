namespace BookStitch.Services;

public interface ILocalProjectImportService
{
    Task<LocalProjectImportResult> CopySourcesAsync(
        string sourceFolder,
        IReadOnlyCollection<string> sourceFiles,
        string projectFolder,
        IProgress<LocalProjectCopyProgress>? progress,
        IProgress<LocalProjectCopiedFile>? copiedFileProgress,
        CancellationToken cancellationToken);
}
