using BookStitch.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BookStitch.Services;

internal sealed record DeferredProjectIdentity(string Folder, string Type, string ProjectId);
internal sealed record DeferredFileRevision(bool Exists, long Length, DateTime WriteUtc, DateTime CreatedUtc);
internal sealed record DeferredCompletionResult(bool NeedsAttention, string Detail);
internal sealed class DeferredCompletionOrder
{
    public int SchemaVersion { get; set; } = 1;
    public string Operation { get; set; } = "";
    public string WorkingRoot { get; set; } = "";
    public string Environment { get; set; } = "";
    public string ProjectFolder { get; set; } = "";
    public string ProjectType { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string PrimaryRelativePath { get; set; } = "";
    public Guid RunId { get; set; }
    public string ConfirmedSessionEnd { get; set; } = "";
    public string Stage { get; set; } = "Pending";
    public Dictionary<string, DeferredFileRevision> Revisions { get; set; } = [];
    public string JournalHash { get; set; } = "";
    public Dictionary<string, string> CommitHashes { get; set; } = [];
}

/// <summary>Consumes one explicitly confirmed session-end order, never discovers projects.</summary>
internal sealed class DeferredProjectCompletionService
{
    internal const string MarkerFileName = "deferred-project-completion.json";
    private const string JournalRelativePath = "project-settings/disc-stop-save.pending.json";
    private const int MaximumMarkerBytes = 32 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly AtomicJsonStore _store;
    private readonly string _root;
    private readonly string _environment;
    private readonly Action<string, Action<string>> _saveDisc;
    private readonly Func<string, string, ProjectDeletionResult> _delete;
    internal string MarkerPath => Path.Combine(SettingsService.GetSettingsFolder(_root), MarkerFileName);

    public DeferredProjectCompletionService(string root, string environment,
        AtomicJsonStore? store = null, Func<string, string, ProjectDeletionResult>? delete = null,
        Action<string, Action<string>>? saveDisc = null)
    {
        _root = Path.GetFullPath(root);
        _environment = environment;
        _store = store ?? new();
        _saveDisc = saveDisc ?? new DiscProjectStopSaveService().Save;
        _delete = delete ?? new ProjectIndexService().DeleteProject;
    }

    // Fixed-size file metadata only. No media, manifest read, tree walk or producer wait here.
    public void RecordConfirmed(DeferredProjectIdentity identity, Guid runId, bool logoff)
    {
        ValidateLocation(identity.Folder, identity.Type);
        if (!Guid.TryParse(identity.ProjectId, out var projectId) || projectId == Guid.Empty || runId == Guid.Empty)
            throw new InvalidDataException("Deferred completion requires a verified project/run identity.");
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(MarkerPath);
        if (File.Exists(MarkerPath)) throw new IOException("A previous deferred completion still requires attention.");
        var order = new DeferredCompletionOrder
        {
            Operation = identity.Type == ProjectManifestTypes.FolderProject ? "DeferredLocalWorkDelete" : "DeferredDiscStopSave",
            WorkingRoot = _root, Environment = _environment,
            ProjectFolder = Path.GetFullPath(identity.Folder), ProjectType = identity.Type,
            ProjectId = identity.ProjectId, RunId = runId,
            PrimaryRelativePath = ResolvePrimary(identity.Folder, identity.Type),
            ConfirmedSessionEnd = logoff ? "Logoff" : "Shutdown"
        };
        foreach (var relative in RevisionPaths(order))
            order.Revisions.Add(relative, Revision(Path.Combine(order.ProjectFolder, relative)));
        if (!order.Revisions[order.PrimaryRelativePath].Exists)
            throw new IOException("Deferred completion has no primary project manifest.");
        Write(order);
    }

    public DeferredCompletionResult ProcessStartup()
    {
        if (!File.Exists(MarkerPath)) return new(false, "No deferred completion.");
        try
        {
            SafeFileSystemEnumeration.EnsureNoReparseAncestors(MarkerPath);
            if (new FileInfo(MarkerPath).Length > MaximumMarkerBytes)
                throw new InvalidDataException("Deferred completion marker exceeds its size limit.");
            var order = JsonSerializer.Deserialize<DeferredCompletionOrder>(File.ReadAllText(MarkerPath), Options)
                ?? throw new InvalidDataException("Invalid deferred completion marker.");
            ValidateOrder(order);
            if (!Directory.Exists(order.ProjectFolder))
            {
                File.Delete(MarkerPath);
                return new(false, "Deferred target is already absent.");
            }
            SafeFileSystemEnumeration.EnsureSafeTree(order.ProjectFolder);
            ValidateIdentity(order);
            var journal = Path.Combine(order.ProjectFolder, JournalRelativePath);
            var applying = order.Stage == "Applying";
            if (applying && !string.IsNullOrEmpty(order.JournalHash))
            {
                if (File.Exists(journal))
                {
                    if (HashFile(journal) != order.JournalHash) throw Stale();
                    ValidateRevisions(order, allowCommitWrites: true);
                }
                else
                {
                    ValidateRevisions(order, allowCommitWrites: true, requireCommitWrites: true);
                    File.Delete(MarkerPath); // Journal removed only after successful existing Recover.
                    return new(false, "Previously committed deferred disc completion acknowledged.");
                }
            }
            else ValidateRevisions(order, allowNewJournal: applying);

            order.Stage = "Applying";
            Write(order);
            if (order.Operation == "DeferredDiscStopSave")
                _saveDisc(order.ProjectFolder, path => RecordPreparedJournal(order, path));
            else
            {
                ValidateLocalFiles(order);
                var result = _delete(_root, order.ProjectFolder);
                if (!result.Deleted) throw new IOException(result.ErrorMessage);
            }
            // Removal is last. If it fails, the Applying marker proves the completed journal result.
            File.Delete(MarkerPath);
            return new(false, "Deferred project completion succeeded.");
        }
        catch (Exception error)
        {
            // One attempt per startup. Keep the order; no destructive fallback or retry loop.
            return new(true, error.ToString());
        }
    }

    private void RecordPreparedJournal(DeferredCompletionOrder order, string journalPath)
    {
        var journal = JsonNode.Parse(File.ReadAllText(journalPath)) ?? throw Stale();
        var writes = journal["Writes"]?.AsArray() ?? throw Stale();
        if (writes.Count is < 1 or > 16) throw Stale();
        var hashes = new Dictionary<string, string>();
        var foundIdentity = false;
        foreach (var write in writes)
        {
            var relative = write?["RelativePath"]?.GetValue<string>() ?? throw Stale();
            var document = write?["Document"] ?? throw Stale();
            var path = Inside(order.ProjectFolder, relative);
            if (!TrackPathService.IsPathInsideFolder(path, ProjectFolderLayout.GetSettingsFolder(order.ProjectFolder))) throw Stale();
            if (TrackPathService.PathEquals(path, Path.Combine(order.ProjectFolder, order.PrimaryRelativePath)))
            {
                if (document["ProjectId"]?.GetValue<string>() != order.ProjectId ||
                    !TrackPathService.PathEquals(document["ProjectFolder"]?.GetValue<string>() ?? "", order.ProjectFolder)) throw Stale();
                foundIdentity = true;
            }
            hashes.Add(NormalizeRelative(relative), Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(document, Options))));
        }
        if (!foundIdentity) throw Stale();
        order.JournalHash = HashFile(journalPath);
        order.CommitHashes = hashes;
        Write(order); // Durable proof BEFORE any journal write/delete is applied.
    }

    private void ValidateOrder(DeferredCompletionOrder order)
    {
        if (order.SchemaVersion != 1 || order.RunId == Guid.Empty || !Guid.TryParse(order.ProjectId, out var projectId) || projectId == Guid.Empty ||
            !TrackPathService.PathEquals(order.WorkingRoot, _root) || order.Environment != _environment ||
            order.ConfirmedSessionEnd is not ("Logoff" or "Shutdown") || order.Stage is not ("Pending" or "Applying") ||
            order.Operation != (order.ProjectType == ProjectManifestTypes.FolderProject ? "DeferredLocalWorkDelete" : "DeferredDiscStopSave") ||
            order.Revisions is null || order.CommitHashes is null || order.CommitHashes.Count > 16 ||
            !RevisionPaths(order).Order().SequenceEqual(order.Revisions.Keys.Order())) throw Stale();
        ValidateLocation(order.ProjectFolder, order.ProjectType);
        if (order.PrimaryRelativePath != ResolvePrimary(order.ProjectFolder, order.ProjectType) && Directory.Exists(order.ProjectFolder)) throw Stale();
        Inside(order.ProjectFolder, order.PrimaryRelativePath);
        foreach (var relative in order.CommitHashes.Keys) Inside(order.ProjectFolder, relative);
    }

    private void ValidateLocation(string folder, string type)
    {
        var layout = WorkFolderStructure.FromRoot(_root);
        var parent = type switch
        {
            ProjectManifestTypes.AudioCdProject => layout.AudioDiscProjectsFolder,
            ProjectManifestTypes.Mp3DiscProject => layout.Mp3DiscProjectsFolder,
            ProjectManifestTypes.FolderProject => layout.LocalProjectsFolder,
            _ => throw Stale()
        };
        if (!TrackPathService.IsPathInsideFolder(folder, _root) ||
            !TrackPathService.PathEquals(Path.GetDirectoryName(Path.GetFullPath(folder)) ?? "", parent)) throw Stale();
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(folder);
    }

    private static void ValidateIdentity(DeferredCompletionOrder order)
    {
        var path = Path.Combine(order.ProjectFolder, order.PrimaryRelativePath);
        var json = File.ReadAllText(path);
        PersistentJsonSchema.Read(json, "Deferred completion project", path);
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement;
        if (value.GetProperty("ProjectId").GetString() != order.ProjectId ||
            !TrackPathService.PathEquals(value.GetProperty("ProjectFolder").GetString() ?? "", order.ProjectFolder)) throw Stale();
        var type = value.GetProperty("ProjectType").GetString();
        if (type != order.ProjectType && !(order.ProjectType == ProjectManifestTypes.Mp3DiscProject && type == "Mp3Disc")) throw Stale();
        var phase = value.GetProperty("PipelineState").GetString();
        if (phase is not (ProjectPipelineStateNames.Preparing or ProjectPipelineStateNames.AcquiringSources or ProjectPipelineStateNames.Converting))
            throw Stale();
    }

    private static void ValidateRevisions(DeferredCompletionOrder order, bool allowCommitWrites = false,
        bool requireCommitWrites = false, bool allowNewJournal = false)
    {
        foreach (var pair in order.Revisions)
        {
            var path = Inside(order.ProjectFolder, pair.Key);
            if (pair.Key == JournalRelativePath && (allowCommitWrites || allowNewJournal)) continue;
            var hasCommit = order.CommitHashes.TryGetValue(pair.Key, out var hash);
            if (allowCommitWrites && hasCommit && File.Exists(path) && HashFile(path) == hash) continue;
            if ((requireCommitWrites && hasCommit) || Revision(path) != pair.Value) throw Stale();
        }
        if (requireCommitWrites && order.CommitHashes.Count == 0) throw Stale();
        // Also bind journal outputs not present in the fixed shutdown revision list.
        if (allowCommitWrites)
            foreach (var pair in order.CommitHashes.Where(pair => !order.Revisions.ContainsKey(pair.Key)))
                if (!File.Exists(Inside(order.ProjectFolder, pair.Key)) || HashFile(Inside(order.ProjectFolder, pair.Key)) != pair.Value)
                    throw Stale();
    }

    private static void ValidateLocalFiles(DeferredCompletionOrder order)
    {
        // Local deletion never follows SourceFolder. Every media file must be in the
        // managed layout and accounted for by the project's export manifest.
        var manifest = JsonSerializer.Deserialize<ExportWorkManifest>(File.ReadAllText(ProjectFolderLayout.GetExportManifestPath(order.ProjectFolder)))
            ?? throw Stale();
        if (manifest.ProjectId != order.ProjectId || !TrackPathService.PathEquals(manifest.ProjectWorkFolder, order.ProjectFolder)) throw Stale();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in manifest.Tracks)
            foreach (var path in new[] { track.SourcePath, track.ConvertedPath })
                if (!string.IsNullOrWhiteSpace(path)) allowed.Add(Inside(order.ProjectFolder, path));
        var sourceIds = manifest.Tracks.Select(track => track.SourceId).Where(id => id != Guid.Empty).ToHashSet();
        foreach (var copy in new SourceCopyIdentityStore().Load(order.ProjectFolder))
        {
            allowed.Add(Inside(order.ProjectFolder, copy.TargetRelativePath));
            if (copy.SourceId != Guid.Empty) sourceIds.Add(copy.SourceId);
        }
        var cover = new ProjectMetadataStore().LoadOrCreate(order.ProjectFolder).Cover;
        foreach (var path in new[] { cover.SourcePath, cover.ProcessedPath,
            manifest.Metadata.CoverSourcePath, manifest.Metadata.ProcessedCoverPath })
            if (!string.IsNullOrWhiteSpace(path) && TrackPathService.IsPathInsideFolder(Path.GetFullPath(Path.Combine(order.ProjectFolder, path)), order.ProjectFolder))
                allowed.Add(Inside(order.ProjectFolder, path));
        var main = new FolderProjectManifestService().Read(order.ProjectFolder) ?? throw Stale();
        var knownSettings = RevisionPaths(order).Select(relative => Inside(order.ProjectFolder, relative))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(order.ProjectFolder, "*", SearchOption.AllDirectories))
        {
            if (knownSettings.Contains(file)) continue;
            if (TrackPathService.IsPathInsideFolder(file, ProjectFolderLayout.GetSourceMetadataSourcesFolder(order.ProjectFolder)) &&
                Path.GetExtension(file) == ".json" && Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var sourceId) && sourceIds.Contains(sourceId)) continue;
            var candidate = file.EndsWith(".copying", StringComparison.OrdinalIgnoreCase) ? file[..^8] :
                file.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ? file[..^5] : file;
            if (file.EndsWith(".copying", StringComparison.OrdinalIgnoreCase) &&
                TrackPathService.IsPathInsideFolder(candidate, ProjectFolderLayout.GetOriginalsFolder(order.ProjectFolder)) &&
                !string.IsNullOrWhiteSpace(main.SourceFolder) && !TrackPathService.IsPathInsideFolder(main.SourceFolder, order.ProjectFolder) &&
                File.Exists(Path.Combine(main.SourceFolder, Path.GetRelativePath(ProjectFolderLayout.GetOriginalsFolder(order.ProjectFolder), candidate)))) continue;
            if (!allowed.Contains(candidate)) throw Stale();
        }
    }

    private void Write(DeferredCompletionOrder order)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(order, Options).Length > MaximumMarkerBytes) throw Stale();
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(MarkerPath);
        _store.Save(MarkerPath, order, Options);
    }

    private static IEnumerable<string> RevisionPaths(DeferredCompletionOrder order) => new[]
    {
        order.PrimaryRelativePath, "project-settings/export-project.json", "project-settings/project-metadata.json",
        "project-settings/track-list-state.json", "project-settings/source-copy-identities.json", JournalRelativePath
    }.Distinct();
    private static string ResolvePrimary(string folder, string type) => NormalizeRelative(Path.GetRelativePath(folder, type switch
    {
        ProjectManifestTypes.AudioCdProject => ProjectFolderLayout.ResolveAudioDiscManifestPath(folder),
        ProjectManifestTypes.Mp3DiscProject => ProjectFolderLayout.ResolveMp3DiscManifestPath(folder),
        ProjectManifestTypes.FolderProject => ProjectFolderLayout.GetFolderManifestPath(folder),
        _ => throw Stale()
    }));
    private static DeferredFileRevision Revision(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? new(true, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc) : new(false, 0, default, default);
    }
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string NormalizeRelative(string path) => path.Replace('\\', '/');
    private static string Inside(string folder, string path)
    {
        var full = Path.GetFullPath(Path.Combine(folder, path));
        if (!TrackPathService.IsPathInsideFolder(full, folder)) throw Stale();
        return full;
    }
    private static InvalidDataException Stale() => new("Deferred completion cannot safely validate the project, revision, or ownership; no automatic cleanup authorized.");
}
