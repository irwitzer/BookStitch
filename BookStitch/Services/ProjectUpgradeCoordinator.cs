using BookStitch.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BookStitch.Services;

/// <summary>The sole entry point for project layout upgrades. Does not validate audio or repair projects.</summary>
public sealed class ProjectUpgradeCoordinator
{
    public const string FolderFileName = ProjectFolderLayout.FolderManifestFileName;
    public const string Mp3FileName = ProjectFolderLayout.Mp3DiscManifestFileName;
    public const string AudioFileName = ProjectFolderLayout.AudioDiscManifestFileName;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly AtomicJsonStore _store = new();

    public ProjectUpgradeResult EnsureCurrentProjectFormat(string projectFolder)=>EnsureCurrentProjectFormat(projectFolder,null);
    public ProjectUpgradeResult EnsureCurrentProjectFormat(string projectFolder,ProjectIntegrityPerformance? performance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        var root = Path.GetFullPath(projectFolder);
        lock (AtomicJsonStore.ForPath(Path.Combine(root, "project-settings", ".project-upgrade")))
        {
            try { return Upgrade(root,performance); }
            catch (ProjectPathSafetyException ex)
            { return new(root, ProjectUpgradeOutcome.RebuildRequired, 0, ex.Message, true); }
            catch (ProjectUpgradeConflictException ex)
            { return new(root, ProjectUpgradeOutcome.RebuildRequired, 0, ex.Message); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or PersistentJsonSchemaException or InvalidOperationException)
            { return new(root, ProjectUpgradeOutcome.RebuildRequired, 0, ex.Message); }
        }
    }

    private ProjectUpgradeResult Upgrade(string root,ProjectIntegrityPerformance? performance)
    {
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(root);
        var settings = ProjectFolderLayout.GetSettingsFolder(root);
        var mains = new[] { FolderFileName, Mp3FileName, AudioFileName }
            .Select(name => Path.Combine(settings, name)).Where(File.Exists).ToArray();
        if (mains.Length > 1) throw new ProjectUpgradeConflictException("Multiple main project manifests.");
        JsonObject? main = mains.Length == 1 ? Read(mains[0], validateCollections: false) : null;
        var version = main?["ProjectFormatVersion"]?.GetValue<int>() ?? 0;
        if (version == ProjectFormat.CurrentProjectFormatVersion)
        {
            var expectedType = Path.GetFileName(mains[0]) == FolderFileName ? ProjectManifestTypes.FolderProject
                : Path.GetFileName(mains[0]) == Mp3FileName ? ProjectManifestTypes.Mp3DiscProject : ProjectManifestTypes.AudioCdProject;
            if (main!["ProjectType"]?.GetValue<string>() != expectedType ||
                string.IsNullOrWhiteSpace(main["ProjectId"]?.GetValue<string>()) || main["Health"] is not JsonObject)
                return new(root, ProjectUpgradeOutcome.RebuildRequired, version, "Invalid main project identity.");
            if (Existing(root, "project.json") is not null || Existing(root, "audio-disc-project.json") is not null)
                return new(root, ProjectUpgradeOutcome.RebuildRequired, version, "Legacy and current main manifests coexist.");
            using var phase=performance?.Phase("reconcile");
            var reconciliation=new ProjectReconciliationService().Reconcile(root,main,mains[0]);
            return new(root,reconciliation.Complete?reconciliation.Changed?ProjectUpgradeOutcome.Upgraded:ProjectUpgradeOutcome.OK:ProjectUpgradeOutcome.RebuildRequired,
                version,reconciliation.Complete?null:"Project v1 subcontracts require reconstruction.",reconciliation.SafetyBlocked) {Repairs=reconciliation.Repairs};
        }
        if (version != 0)
            return new(root, ProjectUpgradeOutcome.RebuildRequired, version, "Unsupported project format version.");
        if (main is not null) ValidateCollections(main);

        // Preflight every rename and metadata collision before changing any filesystem state.
        var legacyWork = Existing(root, "project.json");
        var legacyAudio = Existing(root, "audio-disc-project.json");
        var export = Existing(root, "export-project.json");
        var workJson = legacyWork is null ? null : Read(legacyWork);
        var audioJson = legacyAudio is null ? null : Read(legacyAudio);
        if (workJson is not null && !IsExport(workJson) && !IsMp3(workJson))
            return new(root, ProjectUpgradeOutcome.RebuildRequired, 0, "Invalid legacy work manifest.");
        if (IsExport(workJson) && IsMp3(workJson))
            throw new ProjectUpgradeConflictException("Ambiguous acquisition/export work manifest.");
        if (audioJson is not null && (audioJson["Discs"] is null ||
            audioJson.ContainsKey("ProjectType") && !IsType(audioJson, ProjectManifestTypes.AudioCdProject)))
            return new(root, ProjectUpgradeOutcome.RebuildRequired, 0, "Invalid Audio-CD manifest.");
        var type = main?["ProjectType"]?.GetValue<string>();
        if (type is null)
            type = audioJson is not null && audioJson["Discs"] is not null ? ProjectManifestTypes.AudioCdProject
                : IsMp3(workJson) ? ProjectManifestTypes.Mp3DiscProject
                : IsExport(workJson) ? ProjectManifestTypes.FolderProject
                : export is not null && IsExport(Read(export)) ? ProjectManifestTypes.FolderProject : null;
        if (type is null) return new(root, ProjectUpgradeOutcome.RebuildRequired, 0, "No supported project manifest.");
        if (audioJson is not null && IsMp3(workJson))
            throw new ProjectUpgradeConflictException("Conflicting Audio-CD and MP3-CD identities.");
        var mainName = type switch
        {
            ProjectManifestTypes.FolderProject => FolderFileName,
            ProjectManifestTypes.Mp3DiscProject or "Mp3Disc" => Mp3FileName,
            ProjectManifestTypes.AudioCdProject => AudioFileName,
            _ => throw new ProjectUpgradeConflictException("Unsupported project type.")
        };
        var targetMain = Path.Combine(settings, mainName);
        if (mains.Length == 1 && !AtomicJsonStore.PathComparer.Equals(mains[0], targetMain) ||
            mainName != AudioFileName && legacyAudio is not null || mainName != Mp3FileName && IsMp3(workJson))
            throw new ProjectUpgradeConflictException("Conflicting project types.");
        var exportEvidence = export is not null ? Read(export) : IsExport(workJson) ? workJson : null;
        var existingId = main?["ProjectId"]?.GetValue<string>();
        var exportId = exportEvidence?["ProjectId"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(existingId) && !string.IsNullOrWhiteSpace(exportId) && existingId != exportId)
            throw new ProjectUpgradeConflictException("Conflicting project identities.");
        var mainSource = mainName == AudioFileName ? legacyAudio : mainName == Mp3FileName ? legacyWork : null;
        var exportSource = IsExport(workJson) ? legacyWork : export;
        PreflightFile(mainSource, targetMain);
        PreflightFile(exportSource, Path.Combine(settings, "export-project.json"));
        foreach (var name in new[] { "project.json", "export-project.json", "audio-disc-project.json", "track-list-state.json" })
            PreflightFile(Path.Combine(root, name), Path.Combine(settings, name));
        var oldMetadata = Path.Combine(settings, "metadata", "sources");
        var newMetadata = Path.Combine(settings, "source-metadata");
        PreflightDirectory(oldMetadata, newMetadata);

        SafeFileSystemEnumeration.EnsureSafeTree(root);
        // Remove only proven identical duplicates before normalization changes either
        // copy. Otherwise our own path normalization could create an artificial conflict.
        RemoveIdenticalDuplicate(mainSource, targetMain);
        RemoveIdenticalDuplicate(exportSource, Path.Combine(settings, ProjectFolderLayout.ExportManifestFileName));
        // Preserve the already supported originals/ripped/CD-folder transformations.
        new ProjectFolderMigrationService().UpgradeLegacyLayout(root);
        ProjectFolderLayout.EnsureProjectFolders(root);
        MoveFile(Existing(root, "audio-disc-project.json"), Path.Combine(settings, AudioFileName));
        var remainingWork = Existing(root, "project.json");
        if (remainingWork is not null)
            MoveFile(remainingWork, Path.Combine(settings,
                IsMp3(Read(remainingWork)) ? Mp3FileName : "export-project.json"));
        MoveFile(Existing(root, "export-project.json"), Path.Combine(settings, "export-project.json"));
        MoveMetadata(oldMetadata, newMetadata);
        var exportPath = Path.Combine(settings, "export-project.json");
        var exportJson = File.Exists(exportPath) ? Read(exportPath) : null;
        main = File.Exists(targetMain) ? Read(targetMain) : new JsonObject
        {
            ["schemaVersion"] = 1, ["FormatVersion"] = "1", ["ProjectType"] = ProjectManifestTypes.FolderProject,
            ["SourceFolder"] = exportJson?["SourceFolder"]?.DeepClone(),
            ["PipelineState"] = exportJson?["State"]?["Status"]?.DeepClone() ?? JsonValue.Create(ProjectPipelineStateNames.Preparing),
            ["CreatedUtc"] = exportJson?["CreatedUtc"]?.DeepClone() ?? JsonValue.Create(DateTime.UtcNow)
        };
        // Persist the existing compatibility normalization once, at the upgrade owner.
        // Passive disc readers keep it in memory and never publish it themselves.
        if (mainName == Mp3FileName)
            MergeNormalized(main, JsonSerializer.SerializeToNode(Mp3DiscProjectService.NormalizeForUpgrade(
                main.Deserialize<Mp3DiscProjectManifest>()!, root))!.AsObject());
        else if (mainName == AudioFileName)
            MergeNormalized(main, JsonSerializer.SerializeToNode(new AudioDiscProjectService().NormalizeForUpgrade(
                main.Deserialize<AudioDiscProjectManifest>()!, root))!.AsObject());
        main["ProjectFolder"] = root;
        if (string.IsNullOrWhiteSpace(main["ProjectId"]?.GetValue<string>()))
            main["ProjectId"] = string.IsNullOrWhiteSpace(exportJson?["ProjectId"]?.GetValue<string>())
                ? JsonValue.Create(Guid.NewGuid().ToString("N")) : exportJson!["ProjectId"]!.DeepClone();
        main["ProjectType"] = mainName == Mp3FileName ? ProjectManifestTypes.Mp3DiscProject
            : mainName == AudioFileName ? ProjectManifestTypes.AudioCdProject : ProjectManifestTypes.FolderProject;
        if (exportJson is not null && string.IsNullOrWhiteSpace(exportJson["ProjectId"]?.GetValue<string>()))
        {
            exportJson["ProjectId"] = main["ProjectId"]!.DeepClone();
            _store.Save(exportPath, exportJson, Options);
        }
        main["Health"] ??= JsonSerializer.SerializeToNode(new ProjectHealth());
        main["schemaVersion"] ??= 1;
        main["FormatVersion"] ??= "1";
        main["PipelineState"] ??= ProjectPipelineStateNames.Preparing;
        main["PipelineState"] = ProjectPipelineStateNames.FromManifestValue(main["PipelineState"]!.GetValue<string>()).ToManifestValue();
        main["CreatedUtc"] ??= JsonValue.Create(DateTime.UtcNow);
        main["UpdatedUtc"] = JsonValue.Create(DateTime.UtcNow);
        // Metadata migration is part of the upgrade, never a catalog side effect.
        new ProjectMetadataMigrationService().EnsureMigrated(root,
            mainName == Mp3FileName ? main.Deserialize<Mp3DiscProjectManifest>() : null,
            exportJson?.Deserialize<ExportWorkManifest>(),
            mainName == AudioFileName ? main.Deserialize<AudioDiscProjectManifest>() : null);
        if (!Directory.Exists(newMetadata) || (mainName != FolderFileName && !File.Exists(targetMain)))
            throw new IOException("Project layout migration did not complete.");
        // Publish the overall version only after all structural work succeeded.
        main["ProjectFormatVersion"] = ProjectFormat.CurrentProjectFormatVersion;
        _store.Save(targetMain, main, Options);
        return new(root, ProjectUpgradeOutcome.Upgraded, ProjectFormat.CurrentProjectFormatVersion);
    }

    private static void MergeNormalized(JsonObject original, JsonObject normalized)
    {
        foreach (var property in normalized)
            original[property.Key] = property.Value?.DeepClone();
    }

    internal static string? Existing(string root, string name)
    {
        var preferred = Path.Combine(ProjectFolderLayout.GetSettingsFolder(root), name);
        return File.Exists(preferred) ? preferred : File.Exists(Path.Combine(root, name)) ? Path.Combine(root, name) : null;
    }

    private static JsonObject Read(string path, bool validateCollections = true)
    {
        var json = File.ReadAllText(path);
        PersistentJsonSchema.Read(json, "Project manifest", path);
        var document = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Expected a JSON object.");
        if (validateCollections) ValidateCollections(document);
        return document;
    }

    private static void ValidateCollections(JsonObject document)
    {
        foreach (var key in new[] { "Tracks", "Discs", "ImportedDiscs", "SourceFiles", "History" })
        {
            if (!document.ContainsKey(key)) continue;
            if (document[key] is not JsonArray items || items.Any(item => item is not JsonObject))
                throw new JsonException($"Invalid project collection: {key}");
            foreach (var item in items) ValidateCollections(item!.AsObject());
        }
        foreach (var key in new[] { "Export", "State", "Metadata", "Resume", "Health" })
            if (document.ContainsKey(key) && document[key] is not JsonObject)
                throw new JsonException($"Invalid project object: {key}");
    }

    private static bool IsType(JsonObject document, string type) => document["ProjectType"] is JsonValue value &&
        value.TryGetValue<string>(out var stored) && string.Equals(stored, type, StringComparison.OrdinalIgnoreCase);

    private static bool IsMp3(JsonObject? value) => value is not null &&
        (IsType(value, "Mp3Disc") || IsType(value, ProjectManifestTypes.Mp3DiscProject)) &&
        (value["ImportedDiscs"] is JsonArray || value["TotalDiscs"] is JsonValue);

    private static bool IsExport(JsonObject? value) => value is not null &&
        (IsType(value, ProjectManifestTypes.FolderProject) || IsType(value, ProjectManifestTypes.Mp3DiscProject) || IsType(value, ProjectManifestTypes.AudioCdProject)) &&
        (value["Tracks"] is JsonArray || value["Export"] is JsonObject || value["State"] is JsonObject);

    private static void PreflightFile(string? source, string target)
    {
        if (source is null || !File.Exists(source) || !File.Exists(target) ||
            string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
        if (!File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(target)))
            throw new ProjectUpgradeConflictException($"Conflicting files: {source} / {target}");
    }

    private static void PreflightDirectory(string source, string target)
    {
        if (!Directory.Exists(source)) return;
        SafeFileSystemEnumeration.EnsureSafeTree(source);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            PreflightFile(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    private static void MoveFile(string? source, string target)
    {
        if (source is null || !File.Exists(source) ||
            string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) return;
        PreflightFile(source, target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target)) File.Delete(source); else File.Move(source, target);
    }

    private static void RemoveIdenticalDuplicate(string? source, string target)
    {
        if (source is null || !File.Exists(source) || !File.Exists(target) ||
            AtomicJsonStore.PathComparer.Equals(Path.GetFullPath(source), Path.GetFullPath(target))) return;
        PreflightFile(source, target);
        File.Delete(source);
    }

    private static void MoveMetadata(string source, string target)
    {
        Directory.CreateDirectory(target);
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray())
            MoveFile(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        // Empty folders may remain after an interrupted migration; remove only empty directories.
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source);
        var oldParent = Path.GetDirectoryName(source)!;
        if (!Directory.EnumerateFileSystemEntries(oldParent).Any()) Directory.Delete(oldParent);
    }
}

internal sealed class ProjectUpgradeConflictException(string message) : IOException(message);
