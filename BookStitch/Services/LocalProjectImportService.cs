using System.Globalization;
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
    bool WasReused);

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

    public string CreateProjectFolder(
        string localProjectsFolder,
        string sourceFolder,
        DateTime? localNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localProjectsFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFolder);

        Directory.CreateDirectory(localProjectsFolder);

        var sourceName = new DirectoryInfo(Path.GetFullPath(sourceFolder)).Name;
        var safeName = MakeSafeFileName(sourceName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "Ordnerprojekt";

        var timestamp = (localNow ?? DateTime.Now)
            .ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        return Path.Combine(
            Path.GetFullPath(localProjectsFolder),
            $"{safeName}_{timestamp}");
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

            var wasReused = IsCompleteExistingCopy(sourceFile, targetFile);
            if (!wasReused)
            {
                try
                {
                    await CopyFileAtomicallyAsync(sourceFile, targetFile, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new LocalProjectImportResult(
                        sourceRoot,
                        fullProjectFolder,
                        originalsFolder,
                        orderedFiles.Count,
                        completedFiles,
                        WasCanceled: true);
                }
            }

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
                wasReused));
        }

        return new LocalProjectImportResult(
            sourceRoot,
            fullProjectFolder,
            originalsFolder,
            orderedFiles.Count,
            completedFiles);
    }

    private static void ValidateSourceFiles(string sourceRoot, IReadOnlyCollection<string> sourceFiles)
    {
        foreach (var sourceFile in sourceFiles)
        {
            if (!File.Exists(sourceFile))
                throw new FileNotFoundException("Eine lokale Quelldatei wurde nicht gefunden.", sourceFile);

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

    private static bool IsCompleteExistingCopy(string sourceFile, string targetFile)
    {
        if (!File.Exists(targetFile))
            return false;

        try
        {
            return new FileInfo(sourceFile).Length == new FileInfo(targetFile).Length;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task CopyFileAtomicallyAsync(
        string sourceFile,
        string targetFile,
        CancellationToken cancellationToken)
    {
        var copyingPath = targetFile + ".copying";

        try
        {
            if (File.Exists(copyingPath))
                File.Delete(copyingPath);

            await using (var sourceStream = new FileStream(
                             sourceFile,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             CopyBufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var targetStream = new FileStream(
                             copyingPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             CopyBufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await sourceStream.CopyToAsync(targetStream, CopyBufferSize, cancellationToken);
                await targetStream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(copyingPath, targetFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(copyingPath))
                File.Delete(copyingPath);
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        return string.Join("_", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
