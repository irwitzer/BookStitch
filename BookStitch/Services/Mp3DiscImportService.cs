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
    public bool IsSupportedDataDisc => SupportedAudioFiles.Count > 0 && !HasCdaFiles;
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
    string ImportedFile,
    SourceFileCopyIdentity? SourceIdentity = null);

public sealed record DiscImportResult(
    string SourceFolder,
    string ImportedFolder,
    string ProjectFolder,
    int DiscNumber,
    int TotalDiscs,
    int CopiedFiles,
    bool WasCanceled = false,
    IReadOnlyList<SourceFileCopyIdentity>? ImportedFiles = null);

public sealed class Mp3DiscImportService
{
    private readonly SourceCopyIdentityStore _sourceCopyIdentityStore = new();

    public DiscSourceAnalysis AnalyzeSource(string sourceFolder, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            return new DiscSourceAnalysis(
                sourceFolder,
                Array.Empty<string>(),
                HasCdaFiles: false,
                IsCdDrive: false);
        }

        var files = SafeFileSystemEnumeration.EnumerateFiles(sourceFolder, cancellationToken).ToList();
        var audioFiles = files
            .Where(file => AudioInputFormatCatalog.IsDataDiscImportExtension(Path.GetExtension(file)))
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
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, Guid>? plannedSourceIdsByRelativePath = null, Action<string>? sourceReadStarted = null)
    {
        return Task.Run(async () =>
        {
            var discFolder = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, discNumber);
            var copiedFiles = 0;
            var readStarted = false;
            try
            {
                var analysis = AnalyzeSource(sourceFolder, cancellationToken);
                if (!analysis.IsSupportedDataDisc)
                    throw new InvalidOperationException("Auf der ausgewählten Quelle wurden keine unterstützten Audiodateien gefunden.");

                ProjectFolderLayout.EnsureProjectFolders(projectFolder);

                Directory.CreateDirectory(discFolder);

                var files = analysis.SupportedAudioFiles.ToList();

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
                            ImportedFiles: null,
                            WasCanceled: true);
                    }

                    var sourceFile = files[index];
                    var relativePath = GetRelativePathSafe(sourceFolder, sourceFile);
                    var targetFile = Path.Combine(discFolder, relativePath);
                    var targetDirectory = Path.GetDirectoryName(targetFile);

                    if (!string.IsNullOrWhiteSpace(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);

                    string? contentSha256 = null;
                    var copiedNow = !_sourceCopyIdentityStore.CanReuseExistingCopy(
                        projectFolder,
                        sourceFile,
                        targetFile, cancellationToken);
                    if (copiedNow)
                    {
                        contentSha256 = await SourceFileCopyService.CopyAtomicallyAsync(
                            sourceFile, targetFile, cancellationToken, () =>
                            {
                                if (readStarted) return;
                                readStarted = true;
                                sourceReadStarted?.Invoke(sourceFolder);
                            }).ConfigureAwait(false);
                    }

                    var normalizedRelativePath = Mp3DiscSourceIdentityPlanner.NormalizeRelativePath(relativePath);
                    var plannedSourceId = Guid.Empty;
                    plannedSourceIdsByRelativePath?.TryGetValue(normalizedRelativePath, out plannedSourceId);
                    var sourceIdentity = _sourceCopyIdentityStore.RecordCopiedFile(
                        projectFolder,
                        sourceFile,
                        targetFile,
                        plannedSourceId,
                        contentSha256, cancellationToken);

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
                            targetFile,
                            sourceIdentity));
                    }
                }

                return new DiscImportResult(
                    sourceFolder,
                    discFolder,
                    projectFolder,
                    discNumber,
                    totalDiscs,
                    copiedFiles,
                    ImportedFiles: _sourceCopyIdentityStore.Load(projectFolder)
                        .Where(identity => IsIdentityUnderDiscFolder(projectFolder, discFolder, identity))
                        .ToList());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new DiscImportResult(sourceFolder, discFolder, projectFolder, discNumber,
                    totalDiscs, copiedFiles, WasCanceled: true);
            }
        });
    }

    private static bool IsIdentityUnderDiscFolder(
        string projectFolder,
        string discFolder,
        SourceFileCopyIdentity identity)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(projectFolder, identity.TargetRelativePath));
            var fullDiscFolder = Path.GetFullPath(discFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.StartsWith(fullDiscFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
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
        var sourceName = GetSourceDisplayName(sourceFolder);
        return new ProjectFolderNameService().CreateUniqueProjectFolder(
            mp3DiscProjectsFolder,
            sourceName,
            "MP3-CD");
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

}
