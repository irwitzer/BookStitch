using BookStitch.Models;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BookStitch.Services;

public sealed record Mp3DiscAdditionalImportPlan(
    int CompletedDiscCount,
    int CurrentTotalDiscs,
    int MinimumTotalDiscs,
    int DefaultTotalDiscs,
    int MaximumTotalDiscs);

public sealed record Mp3DiscResumePlan(
    int CompletedDiscCount,
    int CurrentTotalDiscs,
    int MinimumTotalDiscs,
    int? NextMissingDiscNumber,
    string SetupMessage);

public sealed class Mp3DiscProjectService
{
    private const string ManifestFileName = ProjectFolderLayout.Mp3DiscManifestFileName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly AtomicJsonStore JsonStore = new();

    public Mp3DiscProjectManifest LoadOrCreate(
        string projectFolder,
        string sourceFolder,
        int totalDiscs,
        string exportPreset,
        string parallelJobs,
        string outputExtension,
        string outputFolder,
        string fileNameTemplate,
        DiscDriveInfo? sourceDriveInfo = null,
        int targetSampleRateHz = 44100)
    {
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var manifestPath = ProjectFolderLayout.ResolveMp3DiscManifestPath(projectFolder);

        try
        {
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                var schema = PersistentJsonSchema.Read(
                    json,
                    "MP3-CD-Projektmanifest",
                    manifestPath,
                    Mp3DiscManifestVersions.CurrentSchemaVersion);
                PersistentJsonSchema.RequireState(json, manifestPath, "ImportedDiscs", "TotalDiscs");
                var loaded = JsonSerializer.Deserialize<Mp3DiscProjectManifest>(json, JsonOptions);

                if (loaded is not null)
                {
                    var before = JsonSerializer.Serialize(loaded, JsonOptions);
                    var normalized = NormalizeLoadedManifest(loaded, projectFolder);
                    loaded.SourceFolder = sourceFolder;
                    ApplyDriveSnapshot(loaded, sourceDriveInfo);
                    loaded.TotalDiscs = totalDiscs;
                    UpdateSettingsSnapshot(loaded, exportPreset, parallelJobs, outputExtension, outputFolder, fileNameTemplate);
                    if (schema.IsLegacy || normalized || before != JsonSerializer.Serialize(loaded, JsonOptions))
                    {
                        JsonStore.Save(manifestPath, loaded, JsonOptions);
                        if (schema.IsLegacy)
                            PersistentJsonSchema.TraceLegacyMigration("MP3-CD-Projektmanifest", manifestPath);
                    }

                    return loaded;
                }
                throw new PersistentJsonSchemaException(LocalizationText.Get("Persistence.Manifest.Invalid"));
            }
        }
        catch (PersistentJsonSchemaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(LocalizationText.Get("Persistence.Manifest.Unreadable"), ex);
        }

        var now = DateTime.UtcNow;
        return new Mp3DiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectFormatVersion = ProjectFormat.CurrentProjectFormatVersion,
            ProjectId = Guid.NewGuid().ToString("N"),
            ProjectFolder = projectFolder,
            SourceFolder = sourceFolder,
            SourceDriveRoot = sourceDriveInfo?.RootPath ?? string.Empty,
            SourceDriveName = sourceDriveInfo?.DiagnosticDriveName ?? string.Empty,
            SourceDriveDevicePath = sourceDriveInfo?.DevicePath ?? string.Empty,
            SourceVolumeLabel = sourceDriveInfo?.VolumeLabel ?? string.Empty,
            TotalDiscs = totalDiscs,
            CreatedUtc = now,
            UpdatedUtc = now,
            ImportedDiscs = [],
            ExportPreset = exportPreset,
            TargetSampleRateHz = SampleRateSettingsService.Normalize(targetSampleRateHz),
            ParallelJobs = parallelJobs,
            OutputExtension = outputExtension,
            OutputFolder = outputFolder,
            FileNameTemplate = fileNameTemplate
        };
    }


    public Mp3DiscProjectManifest? TryLoad(string projectFolder)
    {
        try
        {
            var manifestPath = ProjectFolderLayout.ResolveMp3DiscManifestPath(projectFolder);
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            var schema = PersistentJsonSchema.Read(
                json,
                "MP3-CD-Projektmanifest",
                manifestPath,
                Mp3DiscManifestVersions.CurrentSchemaVersion);
            PersistentJsonSchema.RequireState(json, manifestPath, "ImportedDiscs", "TotalDiscs");
            var manifest = JsonSerializer.Deserialize<Mp3DiscProjectManifest>(json, JsonOptions);
            if (manifest is null) throw new PersistentJsonSchemaException(LocalizationText.Get("Persistence.Manifest.Invalid"));
            if (manifest is not null)
            {
                var normalized = NormalizeLoadedManifest(manifest, projectFolder);
            }

            return manifest;
        }
        catch (PersistentJsonSchemaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(LocalizationText.Get("Persistence.Manifest.Unreadable"), ex);
        }
    }

    public int CountCompletedImportedDiscs(Mp3DiscProjectManifest manifest)
    {
        return manifest.ImportedDiscs
            .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .Select(disc => disc.DiscNumber)
            .Distinct()
            .Count();
    }

    public int GetMinimumDiscCount(Mp3DiscProjectManifest manifest)
    {
        var highestImportedDiscNumber = manifest.ImportedDiscs
            .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .Select(disc => disc.DiscNumber)
            .DefaultIfEmpty(1)
            .Max();

        return Math.Clamp(highestImportedDiscNumber, 1, 99);
    }


    public int GetMinimumTotalDiscsForAdditionalImport(Mp3DiscProjectManifest manifest)
    {
        var highestCompletedDiscNumber = GetMinimumDiscCount(manifest);
        var minimumTotalDiscs = highestCompletedDiscNumber + 1;

        return Math.Clamp(minimumTotalDiscs, 1, 99);
    }

    public Mp3DiscResumePlan BuildResumePlan(Mp3DiscProjectManifest manifest, int maximumTotalDiscs = 99)
    {
        var clampedMaximum = Math.Clamp(maximumTotalDiscs, 1, 99);
        var completedDiscCount = CountCompletedImportedDiscs(manifest);
        var minimumTotalDiscs = Math.Min(GetMinimumDiscCount(manifest), clampedMaximum);
        var currentTotalDiscs = Math.Clamp(
            Math.Max(manifest.TotalDiscs, minimumTotalDiscs),
            minimumTotalDiscs,
            clampedMaximum);
        var nextMissingDiscNumber = GetNextMissingDiscNumber(manifest, currentTotalDiscs);

        return new Mp3DiscResumePlan(
            CompletedDiscCount: completedDiscCount,
            CurrentTotalDiscs: currentTotalDiscs,
            MinimumTotalDiscs: minimumTotalDiscs,
            NextMissingDiscNumber: nextMissingDiscNumber,
            SetupMessage: BuildResumeSetupMessage(completedDiscCount, currentTotalDiscs, nextMissingDiscNumber));
    }

    public void UpdateResumeDiscPlan(
        Mp3DiscProjectManifest manifest,
        int totalDiscs,
        string sourceFolder,
        int maximumTotalDiscs = 99)
    {
        var clampedMaximum = Math.Clamp(maximumTotalDiscs, 1, 99);
        var minimumTotalDiscs = Math.Min(GetMinimumDiscCount(manifest), clampedMaximum);

        if (totalDiscs < minimumTotalDiscs || totalDiscs > clampedMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalDiscs),
                $"Die Gesamtzahl muss zwischen {minimumTotalDiscs} und {clampedMaximum} liegen.");
        }

        manifest.TotalDiscs = totalDiscs;
        manifest.SourceFolder = sourceFolder?.Trim() ?? string.Empty;
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public Mp3DiscAdditionalImportPlan BuildAdditionalImportPlan(Mp3DiscProjectManifest manifest, int maximumTotalDiscs = 99)
    {
        var clampedMaximum = Math.Clamp(maximumTotalDiscs, 1, 99);
        var completedDiscCount = CountCompletedImportedDiscs(manifest);
        var minimumTotalDiscs = Math.Min(GetMinimumTotalDiscsForAdditionalImport(manifest), clampedMaximum);
        var currentTotalDiscs = Math.Clamp(manifest.TotalDiscs, 1, clampedMaximum);
        var defaultTotalDiscs = minimumTotalDiscs;

        return new Mp3DiscAdditionalImportPlan(
            CompletedDiscCount: completedDiscCount,
            CurrentTotalDiscs: currentTotalDiscs,
            MinimumTotalDiscs: minimumTotalDiscs,
            DefaultTotalDiscs: defaultTotalDiscs,
            MaximumTotalDiscs: clampedMaximum);
    }

    public void IncreaseTotalDiscsForAdditionalImport(Mp3DiscProjectManifest manifest, int newTotalDiscs)
    {
        var minimumTotalDiscs = GetMinimumTotalDiscsForAdditionalImport(manifest);
        if (newTotalDiscs < minimumTotalDiscs || newTotalDiscs > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newTotalDiscs),
                $"Die Gesamtzahl muss zwischen {minimumTotalDiscs} und 99 liegen.");
        }

        manifest.TotalDiscs = newTotalDiscs;
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public int? GetNextMissingDiscNumber(Mp3DiscProjectManifest manifest, int totalDiscs)
    {
        var importedDiscNumbers = manifest.ImportedDiscs
            .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .Select(disc => disc.DiscNumber)
            .ToHashSet();

        for (var discNumber = 1; discNumber <= totalDiscs; discNumber++)
        {
            if (!importedDiscNumbers.Contains(discNumber))
                return discNumber;
        }

        return null;
    }

    public void CompleteDiscImportEarly(Mp3DiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var completedDiscNumbers = manifest.ImportedDiscs
            .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .Select(disc => disc.DiscNumber)
            .OrderBy(number => number)
            .ToArray();
        if (completedDiscNumbers.Length == 0)
            throw new InvalidOperationException("At least one completed disc is required.");

        manifest.TotalDiscs = completedDiscNumbers.Max();
        manifest.ImportedDiscs.RemoveAll(disc => disc.DiscNumber > manifest.TotalDiscs);
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public string BuildResumeSetupMessage(int importedDiscCount, int totalDiscs, int? nextMissingDisc)
    {
        var status = LocalizationText.Format("Disc.Resume.Imported", importedDiscCount, totalDiscs);
        var next = nextMissingDisc.HasValue
            ? LocalizationText.Format("Disc.Resume.NextMissing", nextMissingDisc.Value)
            : LocalizationText.Get("Disc.Resume.AllImported");

        return status + "\n" + next + "\n\n" +
               LocalizationText.Get("Disc.Resume.CorrectSettings") +
               LocalizationText.Get("Disc.TotalBelowImported");
    }

    public void UpdateSettingsSnapshot(
        Mp3DiscProjectManifest manifest,
        string exportPreset,
        string parallelJobs,
        string outputExtension,
        string outputFolder,
        string fileNameTemplate)
    {
        if (!manifest.IsStoppedForResume)
            manifest.ExportPreset = exportPreset ?? "";
        manifest.ParallelJobs = parallelJobs ?? "";
        manifest.OutputExtension = outputExtension ?? "";
        manifest.OutputFolder = outputFolder ?? "";
        manifest.FileNameTemplate = fileNameTemplate ?? "";
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public void UpdateCoverAndOutputSnapshot(
        Mp3DiscProjectManifest manifest,
        string coverSourcePath,
        string processedCoverPath,
        string outputFileName)
    {
        manifest.CoverSourcePath = coverSourcePath ?? "";
        manifest.ProcessedCoverPath = processedCoverPath ?? "";
        manifest.OutputFileName = outputFileName ?? "";
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkDiscCompleted(
        Mp3DiscProjectManifest manifest,
        int discNumber,
        string signature,
        string sourcePath,
        string localFolder,
        int fileCount,
        int copiedFiles,
        DiscDriveInfo? sourceDriveInfo = null,
        IReadOnlyCollection<SourceFileCopyIdentity>? sourceFiles = null,
        bool preserveExistingDiscWithSameSignature = false)
    {
        manifest.ImportedDiscs.RemoveAll(disc => disc.DiscNumber == discNumber);
        if (!preserveExistingDiscWithSameSignature)
            manifest.ImportedDiscs.RemoveAll(disc =>
                !string.IsNullOrWhiteSpace(signature) &&
                string.Equals(disc.Signature, signature, StringComparison.OrdinalIgnoreCase));

        manifest.ImportedDiscs.Add(new Mp3DiscProjectManifestDisc
        {
            DiscNumber = discNumber,
            Status = Mp3DiscImportStatus.Completed,
            Signature = signature,
            SourcePath = sourcePath,
            SourceDriveRoot = sourceDriveInfo?.RootPath ?? string.Empty,
            SourceDriveName = sourceDriveInfo?.DiagnosticDriveName ?? string.Empty,
            SourceDriveDevicePath = sourceDriveInfo?.DevicePath ?? string.Empty,
            SourceVolumeLabel = sourceDriveInfo?.VolumeLabel ?? string.Empty,
            LocalFolder = localFolder,
            FileCount = fileCount,
            CopiedFiles = copiedFiles,
            CompletedUtc = DateTime.UtcNow,
            SourceFiles = CreateManifestSourceFiles(manifest.ProjectFolder, sourceFiles)
        });

        manifest.ImportedDiscs = manifest.ImportedDiscs
            .OrderBy(disc => disc.DiscNumber)
            .ToList();
        manifest.PipelineState = manifest.ImportedDiscs.Count >= manifest.TotalDiscs
            ? ProjectPipelineStateNames.Converting
            : ProjectPipelineStateNames.AcquiringSources;
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    private static void ApplyDriveSnapshot(Mp3DiscProjectManifest manifest, DiscDriveInfo? sourceDriveInfo)
    {
        if (sourceDriveInfo is null)
            return;

        manifest.SourceDriveRoot = sourceDriveInfo.RootPath;
        manifest.SourceDriveName = sourceDriveInfo.DiagnosticDriveName;
        manifest.SourceDriveDevicePath = sourceDriveInfo.DevicePath;
        manifest.SourceVolumeLabel = sourceDriveInfo.VolumeLabel;
    }

    internal Mp3DiscProjectManifest? LoadForResume(string projectFolder)
    {
        var path = ProjectFolderLayout.GetMp3DiscManifestPath(projectFolder);
        lock (AtomicJsonStore.ForPath(path))
        {
            var manifest = TryLoad(projectFolder);
            if (manifest is not null && !System.Text.Json.Nodes.JsonNode.DeepEquals(
                    System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)), JsonSerializer.SerializeToNode(manifest, JsonOptions)))
                Save(manifest);
            return manifest;
        }
    }

    public void Save(Mp3DiscProjectManifest manifest)
    {
        NormalizeLoadedManifest(manifest, manifest.ProjectFolder, validateSources: false);
        manifest.ProjectFormatVersion = ProjectFormat.CurrentProjectFormatVersion;
        manifest.ProjectType = ProjectManifestTypes.Mp3DiscProject;
        if (string.IsNullOrWhiteSpace(manifest.ProjectId)) manifest.ProjectId = Guid.NewGuid().ToString("N");
        ProjectFolderLayout.EnsureProjectFolders(manifest.ProjectFolder);
        manifest.UpdatedUtc = DateTime.UtcNow;

        var manifestPath = GetManifestPath(manifest.ProjectFolder);
        JsonStore.Save(manifestPath, manifest, JsonOptions);
    }

    internal static Mp3DiscProjectManifest NormalizeForUpgrade(Mp3DiscProjectManifest manifest, string projectFolder)
    {
        NormalizeLoadedManifest(manifest, projectFolder, validateSources: false);
        return manifest;
    }

    private static bool NormalizeLoadedManifest(Mp3DiscProjectManifest manifest, string projectFolder, bool validateSources = true)
    {
        var before = JsonSerializer.Serialize(manifest, JsonOptions);
        var changed = false;

        manifest.SchemaVersion = Mp3DiscManifestVersions.CurrentSchemaVersion;
        var loadedFormatVersion = manifest.FormatVersion;
        manifest.FormatVersion = Mp3DiscManifestVersions.Current;
        manifest.ProjectType = string.IsNullOrWhiteSpace(manifest.ProjectType) ? "Mp3Disc" : manifest.ProjectType;
        manifest.ProjectFolder = projectFolder ?? string.Empty;
        var sourcesComplete = manifest.ImportedDiscs?.Count >= manifest.TotalDiscs && manifest.TotalDiscs > 0;
        var storedPipelineState = !string.Equals(
                loadedFormatVersion,
                Mp3DiscManifestVersions.Current,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                manifest.PipelineState,
                ProjectPipelineStateNames.Preparing,
                StringComparison.OrdinalIgnoreCase)
            ? (sourcesComplete ? ProjectPipelineStateNames.Converting : ProjectPipelineStateNames.AcquiringSources)
            : manifest.PipelineState;
        manifest.PipelineState = ProjectPipelineStateNames.FromManifestValue(
            storedPipelineState,
            hasSuccessfulExport: false,
            sourcesComplete: sourcesComplete).ToManifestValue();
        manifest.SourceFolder ??= string.Empty;
        manifest.SourceDriveRoot ??= string.Empty;
        manifest.SourceDriveName ??= string.Empty;
        manifest.SourceDriveDevicePath ??= string.Empty;
        manifest.SourceVolumeLabel ??= string.Empty;
        manifest.ExportPreset ??= string.Empty;
        manifest.ParallelJobs ??= string.Empty;
        manifest.OutputExtension ??= string.Empty;
        manifest.OutputFolder ??= string.Empty;
        manifest.FileNameTemplate ??= string.Empty;
        manifest.OutputFileName ??= string.Empty;
        manifest.Title ??= string.Empty;
        manifest.Author ??= string.Empty;
        manifest.Narrator ??= string.Empty;
        manifest.Genre ??= string.Empty;
        manifest.CoverSourcePath ??= string.Empty;
        manifest.ProcessedCoverPath ??= string.Empty;
        manifest.ImportedDiscs ??= [];

        foreach (var disc in manifest.ImportedDiscs)
        {
            disc.Status ??= Mp3DiscImportStatus.Completed;
            disc.Signature ??= string.Empty;
            disc.SourcePath ??= string.Empty;
            disc.SourceDriveRoot ??= string.Empty;
            disc.SourceDriveName ??= string.Empty;
            disc.SourceDriveDevicePath ??= string.Empty;
            disc.SourceVolumeLabel ??= string.Empty;
            disc.LocalFolder ??= string.Empty;
            disc.SourceFiles ??= [];
        }

        changed |= BackfillMissingSourceFiles(manifest);
        if (validateSources) ProjectSourceIntegrityService.Reconcile(manifest);
        return changed || before != JsonSerializer.Serialize(manifest, JsonOptions);
    }

    private static bool BackfillMissingSourceFiles(Mp3DiscProjectManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.ProjectFolder))
            return false;

        var changed = false;
        var evidence = BuildSourceFileEvidence(manifest.ProjectFolder);

        foreach (var disc in manifest.ImportedDiscs)
        {
            var discFolder = ResolveDiscFolder(manifest.ProjectFolder, disc);
            var sourceFilesByRelativePath = disc.SourceFiles
                .Where(file => !string.IsNullOrWhiteSpace(file.RelativePath))
                .GroupBy(file => NormalizeRelativePath(file.RelativePath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in BuildDiscSourceFileCandidates(manifest.ProjectFolder, discFolder, evidence))
            {
                if (!sourceFilesByRelativePath.ContainsKey(candidate.RelativePath))
                {
                    var added = new Mp3DiscProjectManifestSourceFile
                    {
                        SourceId = candidate.SourceId,
                        RelativePath = candidate.RelativePath,
                        SourceFileName = candidate.SourceFileName,
                        SourceSizeBytes = candidate.SourceSizeBytes,
                        SourceLastWriteUtcTicks = candidate.SourceLastWriteUtcTicks
                    };
                    disc.SourceFiles.Add(added);
                    sourceFilesByRelativePath[added.RelativePath] = added;
                    changed = true;
                }
            }

            foreach (var sourceFile in disc.SourceFiles)
            {
                sourceFile.RelativePath = NormalizeRelativePath(sourceFile.RelativePath);
                sourceFile.SourceFileName ??= string.Empty;

                if (sourceFile.SourceId != Guid.Empty)
                    continue;

                sourceFile.SourceId = ResolveExistingSourceId(
                    manifest.ProjectFolder,
                    sourceFile,
                    evidence) ?? Guid.Empty;
                changed = true;
            }

            disc.SourceFiles = disc.SourceFiles
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return changed;
    }

    private static IEnumerable<SourceFileEvidence> BuildDiscSourceFileCandidates(
        string projectFolder,
        string discFolder,
        IReadOnlyCollection<SourceFileEvidence> evidence)
    {
        foreach (var item in evidence.Where(item => IsPathInsideFolder(item.SourcePath, discFolder)))
            yield return item;

        if (!Directory.Exists(discFolder))
            yield break;

        IEnumerable<string> files;
        try
        {
            files = SafeFileSystemEnumeration.EnumerateFiles(discFolder).ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var file in files.Where(file => AudioInputFormatCatalog.IsDataDiscImportExtension(Path.GetExtension(file))))
        {
            yield return new SourceFileEvidence(
                SourceId: Guid.Empty,
                SourcePath: file,
                RelativePath: NormalizeProjectRelativePath(projectFolder, Path.GetRelativePath(projectFolder, file)),
                SourceFileName: Path.GetFileName(file),
                SourceSizeBytes: GetFileSizeSafe(file),
                SourceLastWriteUtcTicks: GetLastWriteTicksSafe(file));
        }
    }

    private static Guid? ResolveExistingSourceId(
        string projectFolder,
        Mp3DiscProjectManifestSourceFile sourceFile,
        IReadOnlyCollection<SourceFileEvidence> evidence)
    {
        if (evidence.Count == 0)
            return null;

        var sourcePath = GetOriginalSourcePath(projectFolder, sourceFile.RelativePath);
        var pathMatch = evidence.FirstOrDefault(item =>
            item.SourceId != Guid.Empty &&
            !string.IsNullOrWhiteSpace(item.SourcePath) &&
            PathsEqual(item.SourcePath, sourcePath));
        if (pathMatch is not null)
            return pathMatch.SourceId;

        if (sourceFile.SourceSizeBytes > 0 && sourceFile.SourceLastWriteUtcTicks > 0)
        {
            var matches = evidence
                .Where(item =>
                    item.SourceId != Guid.Empty &&
                    item.SourceSizeBytes == sourceFile.SourceSizeBytes &&
                    item.SourceLastWriteUtcTicks == sourceFile.SourceLastWriteUtcTicks &&
                    string.Equals(item.SourceFileName, sourceFile.SourceFileName, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.SourceId)
                .Distinct()
                .Take(2)
                .ToList();

            if (matches.Count == 1)
                return matches[0];
        }

        return null;
    }

    private static IReadOnlyCollection<SourceFileEvidence> BuildSourceFileEvidence(string projectFolder)
    {
        var result = new List<SourceFileEvidence>();
        AddSourceCopyIdentityEvidence(projectFolder, result);
        AddExportManifestEvidence(ProjectFolderLayout.ResolveExportManifestPath(projectFolder), result);
        return result;
    }

    private static void AddSourceCopyIdentityEvidence(string projectFolder, List<SourceFileEvidence> result)
    {
        try
        {
            foreach (var identity in new SourceCopyIdentityStore().Load(projectFolder))
            {
                var sourcePath = Path.Combine(projectFolder, identity.TargetRelativePath);
                result.Add(new SourceFileEvidence(
                    identity.SourceId,
                    sourcePath,
                    NormalizeProjectRelativePath(projectFolder, identity.TargetRelativePath),
                    identity.SourceFileName,
                    identity.SourceSizeBytes,
                    identity.SourceLastWriteUtcTicks));
            }
        }
        catch
        {
            // Copy identity evidence is optional while normalizing legacy MP3-CD manifests.
        }
    }

    private static void AddExportManifestEvidence(string manifestPath, List<SourceFileEvidence> result)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            PersistentJsonSchema.Read(
                json,
                "Work-/Export-Manifest",
                manifestPath,
                ExportWorkManifestVersions.CurrentSchemaVersion);
            var manifest = JsonSerializer.Deserialize<ExportWorkManifest>(json, JsonOptions);
            if (manifest is null)
                return;

            foreach (var track in manifest.Tracks)
            {
                if (string.IsNullOrWhiteSpace(track.SourcePath))
                    continue;

                result.Add(new SourceFileEvidence(
                    track.SourceId,
                    track.SourcePath,
                    NormalizeProjectRelativePath(
                        ProjectFolderLayout.GetProjectFolderFromManifestPath(manifestPath),
                        Path.GetRelativePath(
                            ProjectFolderLayout.GetProjectFolderFromManifestPath(manifestPath),
                            track.SourcePath)),
                    track.SourceFileName,
                    track.SourceSizeBytes,
                    track.SourceLastWriteUtcTicks));
            }
        }
        catch
        {
            // Export manifest evidence is optional while normalizing legacy MP3-CD manifests.
        }
    }

    private static string ResolveDiscFolder(string projectFolder, Mp3DiscProjectManifestDisc disc)
    {
        if (!string.IsNullOrWhiteSpace(disc.LocalFolder) && Directory.Exists(disc.LocalFolder))
            return disc.LocalFolder;

        return ProjectFolderLayout.ResolveDiscOriginalsFolder(projectFolder, Math.Max(1, disc.DiscNumber));
    }

    private static bool IsPathInsideFolder(string path, string folder)
    {
        try
        {
            var fullFolder = Path.GetFullPath(folder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static long GetFileSizeSafe(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static long GetLastWriteTicksSafe(string path)
    {
        try
        {
            return new FileInfo(path).LastWriteTimeUtc.Ticks;
        }
        catch
        {
            return 0;
        }
    }

    public static string GetOriginalSourcePath(string projectFolder,string relativePath) =>
        ProjectManagedPathService.Resolve(projectFolder,Path.Combine("originals",relativePath),"originals");

    private static string NormalizeRelativePath(string relativePath) =>
        (relativePath ?? string.Empty)
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
        .TrimStart(Path.DirectorySeparatorChar);

    private sealed record SourceFileEvidence(
        Guid SourceId,
        string SourcePath,
        string RelativePath,
        string SourceFileName,
        long SourceSizeBytes,
        long SourceLastWriteUtcTicks);

    private static List<Mp3DiscProjectManifestSourceFile> CreateManifestSourceFiles(
        string projectFolder,
        IReadOnlyCollection<SourceFileCopyIdentity>? sourceFiles)
    {
        if (sourceFiles is null || sourceFiles.Count == 0)
            return [];

        return sourceFiles
            .Select(file => new Mp3DiscProjectManifestSourceFile
            {
                SourceId = file.SourceId,
                RelativePath = NormalizeProjectRelativePath(projectFolder, file.TargetRelativePath),
                SourceFileName = file.SourceFileName,
                SourceSizeBytes = file.SourceSizeBytes,
                SourceLastWriteUtcTicks = file.SourceLastWriteUtcTicks
            })
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeProjectRelativePath(string projectFolder, string targetRelativePath)
    {
        try
        {
            return Path.GetRelativePath(
                    ProjectFolderLayout.GetOriginalsFolder(projectFolder),
                    Path.Combine(projectFolder, targetRelativePath))
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
        catch
        {
            return (targetRelativePath ?? string.Empty)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }
    }

    public static string GetManifestPath(string projectFolder)
    {
        return ProjectFolderLayout.GetMp3DiscManifestPath(projectFolder);
    }
}
