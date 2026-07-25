using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace BookStitch.Services;

public sealed record DiscSourceAnalysis(
    string SourceFolder,
    IReadOnlyList<string> SupportedAudioFiles,
    bool HasCdaFiles,
    bool IsCdDrive)
{
    public bool IsSupportedDataDisc => SupportedAudioFiles.Count > 0;
    public bool IsProbablyAudioCd => !IsSupportedDataDisc && (HasCdaFiles || IsCdDrive);
}


public sealed record DiscSourceQuickSample(
    string RelativePath,
    long FileSize);

public sealed record DiscSourceQuickIdentity(
    string VolumeLabel,
    IReadOnlyList<DiscSourceQuickSample> Samples);

public enum DiscSourceQuickCheckStatus
{
    Match,
    Unavailable,
    Mismatch
}

public sealed record DiscSourceQuickCheckResult(
    DiscSourceQuickCheckStatus Status,
    string Reason)
{
    public bool IsMatch => Status == DiscSourceQuickCheckStatus.Match;
}

public sealed record DiscCopyProgress(
    int CopiedFiles,
    int TotalFiles,
    string CurrentFile);

public sealed record DiscCopiedFile(
    int DiscNumber,
    int TotalDiscs,
    int CopiedFiles,
    int TotalFiles,
    string SourceFile,
    string ImportedFile);

public sealed record DiscImportResult(
    string SourceFolder,
    string ImportedFolder,
    string ProjectFolder,
    int DiscNumber,
    int TotalDiscs,
    int CopiedFiles,
    bool WasCanceled = false);

public sealed class Mp3DiscImportService
{
    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".aac",
        ".m4a",
        ".m4b",
        ".wav",
        ".flac"
    };

    public DiscSourceAnalysis AnalyzeSource(string sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            return new DiscSourceAnalysis(
                sourceFolder,
                Array.Empty<string>(),
                HasCdaFiles: false,
                IsCdDrive: false);
        }

        var files = EnumerateFilesSafe(sourceFolder).ToList();
        var audioFiles = files
            .Where(file => SupportedAudioExtensions.Contains(Path.GetExtension(file)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hasCdaFiles = files.Any(file => Path.GetExtension(file).Equals(".cda", StringComparison.OrdinalIgnoreCase));

        return new DiscSourceAnalysis(
            sourceFolder,
            audioFiles,
            hasCdaFiles,
            IsCdDrive(sourceFolder));
    }

    public string CreateDiscProjectFolder(string sourceFolder, string mp3DiscProjectsFolder)
    {
        return CreateProjectFolder(sourceFolder, mp3DiscProjectsFolder);
    }

    public DiscSourceQuickIdentity CreateQuickIdentity(string sourceFolder, DiscSourceAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var orderedFiles = analysis.SupportedAudioFiles
            .OrderBy(file => GetRelativePathSafe(sourceFolder, file), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sampleIndexes = GetQuickSampleIndexes(orderedFiles.Count);
        var samples = sampleIndexes
            .Select(index => new DiscSourceQuickSample(
                GetRelativePathSafe(sourceFolder, orderedFiles[index]),
                GetFileSizeSafe(orderedFiles[index])))
            .ToList();

        return new DiscSourceQuickIdentity(
            GetVolumeLabelSafe(sourceFolder),
            samples);
    }

    public DiscSourceQuickCheckResult CheckQuickIdentity(
        string sourceFolder,
        DiscSourceQuickIdentity expectedIdentity)
    {
        ArgumentNullException.ThrowIfNull(expectedIdentity);

        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            return new DiscSourceQuickCheckResult(
                DiscSourceQuickCheckStatus.Unavailable,
                "Die Quelle ist nicht verfügbar.");
        }

        var isCdDrive = IsCdDrive(sourceFolder);
        if (isCdDrive && !IsCdDriveReady(sourceFolder))
        {
            return new DiscSourceQuickCheckResult(
                DiscSourceQuickCheckStatus.Unavailable,
                "Im ausgewählten Laufwerk ist kein lesbarer Datenträger eingelegt.");
        }

        if (!Directory.Exists(sourceFolder))
        {
            return new DiscSourceQuickCheckResult(
                DiscSourceQuickCheckStatus.Unavailable,
                "Die Quelle ist nicht verfügbar.");
        }

        var currentVolumeLabel = GetVolumeLabelSafe(sourceFolder);
        if (!string.IsNullOrWhiteSpace(expectedIdentity.VolumeLabel) &&
            !string.IsNullOrWhiteSpace(currentVolumeLabel) &&
            !string.Equals(expectedIdentity.VolumeLabel, currentVolumeLabel, StringComparison.OrdinalIgnoreCase))
        {
            return new DiscSourceQuickCheckResult(
                DiscSourceQuickCheckStatus.Mismatch,
                "Die Datenträgerbezeichnung hat sich geändert.");
        }

        foreach (var sample in expectedIdentity.Samples)
        {
            var samplePath = Path.Combine(sourceFolder, sample.RelativePath);
            if (!File.Exists(samplePath))
            {
                return new DiscSourceQuickCheckResult(
                    DiscSourceQuickCheckStatus.Mismatch,
                    $"Die Referenzdatei '{sample.RelativePath}' wurde nicht gefunden.");
            }

            if (GetFileSizeSafe(samplePath) != sample.FileSize)
            {
                return new DiscSourceQuickCheckResult(
                    DiscSourceQuickCheckStatus.Mismatch,
                    $"Die Referenzdatei '{sample.RelativePath}' hat eine andere Größe.");
            }
        }

        return new DiscSourceQuickCheckResult(
            DiscSourceQuickCheckStatus.Match,
            string.Empty);
    }

    public string CreateDiscStructureSignature(string sourceFolder, DiscSourceAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var builder = new StringBuilder();
        builder.AppendLine(GetVolumeLabelSafe(sourceFolder).ToUpperInvariant());

        foreach (var file in analysis.SupportedAudioFiles
                     .OrderBy(file => GetRelativePathSafe(sourceFolder, file), StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                GetRelativePathSafe(sourceFolder, file)
                    .Replace('\\', '/')
                    .ToUpperInvariant());
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public string CreateDiscSignature(string sourceFolder, DiscSourceAnalysis analysis)
    {
        var builder = new StringBuilder();

        foreach (var file in analysis.SupportedAudioFiles.OrderBy(file => GetRelativePathSafe(sourceFolder, file), StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = GetRelativePathSafe(sourceFolder, file).Replace('\\', '/');
            var fileSize = GetFileSizeSafe(file);
            var lastWriteTicks = GetLastWriteTicksSafe(file);
            builder.Append(relativePath.ToUpperInvariant());
            builder.Append('|');
            builder.Append(fileSize.ToString(CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(lastWriteTicks.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public Task<DiscImportResult> CopyFirstDiscAsync(
        string sourceFolder,
        int totalDiscs,
        string mp3DiscProjectsFolder,
        IProgress<DiscCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        return CopyFirstDiscAsync(sourceFolder, totalDiscs, mp3DiscProjectsFolder, progress, copiedFileProgress: null, cancellationToken);
    }

    public Task<DiscImportResult> CopyFirstDiscAsync(
        string sourceFolder,
        int totalDiscs,
        string mp3DiscProjectsFolder,
        IProgress<DiscCopyProgress>? progress,
        IProgress<DiscCopiedFile>? copiedFileProgress,
        CancellationToken cancellationToken)
    {
        var projectFolder = CreateDiscProjectFolder(sourceFolder, mp3DiscProjectsFolder);
        return CopyDiscAsync(sourceFolder, projectFolder, discNumber: 1, totalDiscs, progress, copiedFileProgress, cancellationToken);
    }

    public Task<DiscImportResult> CopyDiscAsync(
        string sourceFolder,
        string projectFolder,
        int discNumber,
        int totalDiscs,
        IProgress<DiscCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        return CopyDiscAsync(sourceFolder, projectFolder, discNumber, totalDiscs, progress, copiedFileProgress: null, cancellationToken);
    }

    public Task<DiscImportResult> CopyDiscAsync(
        string sourceFolder,
        string projectFolder,
        int discNumber,
        int totalDiscs,
        IProgress<DiscCopyProgress>? progress,
        IProgress<DiscCopiedFile>? copiedFileProgress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var analysis = AnalyzeSource(sourceFolder);
            if (!analysis.IsSupportedDataDisc)
                throw new InvalidOperationException("Auf der ausgewählten Quelle wurden keine unterstützten Audiodateien gefunden.");

            ProjectFolderLayout.EnsureProjectFolders(projectFolder);

            var discFolder = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, discNumber);
            Directory.CreateDirectory(discFolder);

            var files = analysis.SupportedAudioFiles.ToList();
            var copiedFiles = 0;

            for (var index = 0; index < files.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new DiscImportResult(
                        sourceFolder,
                        discFolder,
                        projectFolder,
                        discNumber,
                        totalDiscs,
                        copiedFiles,
                        WasCanceled: true);
                }

                var sourceFile = files[index];
                var relativePath = GetRelativePathSafe(sourceFolder, sourceFile);
                var targetFile = Path.Combine(discFolder, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetFile);

                if (!string.IsNullOrWhiteSpace(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);

                var copiedNow = !IsCompleteExistingCopy(sourceFile, targetFile);
                if (copiedNow)
                {
                    var copyingPath = targetFile + ".copying";
                    try
                    {
                        if (File.Exists(copyingPath))
                            File.Delete(copyingPath);

                        File.Copy(sourceFile, copyingPath, overwrite: true);
                        File.Move(copyingPath, targetFile, overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(copyingPath))
                            File.Delete(copyingPath);
                    }
                }

                copiedFiles = index + 1;

                progress?.Report(new DiscCopyProgress(
                    copiedFiles,
                    files.Count,
                    Path.GetFileName(sourceFile)));

                // Live conversion must only be queued for files that were actually copied.
                // Existing complete project originals already have their persisted conversion state;
                // reporting them here would generate a second hash-based output in a working copy.
                if (copiedNow)
                {
                    copiedFileProgress?.Report(new DiscCopiedFile(
                        discNumber,
                        totalDiscs,
                        copiedFiles,
                        files.Count,
                        sourceFile,
                        targetFile));
                }
            }

            return new DiscImportResult(
                sourceFolder,
                discFolder,
                projectFolder,
                discNumber,
                totalDiscs,
                copiedFiles);
        });
    }

    private static bool IsCompleteExistingCopy(string sourceFile, string targetFile)
    {
        if (!File.Exists(targetFile))
            return false;

        try
        {
            return new FileInfo(sourceFile).Length == new FileInfo(targetFile).Length;
        }
        catch
        {
            return false;
        }
    }

    private static void ClearReadOnlyAttributes(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }
            catch
            {
                // Wenn einzelne Attribute nicht geändert werden können, versucht das Löschen danach trotzdem weiter.
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(directory, File.GetAttributes(directory) & ~FileAttributes.ReadOnly);
            }
            catch
            {
                // Attributbereinigung ist nur Best Effort.
            }
        }

        try
        {
            File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);
        }
        catch
        {
            // Attributbereinigung ist nur Best Effort.
        }
    }

    private static long GetFileSizeSafe(string filePath)
    {
        try
        {
            return new FileInfo(filePath).Length;
        }
        catch
        {
            return -1;
        }
    }

    private static long GetLastWriteTicksSafe(string filePath)
    {
        try
        {
            return new FileInfo(filePath).LastWriteTimeUtc.Ticks;
        }
        catch
        {
            return 0;
        }
    }

    private static IReadOnlyList<int> GetQuickSampleIndexes(int fileCount)
    {
        if (fileCount <= 0)
            return Array.Empty<int>();

        if (fileCount == 1)
            return [0];

        if (fileCount == 2)
            return [0, 1];

        return [0, fileCount / 2, fileCount - 1];
    }

    private static bool IsCdDriveReady(string sourceFolder)
    {
        try
        {
            var root = Path.GetPathRoot(sourceFolder);
            return !string.IsNullOrWhiteSpace(root) && new DriveInfo(root).IsReady;
        }
        catch
        {
            return false;
        }
    }

    private static string GetVolumeLabelSafe(string sourceFolder)
    {
        try
        {
            var root = Path.GetPathRoot(sourceFolder);
            if (string.IsNullOrWhiteSpace(root))
                return string.Empty;

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.VolumeLabel?.Trim() ?? string.Empty : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CreateProjectFolder(string sourceFolder, string mp3DiscProjectsFolder)
    {
        var projectsRoot = Path.GetFullPath(mp3DiscProjectsFolder);
        Directory.CreateDirectory(projectsRoot);

        var sourceName = GetSourceDisplayName(sourceFolder);
        var safeName = MakeSafeFileName(sourceName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "MP3-CD";

        var folderName = safeName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(projectsRoot, folderName);
    }

    private static string GetSourceDisplayName(string sourceFolder)
    {
        try
        {
            var root = Path.GetPathRoot(sourceFolder);
            if (!string.IsNullOrWhiteSpace(root) &&
                string.Equals(Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                var drive = new DriveInfo(root);
                if (!string.IsNullOrWhiteSpace(drive.VolumeLabel))
                    return drive.VolumeLabel;
            }
        }
        catch
        {
            // Anzeige-Name ist nur Komfort. Bei Fehlern verwenden wir den Ordnernamen.
        }

        var name = new DirectoryInfo(sourceFolder).Name;
        return string.IsNullOrWhiteSpace(name) ? "MP3-CD" : name;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.Join("_", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string GetRelativePathSafe(string baseFolder, string filePath)
    {
        try
        {
            return Path.GetRelativePath(baseFolder, filePath);
        }
        catch
        {
            return Path.GetFileName(filePath);
        }
    }

    private static bool IsCdDrive(string sourceFolder)
    {
        try
        {
            var root = Path.GetPathRoot(sourceFolder);
            if (string.IsNullOrWhiteSpace(root))
                return false;

            var drive = new DriveInfo(root);
            return drive.DriveType == DriveType.CDRom;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string folder)
    {
        var pending = new Stack<string>();
        pending.Push(folder);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files)
                yield return file;

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                directories = Array.Empty<string>();
            }

            foreach (var directory in directories)
                pending.Push(directory);
        }
    }
}
