using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ProjectExtensionRollbackSnapshot(
    string ProjectFolder,
    HashSet<string> ExistingFiles,
    HashSet<string> ExistingDirectories,
    IReadOnlyDictionary<string, byte[]> JsonFiles)
{
    public Guid RunId { get; init; } = Guid.NewGuid();
    public string Kind { get; init; } = "";
    public string ProjectId { get; init; } = "";
    public Dictionary<string, ProjectRollbackFileIdentity> FileIdentities { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ConvertedBackups { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string FinalPartPath { get; init; } = "";
}

public sealed record ProjectRollbackFileIdentity(long Length, DateTime WriteUtc);

internal sealed class ProjectRollbackJournal
{
    public int Version { get; set; } = 1;
    public string Stage { get; set; } = "Pending";
    public required ProjectExtensionRollbackSnapshot Snapshot { get; set; }
}

public sealed record ProjectExtensionRollbackResult(
    int DeletedFiles,
    int DeletedDirectories,
    int RestoredJsonFiles);

public sealed record PresetReconversionRollbackContext(
    ProjectExtensionRollbackSnapshot Snapshot,
    string PreviousPreset,
    string TargetPreset,
    string TargetPresetFolder,
    bool TargetPresetFolderExisted,
    ProjectPipelineState PreviousPhase = ProjectPipelineState.ReviewBeforeMerge);

public sealed class ProjectExtensionRollbackService
{
    private static readonly ConcurrentDictionary<string, ProjectExtensionRollbackSnapshot> Active = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object TransactionGate = new();
    internal static string JournalPath(string root) => Path.Combine(root, "project-settings", "work-attempt.pending.json");
    private static string BackupFolder(ProjectExtensionRollbackSnapshot snapshot) =>
        Path.Combine(snapshot.ProjectFolder, "merge", "rollback-" + snapshot.RunId.ToString("N"));

    private const int CleanupRetryCount = 20;
    private static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromMilliseconds(100);

    public ProjectExtensionRollbackSnapshot Capture(string projectFolder)
    {
        var fullProjectFolder = ValidateProjectFolder(projectFolder);
        SafeFileSystemEnumeration.EnsureSafeTree(fullProjectFolder);
        Directory.CreateDirectory(fullProjectFolder);

        var files = Directory
            .EnumerateFiles(fullProjectFolder, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => !TrackPathService.PathEquals(path, Path.Combine(ProjectFolderLayout.GetMergeFolder(fullProjectFolder), "concat-list.txt")) &&
                !TrackPathService.PathEquals(path, Path.Combine(ProjectFolderLayout.GetMergeFolder(fullProjectFolder), "chapters.ffmetadata")))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directories = Directory
            .EnumerateDirectories(fullProjectFolder, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        directories.Add(fullProjectFolder);

        var jsonFiles = files
            .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                path => path,
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);

        return new ProjectExtensionRollbackSnapshot(
            fullProjectFolder,
            files,
            directories,
            jsonFiles)
        {
            ProjectId = ReadProjectId(fullProjectFolder),
            FileIdentities = files.Where(path => !jsonFiles.ContainsKey(path)).ToDictionary(path => path,
                path => new ProjectRollbackFileIdentity(new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    public ProjectExtensionRollbackSnapshot CaptureForAdditionalSources(string projectFolder)
        => Begin(Capture(projectFolder) with { Kind = "AdditionalSources" });

    private static string ReadProjectId(string root)
    {
        foreach (var path in new[] { ProjectFolderLayout.GetFolderManifestPath(root),
            ProjectFolderLayout.GetMp3DiscManifestPath(root), ProjectFolderLayout.GetAudioDiscManifestPath(root) })
        {
            if (!File.Exists(path)) continue;
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            if (json.RootElement.TryGetProperty("ProjectId", out var id)) return id.GetString() ?? "";
        }
        return "";
    }

    private static void ValidateFinalPartOwnership(ProjectExtensionRollbackSnapshot snapshot)
    {
        if (snapshot.Kind != "Merge" || string.IsNullOrWhiteSpace(snapshot.FinalPartPath)) return;
        var matched = false;
        foreach (var entry in snapshot.JsonFiles)
        {
            // Only canonical or supported legacy export manifests grant ownership.
            if (!new[] { ProjectFolderLayout.GetExportManifestPath(snapshot.ProjectFolder),
                Path.Combine(snapshot.ProjectFolder, ProjectFolderLayout.ExportManifestFileName),
                ProjectFolderLayout.GetWorkManifestPath(snapshot.ProjectFolder),
                Path.Combine(snapshot.ProjectFolder, ProjectFolderLayout.WorkManifestFileName) }
                .Any(path => TrackPathService.PathEquals(path, entry.Key))) continue;
            using var json = JsonDocument.Parse(entry.Value);
            if (!json.RootElement.TryGetProperty("Export", out var export) ||
                !export.TryGetProperty("OutputFolder", out var folder) ||
                !export.TryGetProperty("OutputFileName", out var name) ||
                string.IsNullOrWhiteSpace(folder.GetString()) || string.IsNullOrWhiteSpace(name.GetString())) continue;
            var outputName = name.GetString()!;
            if (outputName != Path.GetFileName(outputName) || !Path.IsPathFullyQualified(folder.GetString()!)) continue;
            var outputPath = Path.Combine(folder.GetString()!, outputName);
            // Current plans stage in project/merge; version-1 journals may stage beside the output.
            matched |= TrackPathService.PathEquals(snapshot.FinalPartPath,
                ConvertedTrackPathService.GetMergePartFilePath(snapshot.ProjectFolder, outputPath)) ||
                TrackPathService.PathEquals(snapshot.FinalPartPath, ConvertedTrackPathService.GetPartFilePath(outputPath));
        }
        if (!matched) throw new InvalidDataException("Merge rollback has no verified final part ownership.");
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(snapshot.FinalPartPath);
    }

    private static void CleanupFinalPart(ProjectExtensionRollbackSnapshot snapshot)
    {
        ValidateFinalPartOwnership(snapshot);
        if (snapshot.Kind == "Merge" && !string.IsNullOrWhiteSpace(snapshot.FinalPartPath) &&
            !snapshot.ExistingFiles.Contains(snapshot.FinalPartPath) && File.Exists(snapshot.FinalPartPath))
            File.Delete(snapshot.FinalPartPath);
    }

    private static void SaveJournal(ProjectExtensionRollbackSnapshot snapshot, string stage = "Pending")
        => new AtomicJsonStore().Save(JournalPath(snapshot.ProjectFolder),
            new ProjectRollbackJournal { Snapshot = snapshot, Stage = stage }, null);

    private static ProjectExtensionRollbackSnapshot Begin(ProjectExtensionRollbackSnapshot snapshot)
    {
        lock (TransactionGate)
        {
            if (File.Exists(JournalPath(snapshot.ProjectFolder)) || Active.ContainsKey(snapshot.ProjectFolder))
                throw new IOException("A project attempt already owns the rollback boundary.");
            SaveJournal(snapshot); // Durable before the first project mutation.
            Active[snapshot.ProjectFolder] = snapshot;
            return snapshot;
        }
    }

    public ProjectExtensionRollbackSnapshot CaptureMerge(string projectFolder, string finalPartPath)
    {
        var root = ValidateProjectFolder(projectFolder);
        if (Active.TryGetValue(root, out var active) && active.Kind == "Merge") return active;
        return Begin(Capture(root) with { Kind = "Merge", FinalPartPath = Path.GetFullPath(finalPartPath) });
    }

    public ProjectExtensionRollbackSnapshot? GetActive(string projectFolder)
        => Active.GetValueOrDefault(Path.GetFullPath(projectFolder));

    internal static void PreserveActiveConvertedFile(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var snapshot in Active.Values)
            if (snapshot.Kind is "AdditionalSources" or "Reconversion" && snapshot.ExistingFiles.Contains(full))
                new ProjectExtensionRollbackService().PreserveConvertedFile(snapshot, full);
    }

    public void PreserveConvertedFile(ProjectExtensionRollbackSnapshot snapshot, string path)
    {
        var full = Path.GetFullPath(path);
        ValidatePresetFolderInsideConvertedRoot(snapshot.ProjectFolder, full);
        if (!snapshot.ExistingFiles.Contains(full) || !File.Exists(full)) return;
        lock (snapshot)
        {
            if (snapshot.ConvertedBackups.ContainsKey(full)) return;
            var backup = Path.Combine(BackupFolder(snapshot), Guid.NewGuid().ToString("N") + ".aac-backup");
            SafeFileSystemEnumeration.EnsureNoReparseAncestors(backup);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            using (var source = File.OpenRead(full))
            using (var target = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }
            snapshot.ConvertedBackups.Add(full, backup);
            SaveJournal(snapshot); // Copy-on-write: record the backup before the target can be replaced.
        }
    }

    private static void EnsureOwner(ProjectExtensionRollbackSnapshot snapshot)
    {
        var path = JournalPath(snapshot.ProjectFolder);
        if (!File.Exists(path)) return;
        var journal = JsonSerializer.Deserialize<ProjectRollbackJournal>(File.ReadAllBytes(path));
        if (journal?.Snapshot.RunId != snapshot.RunId)
            throw new IOException("A different attempt owns the project rollback boundary.");
    }

    public void Commit(ProjectExtensionRollbackSnapshot snapshot)
    {
        lock (TransactionGate)
        {
            if (!File.Exists(JournalPath(snapshot.ProjectFolder))) return;
            EnsureOwner(snapshot);
            SaveJournal(snapshot, "Committed");
            CleanupBackups(snapshot);
            File.Delete(JournalPath(snapshot.ProjectFolder));
            Active.TryRemove(snapshot.ProjectFolder, out _);
        }
    }

    private static void CleanupBackups(ProjectExtensionRollbackSnapshot snapshot)
    {
        var folder = BackupFolder(snapshot);
        SafeFileSystemEnumeration.EnsureSafeTree(folder);
        if (!Directory.Exists(folder)) return;
        foreach (var path in Directory.EnumerateFiles(folder))
            if (TryDeleteGeneratedFile(snapshot.ProjectFolder, path) == 0 && File.Exists(path))
                throw new IOException("Rollback backup cleanup failed.");
        Directory.Delete(folder, recursive: false);
    }

    public bool Recover(string projectFolder)
    {
        var root = ValidateProjectFolder(projectFolder);
        lock (TransactionGate)
        {
            if (Active.ContainsKey(root) || !File.Exists(JournalPath(root))) return false;
            SafeFileSystemEnumeration.EnsureSafeTree(root);
            var journal = JsonSerializer.Deserialize<ProjectRollbackJournal>(File.ReadAllBytes(JournalPath(root)))
                ?? throw new InvalidDataException("Invalid project rollback journal.");
            var snapshot = journal.Snapshot ?? throw new InvalidDataException("Missing project rollback snapshot.");
            if (journal.Version != 1 || snapshot.RunId == Guid.Empty ||
                !TrackPathService.PathEquals(root, snapshot.ProjectFolder) ||
                snapshot.Kind is not ("AdditionalSources" or "Reconversion" or "Merge") ||
                journal.Stage is not ("Pending" or "Committed") ||
                snapshot.ExistingFiles is null || snapshot.ExistingDirectories is null || snapshot.JsonFiles is null ||
                snapshot.FileIdentities is null || snapshot.ConvertedBackups is null ||
                (!string.IsNullOrEmpty(snapshot.ProjectId) && snapshot.ProjectId != ReadProjectId(root)))
                throw new InvalidDataException("Project rollback journal does not match this project.");
            snapshot = snapshot with
            {
                ExistingFiles = snapshot.ExistingFiles.ToHashSet(StringComparer.OrdinalIgnoreCase),
                ExistingDirectories = snapshot.ExistingDirectories.ToHashSet(StringComparer.OrdinalIgnoreCase)
            };
            foreach (var path in snapshot.ExistingFiles.Concat(snapshot.JsonFiles.Keys).Concat(snapshot.FileIdentities.Keys))
                EnsureInsideProject(root, path);
            foreach (var pair in snapshot.ConvertedBackups)
            {
                ValidatePresetFolderInsideConvertedRoot(root, pair.Key);
                if (!TrackPathService.PathEquals(Path.GetDirectoryName(pair.Value) ?? "", BackupFolder(snapshot)))
                    throw new InvalidDataException("Invalid converted rollback backup.");
            }
            if (journal.Stage == "Committed") Commit(snapshot);
            else Rollback(snapshot);
            return true;
        }
    }

    // Startup owns recovery before catalog/UI access. Explicit project loading repeats it idempotently.
    public IReadOnlyList<string> RecoverInterruptedAttempts(IEnumerable<string> projectParents)
    {
        var failures = new List<string>();
        foreach (var parent in projectParents)
        {
            if (!Directory.Exists(parent)) continue;
            foreach (var folder in Directory.EnumerateDirectories(parent))
            {
                try { Recover(folder); }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
                { failures.Add(folder + ": " + error.Message); }
            }
        }
        return failures;
    }

    public PresetReconversionRollbackContext CapturePresetReconversion(
        string projectFolder,
        string previousPreset,
        string targetPreset)
    {
        if (string.IsNullOrWhiteSpace(previousPreset))
            throw new ArgumentException("Das bisher vorbereitete Preset darf nicht leer sein.", nameof(previousPreset));
        if (string.IsNullOrWhiteSpace(targetPreset))
            throw new ArgumentException("Das neue Preset darf nicht leer sein.", nameof(targetPreset));
        // A partially prepared selected preset is a valid rollback baseline.
        // Repairing it therefore legitimately uses the same previous and target preset.
        var fullProjectFolder = ValidateProjectFolder(projectFolder);
        var parsedTargetPreset = ExportPreset.Parse(targetPreset);
        var targetPresetFolder = ProjectFolderLayout.GetConvertedPresetFolder(
            fullProjectFolder,
            parsedTargetPreset.GetFolderName());

        var fullTargetPresetFolder = Path.GetFullPath(targetPresetFolder);
        var baseline = Capture(fullProjectFolder);
        var jsonFiles = new Dictionary<string, byte[]>(baseline.JsonFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var path in jsonFiles.Keys.ToArray())
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(jsonFiles[path]) as System.Text.Json.Nodes.JsonObject;
            if (node is null) continue;
            var changed = false;
            foreach (var key in new[] { "SelectedPreset", "ExportPreset" })
                if (node.ContainsKey(key) && node[key]?.GetValue<string>() != previousPreset)
                { node[key] = previousPreset; changed = true; }
            if (node["Export"] is System.Text.Json.Nodes.JsonObject export && export.ContainsKey("SelectedPreset") &&
                export["SelectedPreset"]?.GetValue<string>() != previousPreset)
            { export["SelectedPreset"] = previousPreset; changed = true; }
            if (changed) jsonFiles[path] = Encoding.UTF8.GetBytes(node.ToJsonString());
        }
        return new PresetReconversionRollbackContext(
            Begin(baseline with { Kind = "Reconversion", JsonFiles = jsonFiles }),
            previousPreset.Trim(),
            targetPreset.Trim(),
            fullTargetPresetFolder,
            Directory.Exists(fullTargetPresetFolder));
    }

    public ProjectExtensionRollbackResult RollbackPresetReconversion(
        PresetReconversionRollbackContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var projectFolder = ValidateProjectFolder(context.Snapshot.ProjectFolder);
        var targetPresetFolder = ValidatePresetFolderInsideConvertedRoot(
            projectFolder,
            context.TargetPresetFolder);

        SafeFileSystemEnumeration.EnsureSafeTree(targetPresetFolder);
        // Reuse the source-extension boundary: remove only newly created files and
        // restore every project JSON, preserving all artifacts that predate this run.
        return Rollback(context.Snapshot);
    }

    public ProjectExtensionRollbackResult Rollback(
        ProjectExtensionRollbackSnapshot snapshot,
        IEnumerable<string>? generatedFiles = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var projectFolder = ValidateProjectFolder(snapshot.ProjectFolder);
        if (!Directory.Exists(projectFolder))
            return new ProjectExtensionRollbackResult(0, 0, 0);

        SafeFileSystemEnumeration.EnsureSafeTree(projectFolder);
        EnsureOwner(snapshot);
        // Validate before deleting artifacts or restoring manifests, even if the part is absent.
        ValidateFinalPartOwnership(snapshot);
        if (snapshot.Kind == "Merge")
        {
            var mergeFolder = ProjectFolderLayout.GetMergeFolder(projectFolder);
            var owned = new[] { snapshot.FinalPartPath, Path.Combine(mergeFolder, "concat-list.txt"),
                Path.Combine(mergeFolder, "chapters.ffmetadata") };
            var unowned = Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories).FirstOrDefault(file =>
                !snapshot.ExistingFiles.Contains(Path.GetFullPath(file)) &&
                !TrackPathService.PathEquals(file, JournalPath(projectFolder)) &&
                !owned.Any(path => !string.IsNullOrWhiteSpace(path) && TrackPathService.PathEquals(path, file)));
            if (unowned is not null)
                throw new IOException("Merge rollback contains an unowned new artifact; pending journal retained: " + unowned);
        }
        foreach (var pair in snapshot.ConvertedBackups)
        {
            EnsureInsideProject(projectFolder, pair.Key);
            EnsureInsideProject(projectFolder, pair.Value);
            if (File.Exists(pair.Value))
            {
                File.Copy(pair.Value, pair.Key, overwrite: true);
                File.SetLastWriteTimeUtc(pair.Key, snapshot.FileIdentities[pair.Key].WriteUtc);
            }
        }
        foreach (var pair in snapshot.FileIdentities)
        {
            var info = new FileInfo(pair.Key);
            if (!info.Exists || info.Length != pair.Value.Length || info.LastWriteTimeUtc != pair.Value.WriteUtc)
                throw new IOException("An original project artifact changed or is missing: " + pair.Key);
        }
        var explicitGeneratedFiles = generatedFiles?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        var deletedFiles = 0;
        var stablePasses = 0;

        for (var attempt = 0; attempt < CleanupRetryCount && stablePasses < 2; attempt++)
        {
            var deletedThisPass = 0;

            foreach (var generatedFile in explicitGeneratedFiles)
            {
                if (!snapshot.ExistingFiles.Contains(generatedFile))
                    deletedThisPass += TryDeleteGeneratedFile(projectFolder, generatedFile);
                if (!snapshot.ExistingFiles.Contains(generatedFile + ".part"))
                    deletedThisPass += TryDeleteGeneratedFile(projectFolder, generatedFile + ".part");
            }

            foreach (var file in Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories).ToArray())
            {
                var fullPath = Path.GetFullPath(file);
                if (snapshot.ExistingFiles.Contains(fullPath) || TrackPathService.PathEquals(fullPath, JournalPath(projectFolder)) ||
                    snapshot.ConvertedBackups.Values.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                    continue;

                deletedThisPass += TryDeleteGeneratedFile(projectFolder, fullPath);
            }

            deletedFiles += deletedThisPass;
            stablePasses = deletedThisPass == 0 ? stablePasses + 1 : 0;

            if (stablePasses < 2)
                Thread.Sleep(CleanupRetryDelay);
        }

        if (Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories)
            .Any(file => !snapshot.ExistingFiles.Contains(Path.GetFullPath(file)) &&
                !TrackPathService.PathEquals(file, JournalPath(projectFolder)) &&
                !snapshot.ConvertedBackups.Values.Contains(Path.GetFullPath(file), StringComparer.OrdinalIgnoreCase)))
            throw new IOException(LocalizationText.Get("MainWindow.Preset.CleanupFailed"));

        var restoredJsonFiles = RestoreJsonFiles(snapshot, projectFolder);
        CleanupFinalPart(snapshot);
        CleanupBackups(snapshot);

        var deletedDirectories = 0;
        foreach (var directory in Directory
                     .EnumerateDirectories(projectFolder, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            var fullPath = Path.GetFullPath(directory);
            if (snapshot.ExistingDirectories.Contains(fullPath) || Directory.EnumerateFileSystemEntries(fullPath).Any())
                continue;

            EnsureInsideProject(projectFolder, fullPath);
            Directory.Delete(fullPath, recursive: false);
            deletedDirectories++;
        }

        // Keep recovery ownership until directory cleanup has also succeeded.
        if (File.Exists(JournalPath(projectFolder))) File.Delete(JournalPath(projectFolder));
        Active.TryRemove(projectFolder, out _);

        return new ProjectExtensionRollbackResult(
            deletedFiles,
            deletedDirectories,
            restoredJsonFiles);
    }

    private static int RestoreJsonFiles(
        ProjectExtensionRollbackSnapshot snapshot,
        string projectFolder)
    {
        var restoredJsonFiles = 0;
        foreach (var entry in snapshot.JsonFiles)
        {
            EnsureInsideProject(projectFolder, entry.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(entry.Key)!);
            new AtomicJsonStore().RestoreBytes(entry.Key, entry.Value);
            restoredJsonFiles++;
        }

        return restoredJsonFiles;
    }

    private static string ValidatePresetFolderInsideConvertedRoot(
        string projectFolder,
        string presetFolder)
    {
        var convertedRoot = Path.GetFullPath(ProjectFolderLayout.GetConvertedFolder(projectFolder))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPresetFolder = Path.GetFullPath(presetFolder);

        if (!fullPresetFolder.StartsWith(convertedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Preset-Rollback darf nur innerhalb des converted-Ordners arbeiten.");

        return fullPresetFolder;
    }

    private static int TryDeleteGeneratedFile(string projectFolder, string path)
    {
        EnsureInsideProject(projectFolder, path);
        if (!File.Exists(path))
            return 0;

        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return 1;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string ValidateProjectFolder(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            throw new ArgumentException("Der Projektordner darf nicht leer sein.", nameof(projectFolder));

        SafeFileSystemEnumeration.EnsureNoReparseAncestors(projectFolder);
        return Path.GetFullPath(projectFolder);
    }

    private static void EnsureInsideProject(string projectFolder, string path)
    {
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(path);
        var root = projectFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ein Erweiterungs-Rollback darf nur innerhalb des Projektordners arbeiten.");
    }
}
