using BookStitch.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;

namespace BookStitch.Services;

/// <summary>Commits a disc boundary after all producers have stopped. A durable redo
/// journal makes metadata updates and cleanup repeatable after an interrupted commit.</summary>
public sealed class DiscProjectStopSaveService
{
    private const string JournalName = "disc-stop-save.pending.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly AtomicJsonStore _store = new();
    private static readonly ConcurrentDictionary<string, object> ProjectLocks = new(StringComparer.OrdinalIgnoreCase);
    public bool HasPendingCommit(string projectFolder) => !string.IsNullOrWhiteSpace(projectFolder) && File.Exists(JournalPath(projectFolder));

    internal static bool CanOfferImportSave(ProjectPipelineState phase, bool initialImport, bool extension, bool reconversion)
        => initialImport && !extension && !reconversion &&
            phase is ProjectPipelineState.AcquiringSources or ProjectPipelineState.Converting;

    public bool CanSave(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            return false;
        try
        {
            if (File.Exists(ProjectExtensionRollbackService.JournalPath(projectFolder)) ||
                Read<ExportWorkManifest>(ProjectFolderLayout.ResolveExportManifestPath(projectFolder))?.Resume.MergePaused == true) return false;
            if (HasPendingCommit(projectFolder)) return true;
            var audio = Read<AudioDiscProjectManifest>(ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder));
            if (audio is not null)
                return audio.TotalDiscs >= 1;
            var mp3 = Read<Mp3DiscProjectManifest>(ProjectFolderLayout.ResolveMp3DiscManifestPath(projectFolder));
            return mp3?.ProjectType.Contains("Mp3Disc", StringComparison.OrdinalIgnoreCase) == true &&
                mp3.TotalDiscs >= 1;
        }
        catch (IOException) { return false; }
        catch (InvalidDataException) { return false; }
        catch (JsonException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (PersistentJsonSchemaException) { return false; }
    }

    public void Save(string projectFolder)
    {
        lock (ProjectLocks.GetOrAdd(Path.GetFullPath(projectFolder), _ => new object()))
            SaveCore(projectFolder, null);
    }

    internal void Save(string projectFolder, Action<string> journalPrepared)
    {
        lock (ProjectLocks.GetOrAdd(Path.GetFullPath(projectFolder), _ => new object()))
            SaveCore(projectFolder, journalPrepared);
    }

    private void SaveCore(string projectFolder, Action<string>? journalPrepared)
    {
        var root = Path.GetFullPath(projectFolder);
        EnsureSafeTree(root);
        if (File.Exists(JournalPath(root)))
        {
            journalPrepared?.Invoke(JournalPath(root));
            Recover(root);
            return;
        }
        if (File.Exists(ProjectExtensionRollbackService.JournalPath(root)) ||
            Read<ExportWorkManifest>(ProjectFolderLayout.ResolveExportManifestPath(root))?.Resume.MergePaused == true)
            throw new InvalidOperationException(LocalizationText.Get("Disc.StopSave.Unavailable"));
        var originalAudio = Read<AudioDiscProjectManifest>(ProjectFolderLayout.ResolveAudioDiscManifestPath(root));
        if (originalAudio is not null)
            foreach (var track in originalAudio.Discs.SelectMany(disc => disc.Tracks))
                if (!string.IsNullOrWhiteSpace(track.RelativePath))
                    Inside(root, Path.Combine(root, track.RelativePath));

        var audioPath = ProjectFolderLayout.ResolveAudioDiscManifestPath(root);
        var mp3Path = ProjectFolderLayout.ResolveMp3DiscManifestPath(root);
        var audio = Read<AudioDiscProjectManifest>(audioPath);
        var mp3 = audio is null ? Read<Mp3DiscProjectManifest>(mp3Path) : null;
        var keptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keptIds = new HashSet<Guid>();
        var completedDiscs = new HashSet<int>();
        var writes = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
        string preset;
        string pipelineState;

        if (audio is not null)
        {
            if (audio.TotalDiscs < 1)
                throw new InvalidOperationException(LocalizationText.Get("Disc.StopSave.Unavailable"));
            audio.Discs = audio.Discs.Where(IsComplete).ToList();
            foreach (var disc in audio.Discs)
            {
                completedDiscs.Add(disc.DiscNumber);
                foreach (var track in disc.Tracks)
                {
                    var path = Inside(ProjectFolderLayout.GetOriginalsFolder(root), Path.Combine(root, track.RelativePath));
                    RequireFile(path, track.OutputSizeBytes);
                    keptFiles.Add(path);
                    keptIds.Add(track.SourceId);
                }
            }
            audio.IsStoppedForResume = true;
            audio.ErrorMessage = "";
            audio.ExportErrorMessage = "";
            audio.ExportCompletedUtc = null;
            audio.ExportStatus = AudioDiscExportStatus.NotStarted;
            var complete = new AudioDiscProjectService().GetNextRequiredDiscNumber(audio) is null;
            audio.Status = complete ? AudioDiscProjectStatus.RippingCompleted : AudioDiscProjectStatus.WaitingForDisc;
            audio.PipelineState = pipelineState = complete ? ProjectPipelineStateNames.Converting : ProjectPipelineStateNames.AcquiringSources;
            audio.UpdatedUtc = DateTime.UtcNow;
            audio.RipDuration = audio.Discs.Aggregate(TimeSpan.Zero, (sum, disc) => sum + (disc.RipDuration ?? TimeSpan.Zero));
            preset = audio.ExportPreset;
            writes[audioPath] = JsonSerializer.SerializeToNode(audio, Options)!;
        }
        else if (mp3?.ProjectType.Contains("Mp3Disc", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (mp3.TotalDiscs < 1)
                throw new InvalidOperationException(LocalizationText.Get("Disc.StopSave.Unavailable"));
            mp3.ImportedDiscs = mp3.ImportedDiscs.Where(IsComplete).ToList();
            foreach (var disc in mp3.ImportedDiscs)
            {
                completedDiscs.Add(disc.DiscNumber);
                var folder = Inside(root, ProjectFolderLayout.ResolveDiscOriginalsFolder(root, disc.DiscNumber));
                var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(file => !IsPartial(file)).ToArray();
                if (files.Length != disc.FileCount || files.Length == 0)
                    throw new IOException(LocalizationText.Get("Disc.StopSave.MissingSources"));
                foreach (var file in files)
                {
                    RequireFile(file);
                    keptFiles.Add(Path.GetFullPath(file));
                }
                foreach (var source in disc.SourceFiles)
                {
                    RequireFile(Inside(root, Path.Combine(ProjectFolderLayout.GetOriginalsFolder(root), source.RelativePath)), source.SourceSizeBytes);
                    keptIds.Add(source.SourceId);
                }
            }
            mp3.IsStoppedForResume = true;
            mp3.PipelineState = pipelineState = Enumerable.Range(1, mp3.TotalDiscs).All(completedDiscs.Contains)
                ? ProjectPipelineStateNames.Converting : ProjectPipelineStateNames.AcquiringSources;
            mp3.UpdatedUtc = DateTime.UtcNow;
            preset = mp3.ExportPreset;
            writes[mp3Path] = JsonSerializer.SerializeToNode(mp3, Options)!;
        }
        else
            throw new InvalidOperationException(LocalizationText.Get("Disc.StopSave.Unavailable"));

        if (completedDiscs.Count != (audio?.Discs.Count ?? mp3!.ImportedDiscs.Count) ||
            completedDiscs.Any(number => number < 1 || number > (audio?.TotalDiscs ?? mp3!.TotalDiscs)))
            throw new InvalidDataException(LocalizationText.Get("Disc.StopSave.InvalidState"));

        if (string.IsNullOrWhiteSpace(preset))
            throw new InvalidDataException(LocalizationText.Get("Disc.StopSave.MissingPreset"));

        var keptConverted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parsedPreset = ExportPreset.Parse(preset);
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(root, parsedPreset.GetFolderName());
        // MP3 live preparation can finish before an export manifest is created.
        // Use the same path and file-reuse contract as the normal disc pipeline.
        foreach (var file in keptFiles)
        {
            var track = new TrackInfo { FilePath = file, FileName = Path.GetFileName(file) };
            var converted = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, file, track);
            if (PreparedConvertedTrackReuseService.CanReuseForDiscProject(
                    audio is null ? ProjectManifestTypes.Mp3DiscProject : ProjectManifestTypes.AudioCdProject, file, converted))
                keptConverted.Add(Inside(ProjectFolderLayout.GetConvertedFolder(root), converted));
        }
        foreach (var manifestPath in new[] { mp3Path, ProjectFolderLayout.ResolveExportManifestPath(root) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (writes.ContainsKey(manifestPath) || !File.Exists(manifestPath))
                continue;
            var manifest = Read<ExportWorkManifest>(manifestPath)!;
            manifest.Tracks = manifest.Tracks.Where(track => keptFiles.Contains(Path.GetFullPath(track.SourcePath))).ToList();
            foreach (var track in manifest.Tracks)
            {
                keptIds.Add(track.SourceId);
                var reusable = string.Equals(track.Status, ProjectManifestTrackStatuses.Converted, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(track.Preset, preset, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(track.ConvertedPath) && !IsPartial(track.ConvertedPath) &&
                    File.Exists(track.ConvertedPath) && new FileInfo(track.ConvertedPath).Length > 0 &&
                    (track.ConvertedSizeBytes <= 0 || new FileInfo(track.ConvertedPath).Length == track.ConvertedSizeBytes);
                if (reusable)
                    keptConverted.Add(Inside(ProjectFolderLayout.GetConvertedFolder(root), track.ConvertedPath));
                else
                {
                    track.Status = ProjectManifestTrackStatuses.Pending;
                    track.ConvertedPath = "";
                    track.ConvertedSizeBytes = 0;
                    track.ConvertedLastWriteUtcTicks = 0;
                    track.StartedUtc = null;
                    track.CompletedUtc = null;
                    track.LastError = "";
                }
            }
            var indexes = manifest.Tracks.Select(track => track.TrackIndex).ToHashSet();
            manifest.Discs = manifest.Discs.Where(disc => completedDiscs.Contains(disc.DiscIndex)).ToList();
            manifest.History.RemoveAll(item => item.DiscIndex.HasValue && !completedDiscs.Contains(item.DiscIndex.Value) ||
                item.TrackIndex.HasValue && !indexes.Contains(item.TrackIndex.Value));
            manifest.SelectedPreset = manifest.Export.SelectedPreset = preset;
            manifest.State.Status = pipelineState;
            manifest.State.LastStartedTrackIndex = 0;
            manifest.State.LastCompletedTrackIndex = manifest.Tracks.Where(track => track.Status == ProjectManifestTrackStatuses.Converted)
                .Select(track => track.TrackIndex).DefaultIfEmpty(0).Max();
            manifest.State.CancelRequestedUtc = null;
            manifest.State.LastErrorSummary = "";
            manifest.State.LastErrorUtc = null;
            manifest.Resume.CanResume = true;
            manifest.Resume.Reason = "DiscBoundarySaved";
            manifest.UpdatedUtc = DateTime.UtcNow;
            writes[manifestPath] = JsonSerializer.SerializeToNode(manifest, Options)!;
        }

        foreach (var identity in new SourceCopyIdentityStore().Load(root))
            if (keptFiles.Contains(Path.GetFullPath(Path.Combine(root, identity.TargetRelativePath))))
                keptIds.Add(identity.SourceId);
        keptIds.Remove(Guid.Empty);
        var keptKeys = keptFiles.Select(file => "path:" + file.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (audio is not null)
            foreach (var track in audio.Discs.SelectMany(disc => disc.Tracks))
                keptKeys.Add($"disc:{track.DiscNumber}:track:{track.TrackNumber}:file:{track.FileName}");
        PruneState(ProjectFolderLayout.ResolveTrackListStatePath(root), "Tracks", item =>
            Guid.TryParse(item["SourceId"]?.ToString(), out var id) && keptIds.Contains(id) ||
            keptKeys.Contains(item["Key"]?.ToString() ?? ""), writes, TrackListStateService.CurrentSchemaVersion);
        PruneState(Path.Combine(ProjectFolderLayout.GetSettingsFolder(root), SourceCopyIdentityStore.FileName), "Files", item =>
            keptFiles.Contains(Path.GetFullPath(Path.Combine(root, item["TargetRelativePath"]?.ToString() ?? ""))), writes);

        var deletes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectDiscarded(ProjectFolderLayout.ResolveOriginalsFolder(root), keptFiles, deletes);
        CollectDiscarded(ProjectFolderLayout.GetConvertedFolder(root), keptConverted, deletes);
        CollectDiscarded(ProjectFolderLayout.GetMergeFolder(root), new HashSet<string>(), deletes);
        var sourcesFolder = ProjectFolderLayout.GetSourceMetadataSourcesFolder(root);
        if (Directory.Exists(sourcesFolder))
            foreach (var file in Directory.GetFiles(sourcesFolder, "*.json"))
                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id) || !keptIds.Contains(id))
                    deletes.Add(file);

        var journal = new StopSaveJournal
        {
            Writes = writes.Select(pair => new StopSaveWrite { RelativePath = Path.GetRelativePath(root, pair.Key), Document = pair.Value }).ToList(),
            DeleteFiles = deletes.Select(file => Path.GetRelativePath(root, file)).ToList()
        };
        _store.Save(JournalPath(root), journal, Options);
        journalPrepared?.Invoke(JournalPath(root));
        Recover(root);
    }

    public void Recover(string projectFolder)
    {
        lock (ProjectLocks.GetOrAdd(Path.GetFullPath(projectFolder), _ => new object()))
            RecoverCore(projectFolder);
    }

    private void RecoverCore(string projectFolder)
    {
        var root = Path.GetFullPath(projectFolder);
        var path = JournalPath(root);
        if (!File.Exists(path))
            return;
        EnsureSafeTree(root);
        var journal = Read<StopSaveJournal>(path) ?? throw new InvalidDataException(LocalizationText.Get("Disc.StopSave.InvalidState"));
        if (journal.SchemaVersion != 1 || journal.Writes.Count == 0)
            throw new InvalidDataException(LocalizationText.Get("Disc.StopSave.InvalidState"));
        // Validate every path before the first mutation, including forged journals.
        foreach (var write in journal.Writes)
        {
            Inside(ProjectFolderLayout.GetSettingsFolder(root), Path.Combine(root, write.RelativePath));
            if (write.Document is null) throw new InvalidDataException(LocalizationText.Get("Disc.StopSave.InvalidState"));
        }
        foreach (var file in journal.DeleteFiles)
        {
            var target = Inside(root, Path.Combine(root, file));
            var allowed = new[] { ProjectFolderLayout.GetOriginalsFolder(root), ProjectFolderLayout.GetConvertedFolder(root),
                ProjectFolderLayout.GetMergeFolder(root), ProjectFolderLayout.GetSourceMetadataSourcesFolder(root) };
            if (!allowed.Any(folder => IsInside(folder, target)))
                throw new IOException(LocalizationText.Get("Disc.StopSave.InvalidState"));
        }
        foreach (var write in journal.Writes)
            _store.Save(Path.Combine(root, write.RelativePath), write.Document, Options);
        foreach (var file in journal.DeleteFiles)
        {
            var target = Path.Combine(root, file);
            if (File.Exists(target))
            {
                // File.Copy preserves the read-only flag from real CD media. Only
                // clear it on validated project copies scheduled for deletion.
                var attributes = File.GetAttributes(target);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
                File.Delete(target);
            }
        }
        foreach (var folder in new[] { ProjectFolderLayout.GetOriginalsFolder(root), ProjectFolderLayout.GetConvertedFolder(root), ProjectFolderLayout.GetMergeFolder(root) })
            if (Directory.Exists(folder))
                foreach (var directory in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(value => value.Length))
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        File.Delete(path);
    }

    public static void ValidateResumeSources(ProjectResumePlan plan)
    {
        if (!plan.IsStoppedDiscProject) return;
        if (string.IsNullOrWhiteSpace(plan.SelectedPreset))
            throw new InvalidDataException(LocalizationText.Get("Disc.StopSave.MissingPreset"));
        EnsureSafeTree(plan.ProjectFolder);
        if (plan.Tracks.Count == 0 && !(plan.ImportedDiscCount == 0 &&
            plan.CanContinueDiscImport && plan.NextMissingDiscNumber == 1))
            throw new InvalidDataException(LocalizationText.Get("Disc.StopSave.MissingSources"));
        foreach (var track in plan.Tracks)
        {
            var source = Inside(plan.ProjectFolder, track.SourcePath);
            // Recovery may retain pending track rows for reacquisition; completed claims are checked below.
            if (!plan.CanContinueDiscImport) RequireFile(source);
        }
        var audio = Read<AudioDiscProjectManifest>(ProjectFolderLayout.ResolveAudioDiscManifestPath(plan.ProjectFolder));
        if (audio is not null)
        {
            foreach (var track in audio.Discs.SelectMany(disc => disc.Tracks)
                         .Where(track => string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase)))
                RequireFile(Inside(plan.ProjectFolder, Path.Combine(plan.ProjectFolder, track.RelativePath)), track.OutputSizeBytes);
        }
        else
        {
            var mp3 = Read<Mp3DiscProjectManifest>(ProjectFolderLayout.ResolveMp3DiscManifestPath(plan.ProjectFolder));
            if (mp3 is null) throw new InvalidDataException(LocalizationText.Get("Disc.StopSave.MissingSources"));
            foreach (var disc in mp3.ImportedDiscs.Where(disc =>
                         string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase)))
            {
                var folder = Inside(plan.ProjectFolder, ProjectFolderLayout.ResolveDiscOriginalsFolder(plan.ProjectFolder, disc.DiscNumber));
                if (!Directory.Exists(folder) || Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Count(file => !IsPartial(file)) != disc.FileCount)
                    throw new IOException(LocalizationText.Get("Disc.StopSave.MissingSources"));
                foreach (var source in disc.SourceFiles)
                    RequireFile(Inside(plan.ProjectFolder, Path.Combine(ProjectFolderLayout.GetOriginalsFolder(plan.ProjectFolder), source.RelativePath)), source.SourceSizeBytes);
            }
        }
    }

    private static bool IsComplete(AudioDiscProjectManifestDisc disc) => disc.TrackCount > 0 &&
        disc.Tracks.Count == disc.TrackCount && disc.Tracks.All(track => string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase));
    private static bool IsComplete(Mp3DiscProjectManifestDisc disc) => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase) &&
        disc.FileCount > 0 && disc.CopiedFiles == disc.FileCount;
    private static T? Read<T>(string path)
    {
        if (!File.Exists(path)) return default;
        var json = File.ReadAllText(path);
        PersistentJsonSchema.Read(json, "Disc stop-save", path, 1);
        var result = JsonSerializer.Deserialize<T>(json, Options);
        var invalid = result switch
        {
            AudioDiscProjectManifest audio => audio.Discs is null || audio.Discs.Any(disc => disc is null || disc.Tracks is null || disc.Tracks.Any(track => track is null)),
            Mp3DiscProjectManifest mp3 => mp3.ImportedDiscs is null || mp3.ImportedDiscs.Any(disc => disc is null || disc.SourceFiles is null || disc.SourceFiles.Any(file => file is null)),
            ExportWorkManifest export => export.Tracks is null || export.Tracks.Any(track => track is null || string.IsNullOrWhiteSpace(track.SourcePath)) ||
                export.Discs is null || export.Discs.Any(disc => disc is null) || export.History is null || export.History.Any(item => item is null) ||
                export.State is null || export.Export is null || export.Resume is null,
            StopSaveJournal journal => journal.Writes is null || journal.DeleteFiles is null || journal.DeleteFiles.Any(string.IsNullOrWhiteSpace) ||
                journal.Writes.Any(write => write is null || string.IsNullOrWhiteSpace(write.RelativePath) || write.Document is null),
            _ => false
        };
        if (invalid) throw new InvalidDataException(LocalizationText.Get("Disc.StopSave.InvalidState"));
        return result;
    }
    private static string JournalPath(string root) => Path.Combine(ProjectFolderLayout.GetSettingsFolder(root), JournalName);
    private static bool IsPartial(string path) => path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".copying", StringComparison.OrdinalIgnoreCase);
    private static void RequireFile(string path, long? size = null)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || size is > 0 && file.Length != size)
            throw new IOException(LocalizationText.Get("Disc.StopSave.MissingSources"));
    }
    private static void CollectDiscarded(string folder, IReadOnlySet<string> keep, ISet<string> deletes)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            if (!keep.Contains(Path.GetFullPath(file))) deletes.Add(file);
    }
    private static void PruneState(string path, string arrayName, Func<JsonNode, bool> keep, IDictionary<string, JsonNode> writes, int schemaVersion = 1)
    {
        if (!File.Exists(path)) return;
        var node = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException(path);
        PersistentJsonSchema.Read(node.ToJsonString(), "Disc stop-save state", path, schemaVersion, allowLegacyArray: true);
        var array = node as JsonArray ?? node[arrayName] as JsonArray;
        if (array is null) throw new InvalidDataException(path);
        for (var index = array.Count - 1; index >= 0; index--)
            if (array[index] is not { } item || !keep(item)) array.RemoveAt(index);
        writes[path] = node;
    }
    private static string Inside(string root, string path)
    {
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(path);
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException(LocalizationText.Get("Disc.StopSave.InvalidState"));
        return full;
    }
    private static bool IsInside(string root, string path) => Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void EnsureSafeTree(string root) =>
        SafeFileSystemEnumeration.EnsureSafeTree(root);

    private sealed class StopSaveJournal
    {
        [System.Text.Json.Serialization.JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;
        public List<StopSaveWrite> Writes { get; set; } = [];
        public List<string> DeleteFiles { get; set; } = [];
    }
    private sealed class StopSaveWrite
    {
        public string RelativePath { get; set; } = "";
        public JsonNode Document { get; set; } = null!;
    }
}
