using System.IO;

namespace BookStitch.Services;

public sealed record LocalProjectCopyProgress(
    int CompletedFiles,
    int TotalFiles,
    string CurrentFileName);

public sealed record LocalProjectCopiedFile(
    int CompletedFiles,
    int TotalFiles,
    string SourceFile,
    string TargetFile,
    bool WasReused,
    SourceFileCopyIdentity? SourceIdentity = null);

public sealed record LocalProjectImportResult(
    string SourceFolder,
    string ProjectFolder,
    string OriginalsFolder,
    int TotalFiles,
    int CompletedFiles,
    bool WasCanceled = false);

public sealed class LocalProjectImportService : ILocalProjectImportService
{
    public const string OriginalsFolderName = ProjectFolderLayout.OriginalsFolderName;
    private const int CopyBufferSize = 1024 * 1024;
    private readonly ProjectFolderNameService _projectFolderNameService = new();
    private readonly SourceCopyIdentityStore _sourceCopyIdentityStore = new();

    public string CreateProjectFolder(
        string localProjectsFolder,
        string sourceFolder,
        DateTime? localNow = null,
        int targetSampleRateHz = 44100, bool allowAacReuse = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localProjectsFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);

        var sourceName = new DirectoryInfo(Path.GetFullPath(sourceFolder)).Name;
        var projectFolder = _projectFolderNameService.CreateUniqueProjectFolder(
            localProjectsFolder,
            sourceName,
            "Ordnerprojekt",
            localNow);
        new FolderProjectManifestService().Create(projectFolder, Path.GetFullPath(sourceFolder), targetSampleRateHz, allowAacReuse);
        return projectFolder;
    }

    public async Task<LocalProjectImportResult> CopySourcesAsync(
        string sourceFolder,
        IReadOnlyCollection<string> sourceFiles,
        string projectFolder,
        IProgress<LocalProjectCopyProgress>? progress,
        IProgress<LocalProjectCopiedFile>? copiedFileProgress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        var sourceRoot = Path.GetFullPath(sourceFolder);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Der Quellordner wurde nicht gefunden: {sourceRoot}");

        var orderedFiles = sourceFiles
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => Path.GetRelativePath(sourceRoot, file), StringComparer.OrdinalIgnoreCase)
            .ToList();

        ValidateSourceFiles(sourceRoot, orderedFiles);

        var fullProjectFolder = Path.GetFullPath(projectFolder);
        ProjectFolderLayout.EnsureProjectFolders(fullProjectFolder);
        var originalsFolder = ProjectFolderLayout.GetOriginalsFolder(fullProjectFolder);

        var completedFiles = 0;

        try
        {
            foreach (var sourceFile in orderedFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new LocalProjectImportResult(
                        sourceRoot,
                        fullProjectFolder,
                        originalsFolder,
                        orderedFiles.Count,
                        completedFiles,
                        WasCanceled: true);
                }

                var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
                var targetFile = Path.Combine(originalsFolder, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);

                SourceFileCopyService.CleanupTemporaryCopy(targetFile);
                string? contentSha256 = null;
                var wasReused = _sourceCopyIdentityStore.CanReuseExistingCopy(
                    fullProjectFolder,
                    sourceFile,
                    targetFile, cancellationToken);
                if (!wasReused)
                {
                    contentSha256 = await SourceFileCopyService.CopyAtomicallyAsync(sourceFile, targetFile, cancellationToken);
                }

                var sourceIdentity = _sourceCopyIdentityStore.RecordCopiedFile(
                    fullProjectFolder,
                    sourceFile,
                    targetFile,
                    contentSha256: contentSha256, cancellationToken: cancellationToken);

                completedFiles++;
                progress?.Report(new LocalProjectCopyProgress(
                    completedFiles,
                    orderedFiles.Count,
                    Path.GetFileName(sourceFile)));
                copiedFileProgress?.Report(new LocalProjectCopiedFile(
                    completedFiles,
                    orderedFiles.Count,
                    sourceFile,
                    targetFile,
                    wasReused,
                    sourceIdentity));
            }

            return new LocalProjectImportResult(
                sourceRoot,
                fullProjectFolder,
                originalsFolder,
                orderedFiles.Count,
                completedFiles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new LocalProjectImportResult(sourceRoot, fullProjectFolder, originalsFolder,
                orderedFiles.Count, completedFiles, WasCanceled: true);
        }
    }

    private static void ValidateSourceFiles(string sourceRoot, IReadOnlyCollection<string> sourceFiles)
    {
        foreach (var sourceFile in sourceFiles)
        {
            if (!File.Exists(sourceFile))
                throw new FileNotFoundException(LocalizationText.Get("Sources.LocalFileMissing"), sourceFile);

            var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
            if (Path.IsPathRooted(relativePath) ||
                relativePath.Equals("..", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Die Quelldatei liegt außerhalb des ausgewählten Quellordners: {sourceFile}");
            }
        }
    }

}
