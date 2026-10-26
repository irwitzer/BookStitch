using BookStitch.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace BookStitch.Services;

internal sealed record IntegritySource(Guid Id, string Path, long Size, long LastWrite, string? Hash);
internal sealed class IntegrityProject
{
    public required string Root { get; init; }
    public required string MainPath { get; init; }
    public required JsonObject Main { get; init; }
    public JsonObject? Export { get; set; }
    public List<IntegritySource> Sources { get; } = [];
    public Dictionary<string, AudioProbeInfo?> Probes { get; } = new(AtomicJsonStore.PathComparer);
    public Dictionary<string, (long Size,long Ticks,string Hash)> Hashes { get; } = new(AtomicJsonStore.PathComparer);
    public List<IntegrityFinding> Findings { get; } = [];
    public HashSet<string> KnownFiles { get; } = new(AtomicJsonStore.PathComparer);
    public JsonObject? CopyIdentities { get; set; }
    public bool Safe { get; set; } = true;
    public List<string> RecoveryBackups { get; } = [];
}

/// <summary>Read-only inspection. All repairs and persistence belong to the repair/inspection owners.</summary>
public sealed class ProjectIntegrityValidator
{
    private readonly AudioProbeBatchService _probes;
    public ProjectIntegrityValidator(AudioProbeBatchService probes) => _probes = probes;
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static string Text(JsonNode? node, string name) => node?[name]?.ToString() ?? "";
    internal static long Number(JsonNode? node, string name) => long.TryParse(Text(node, name), out var n) ? n : 0;
    internal static Guid Id(JsonNode? node) => Guid.TryParse(Text(node, "SourceId"), out var id) ? id : Guid.Empty;
    internal static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new JsonException("Expected a JSON object: " + path);
    internal static IEnumerable<JsonObject> Rows(JsonNode? node, string name) =>
        (node?[name] as JsonArray ?? []).OfType<JsonObject>();
    internal static void Add(IntegrityProject project, IntegrityIssueCode code, ProjectHealthStatus health,
        RepairActionCode repair, string detail, string? path = null, Guid? source = null) =>
        project.Findings.Add(new(code, health, repair, detail, path, source));
    internal static string Resolve(IntegrityProject p, string path, string managedFolder)
    {
        return ProjectManagedPathService.Resolve(p.Root,path,managedFolder,
            [Text(p.Main,"ProjectFolder"),Text(p.Export,"ProjectWorkFolder")]);
    }

    internal async Task<IntegrityProject> ValidateAsync(string folder, ProjectIntegrityOptions options,
        IProgress<IntegrityProgress>? progress, CancellationToken token, IntegrityProject? previous = null)
    {
        token.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(folder);
        var mains = new[] { ProjectFolderLayout.GetFolderManifestPath(root), ProjectFolderLayout.GetMp3DiscManifestPath(root),
            ProjectFolderLayout.GetAudioDiscManifestPath(root) }.Where(File.Exists).ToArray();
        if (mains.Length != 1) throw new InvalidDataException("Exactly one main manifest is required.");
        var p = new IntegrityProject { Root = root, MainPath = mains[0], Main = Read(mains[0]) };
        if (previous is not null)
        {
            p.RecoveryBackups.AddRange(previous.RecoveryBackups);
            foreach (var pair in previous.Probes) p.Probes[pair.Key] = pair.Value;
            foreach (var pair in previous.Hashes) p.Hashes[pair.Key] = pair.Value;
        }
        try { SafeFileSystemEnumeration.EnsureSafeTree(root, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            p.Safe = false;
            Add(p, ex is ProjectPathSafetyException ? IntegrityIssueCode.UnsafeReparsePoint : ex is ProjectStaleManagedPathException ? IntegrityIssueCode.StaleManagedPath : IntegrityIssueCode.ManifestCorrupt, ProjectHealthStatus.RebuildRequired, RepairActionCode.None, ex.Message);
            return p;
        }
        progress?.Report(new("Integrity.Phase.Structure", root));
        var type = Text(p.Main, "ProjectType");
        var expectedType = Path.GetFileName(p.MainPath) == ProjectFolderLayout.FolderManifestFileName ? ProjectManifestTypes.FolderProject
            : Path.GetFileName(p.MainPath) == ProjectFolderLayout.Mp3DiscManifestFileName ? ProjectManifestTypes.Mp3DiscProject : ProjectManifestTypes.AudioCdProject;
        if (type != expectedType) Add(p, IntegrityIssueCode.ProjectTypeConflict, ProjectHealthStatus.RebuildRequired, RepairActionCode.None, "Main filename and project type disagree.");
        if (string.IsNullOrWhiteSpace(Text(p.Main, "ProjectId"))) Add(p, IntegrityIssueCode.ProjectIdConflict, ProjectHealthStatus.RebuildRequired, RepairActionCode.None, "Main identity is missing.");
        CheckSchema(p, p.MainPath, p.Main, 1);
        if (Number(p.Main, "ProjectFormatVersion") != ProjectFormat.CurrentProjectFormatVersion)
            Add(p, IntegrityIssueCode.UnsupportedSchema, ProjectHealthStatus.RebuildRequired, RepairActionCode.None, "Project format is not current.");
        foreach (var directory in new[] { "originals", "converted", "merge", "project-settings/source-metadata" })
            if (!Directory.Exists(Path.Combine(root, directory)))
                Add(p, IntegrityIssueCode.StandardDirectoryMissing, ProjectHealthStatus.Warning, RepairActionCode.CreateDirectory, "Standard directory is missing.", Path.Combine(root, directory));
        var expectedFormat = type == ProjectManifestTypes.FolderProject ? "1" : type == ProjectManifestTypes.Mp3DiscProject ? Mp3DiscManifestVersions.Current : AudioDiscProjectManifestVersions.Current;
        if (Text(p.Main,"FormatVersion") != expectedFormat)
            Add(p,IntegrityIssueCode.UnsupportedSchema,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"Unsupported main format version.");
        if (type == ProjectManifestTypes.Mp3DiscProject && p.Main["ImportedDiscs"] is not JsonArray ||
            type == ProjectManifestTypes.AudioCdProject && p.Main["Discs"] is not JsonArray)
            Add(p,IntegrityIssueCode.ManifestCorrupt,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"Acquisition collections are missing.");
        p.KnownFiles.Add(p.MainPath);
        var exportPath = ProjectFolderLayout.GetExportManifestPath(root);
        p.KnownFiles.Add(exportPath);
        if (File.Exists(exportPath))
        {
            try
            {
                p.Export = Read(exportPath);
                if(p.Export["Tracks"] is not JsonArray || p.Export["Export"] is not JsonObject || p.Export["State"] is not JsonObject)
                    throw new JsonException("Required export manifest collections are missing.");
                // Timing is independently reconstructable; validate the critical document without it.
                var critical=(JsonObject)p.Export.DeepClone();
                foreach(var row in Rows(critical,"Tracks"))row["AacTiming"]=null;
                _=critical.Deserialize<ExportWorkManifest>() ?? throw new JsonException("Invalid export manifest.");
                CheckSchema(p, exportPath, p.Export, ExportWorkManifestVersions.CurrentSchemaVersion);
                if(Text(p.Export,"FormatVersion")!=ExportWorkManifestVersions.Current)
                    Add(p,IntegrityIssueCode.UnsupportedSchema,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"Unsupported export format version.",exportPath);
                if (Text(p.Export, "ProjectId") != Text(p.Main, "ProjectId")) Add(p, IntegrityIssueCode.ProjectIdConflict, ProjectHealthStatus.RebuildRequired, RepairActionCode.None, "Main/export identities disagree.", exportPath);
                if (Text(p.Export, "ProjectType") != type) Add(p, IntegrityIssueCode.ProjectTypeConflict, ProjectHealthStatus.RebuildRequired, RepairActionCode.None, "Main/export project types disagree.", exportPath);
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
            { Add(p, IntegrityIssueCode.ManifestCorrupt, ProjectHealthStatus.RebuildRequired, RepairActionCode.None, ex.Message, exportPath); }
        }
        else if (Text(p.Main,"PipelineState") is "Completed" or "ReviewBeforeMerge")
            Add(p, IntegrityIssueCode.ManifestCorrupt, ProjectHealthStatus.RebuildRequired, RepairActionCode.None, "Review/complete export manifest is missing.", exportPath);
        foreach (var disc in Rows(p.Main, "ImportedDiscs")) foreach (var row in Rows(disc,"SourceFiles"))
            RegisterSource(p,row,Text(row,"RelativePath"),Number(row,"SourceSizeBytes"),Number(row,"SourceLastWriteUtcTicks"),null,
                ProjectFolderLayout.GetOriginalsFolder(root));
        foreach (var disc in Rows(p.Main, "Discs")) foreach (var row in Rows(disc,"Tracks").Where(r => Text(r,"Status") == "Ripped"))
            RegisterSource(p,row,Text(row,"RelativePath"),Number(row,"OutputSizeBytes"),0,Text(row,"ContentSha256"));
        foreach(var row in Rows(p.Export,"Tracks"))
        {
            if(type!=ProjectManifestTypes.FolderProject)
            {
                try
                {
                    var path=Resolve(p,Text(row,"SourcePath"),"originals");
                    if(!File.Exists(path)&&!p.Sources.Any(source=>AtomicJsonStore.PathComparer.Equals(source.Path,path)))continue;
                }
                catch(IOException ex){Add(p,ex is ProjectPathSafetyException ? IntegrityIssueCode.UnsafeReparsePoint : ex is ProjectStaleManagedPathException ? IntegrityIssueCode.StaleManagedPath : IntegrityIssueCode.ManifestCorrupt,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,ex.Message);continue;}
            }
            RegisterSource(p,row,Text(row,"SourcePath"),Number(row,"SourceSizeBytes"),Number(row,"SourceLastWriteUtcTicks"),null);
        }
        var identitiesPath = Path.Combine(ProjectFolderLayout.GetSettingsFolder(root),SourceCopyIdentityStore.FileName);
        if (type != ProjectManifestTypes.AudioCdProject && (p.Sources.Count > 0 || File.Exists(identitiesPath)))
        {
            p.KnownFiles.Add(identitiesPath);
            try
            {
                if (!File.Exists(identitiesPath)) throw new FileNotFoundException("Copy identities are missing.");
                p.CopyIdentities = Read(identitiesPath); CheckSchema(p,identitiesPath,p.CopyIdentities,1);
                if (p.CopyIdentities["Files"] is not JsonArray) throw new JsonException("Copy identity entries are missing.");
                foreach (var row in Rows(p.CopyIdentities,"Files"))
                {
                    string path;
                    try { path=Resolve(p,Text(row,"TargetRelativePath"),"originals"); }
                    catch(IOException ex)
                    { Add(p,ex is ProjectPathSafetyException ? IntegrityIssueCode.UnsafeReparsePoint : ex is ProjectStaleManagedPathException ? IntegrityIssueCode.StaleManagedPath : IntegrityIssueCode.ManifestCorrupt,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,ex.Message,identitiesPath);continue; }
                    var source = p.Sources.FirstOrDefault(s => AtomicJsonStore.PathComparer.Equals(s.Path,path));
                    if (source is null && Id(row) != Guid.Empty && Number(row,"SourceSizeBytes") > 0)
                    {
                        RegisterSource(p,row,path,Number(row,"SourceSizeBytes"),Number(row,"SourceLastWriteUtcTicks"),Text(row,"ContentSha256"));
                        source=p.Sources.FirstOrDefault(s=>AtomicJsonStore.PathComparer.Equals(s.Path,path));
                    }
                    if (source is null || Id(row) != source.Id || Number(row,"SourceSizeBytes") != source.Size)
                        Add(p,IntegrityIssueCode.ManifestConflict,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"Copy identity conflicts with acquired source.",path);
                    else { var index = p.Sources.IndexOf(source); p.Sources[index] = source with { Hash = Text(row,"ContentSha256") }; }
                }
                foreach(var source in p.Sources)
                    if(!Rows(p.CopyIdentities,"Files").Any(r=>Id(r)==source.Id))
                        Add(p,IntegrityIssueCode.SourceCopyIdentityMissing,ProjectHealthStatus.Warning,RepairActionCode.RebuildCopyIdentity,"Copy identity is missing for an acquired source.",identitiesPath,source.Id);
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
            { p.CopyIdentities=null; Add(p,File.Exists(identitiesPath)?IntegrityIssueCode.SourceCopyIdentityCorrupt:IntegrityIssueCode.SourceCopyIdentityMissing,ProjectHealthStatus.Warning,RepairActionCode.RebuildCopyIdentity,ex.Message,identitiesPath); }
        }
        progress?.Report(new("Integrity.Phase.Sources",root));
        using(options.Performance?.Phase("hash"))
        foreach (var source in p.Sources)
        {
            token.ThrowIfCancellationRequested();
            p.KnownFiles.Add(source.Path);
            try
            {
                var file = new FileInfo(source.Path);
                if (!file.Exists) { Add(p,IntegrityIssueCode.SourceMissing,ProjectHealthStatus.Damaged,RepairActionCode.None,"Acquired original is missing.",source.Path,source.Id); continue; }
                if(file.Length==0){ Add(p,IntegrityIssueCode.SourceZeroLength,ProjectHealthStatus.Damaged,RepairActionCode.None,"Original is empty.",source.Path,source.Id);continue; }
                if(source.Size > 0 && file.Length!=source.Size){ Add(p,IntegrityIssueCode.SourceSizeMismatch,ProjectHealthStatus.Damaged,RepairActionCode.None,"Original size differs from acquisition evidence.",source.Path,source.Id);continue; }
                var changed = source.LastWrite>0 && source.LastWrite!=file.LastWriteTimeUtc.Ticks;
                if(!string.IsNullOrEmpty(source.Hash) && (options.FullContentHashes||changed))
                {
                    var hash=await HashWithCacheAsync(p,source.Path,token,options.Performance).ConfigureAwait(false);
                    if(!hash.Equals(source.Hash,StringComparison.OrdinalIgnoreCase)) Add(p,IntegrityIssueCode.SourceHashMismatch,ProjectHealthStatus.Damaged,RepairActionCode.None,"Original hash differs from its stored reference.",source.Path,source.Id);
                    else if(changed) Add(p,IntegrityIssueCode.SourceMetadataMismatch,ProjectHealthStatus.Warning,RepairActionCode.UpdateSourceEvidence,"Timestamp changed; existing reference hash confirms identical content.",source.Path,source.Id);
                }
                else if(changed) Add(p,IntegrityIssueCode.SourceMetadataMismatch,ProjectHealthStatus.Warning,RepairActionCode.None,"Timestamp changed without reference hash; identity cannot be proven.",source.Path,source.Id);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Add(p,IntegrityIssueCode.SourceUnreadable,ProjectHealthStatus.Damaged,RepairActionCode.None,ex.Message,source.Path,source.Id);}
        }
        var prepared = new List<(JsonObject Row,string Path)>();
        foreach(var row in Rows(p.Export,"Tracks").Where(r=>!string.IsNullOrWhiteSpace(Text(r,"ConvertedPath"))))
        {
            try { var path=Resolve(p,Text(row,"ConvertedPath"),"converted");if(Text(row,"Status")=="Converted")prepared.Add((row,path));p.KnownFiles.Add(path); }
            catch(IOException ex){Add(p,ex is ProjectPathSafetyException ? IntegrityIssueCode.UnsafeReparsePoint : ex is ProjectStaleManagedPathException ? IntegrityIssueCode.StaleManagedPath : IntegrityIssueCode.ManifestCorrupt,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,ex.Message);}
        }
        var paths=p.Sources.Select(s=>s.Path).Concat(prepared.Select(s=>s.Path)).Distinct(AtomicJsonStore.PathComparer)
            .Where(path=>File.Exists(path)&&new FileInfo(path).Length>0&&!p.Probes.ContainsKey(path)).ToArray();
        if(paths.Length>0)
        {
            if(!File.Exists(options.FfprobePath)) throw new FileNotFoundException("FFprobe is required for a complete integrity check.",options.FfprobePath);
            using var probePhase=options.Performance?.Phase("ffprobe");
            options.Performance?.Maximum("MaxFfprobeConcurrency",new FfprobeConcurrencyPolicy().GetEffectiveConcurrency(paths.Length,options.ParallelJobs));
            var requests=paths.Select((path,index)=>new AudioProbeBatchRequest(index,path)).ToArray();
            var results=await _probes.ProbeAsync(requests,options.FfprobePath,options.ParallelJobs,options.Performance?.Processes,
                new InlineProgress<int>(completed=>progress?.Report(new("Integrity.Phase.Probe",root,completed,paths.Length))),token:token).ConfigureAwait(false);
            for(var index=0;index<paths.Length;index++)if(results[index] is not null){options.Performance?.Add("FilesProbed");options.Performance?.Add("ProbeBytes",new FileInfo(paths[index]).Length);}
            token.ThrowIfCancellationRequested();
            for(var index=0;index<paths.Length;index++)p.Probes[paths[index]]=results[index];
        }
        foreach(var source in p.Sources)
        {
            if(p.Probes.TryGetValue(source.Path,out var probe)&&probe?.HasPlausibleAudioProperties!=true)
                Add(p,IntegrityIssueCode.SourceUnreadable,ProjectHealthStatus.Damaged,RepairActionCode.None,probe?.ErrorMessage??"Audio probe failed.",source.Path,source.Id);
            if(source.Id==Guid.Empty)continue;
            var metadataPath=ProjectFolderLayout.GetSourceMetadataSnapshotPath(root,source.Id);p.KnownFiles.Add(metadataPath);
            try
            {
                var snapshot = new SourceMetadataSnapshotStore().TryLoad(root,source.Id);
                if(snapshot is null) Add(p,IntegrityIssueCode.SourceMetadataMissing,ProjectHealthStatus.Warning,RepairActionCode.RebuildSourceMetadata,"Source metadata is missing.",metadataPath,source.Id);
                else if(snapshot.SourceId!=source.Id)Add(p,IntegrityIssueCode.ManifestConflict,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"Source metadata identity conflicts.",metadataPath,source.Id);
            }
            catch(PersistentJsonSchemaException ex) when(ex.IsUnsupportedFutureVersion){Add(p,IntegrityIssueCode.UnsupportedSchema,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,ex.Message,metadataPath);}
            catch(Exception ex) when(ex is JsonException or IOException or InvalidOperationException){Add(p,IntegrityIssueCode.SourceMetadataCorrupt,ProjectHealthStatus.Warning,RepairActionCode.RebuildSourceMetadata,ex.Message,metadataPath,source.Id);}
        }
        foreach(var (row,path) in prepared)
        {
            var file=new FileInfo(path);
            if(!file.Exists){Add(p,IntegrityIssueCode.PreparedMissing,ProjectHealthStatus.Warning,RepairActionCode.InvalidatePrepared,"Prepared audio is missing; preparation must be repeated.",path,Id(row));continue;}
            if(file.Length==0||!p.Probes.TryGetValue(path,out var probe)||probe?.HasPlausibleAudioProperties!=true)
            {Add(p,IntegrityIssueCode.PreparedUnreadable,ProjectHealthStatus.Warning,RepairActionCode.InvalidatePrepared,"Prepared audio is unreadable.",path,Id(row));continue;}
            var preset=ExportPreset.Parse(Text(row,"Preset"));
            ConvertedAacTiming? timing=null;
            var corruptTiming=false;
            try { timing=row["AacTiming"]?.Deserialize<ConvertedAacTiming>(); }
            catch(JsonException) { corruptTiming=true; }
            if(!Guid.TryParse(Text(row,"PreparationId"),out var preparation)||preparation==Guid.Empty||Number(row,"PreparationVersion")!=1||
                file.Length!=Number(row,"ConvertedSizeBytes")||file.LastWriteTimeUtc.Ticks!=Number(row,"ConvertedLastWriteUtcTicks")||
                !probe.IsAac||probe.SampleRateHz!=(p.Main["TargetSampleRateHz"] is null ? 44100 : Number(p.Main,"TargetSampleRateHz"))||probe.Channels!=preset.Channels||
                !Path.GetRelativePath(ProjectFolderLayout.GetConvertedFolder(root),path).StartsWith(preset.GetFolderName()+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                Add(p,IntegrityIssueCode.PreparedMetadataMismatch,ProjectHealthStatus.Warning,RepairActionCode.InvalidatePrepared,"Prepared identity, preset or file evidence is inconsistent.",path,Id(row));
            else if(corruptTiming || timing is not null && (!timing.IsValid||timing.Length!=file.Length||timing.LastWriteUtcTicks!=file.LastWriteTimeUtc.Ticks))
                Add(p,IntegrityIssueCode.PreparedTimingMismatch,ProjectHealthStatus.Warning,RepairActionCode.ResetTiming,"Timing evidence is inconsistent.",path,Id(row));
        }
        ValidateAncillary(p);
        foreach(var name in new[]{"concat-list.txt","chapters.ffmetadata"})
            p.KnownFiles.Add(Path.Combine(ProjectFolderLayout.GetMergeFolder(root),name));
        foreach(var path in SafeFileSystemEnumeration.EnumerateFiles(root,token,(path,error)=>throw new IOException("Cannot enumerate project entry: "+path,error)))
        {
            token.ThrowIfCancellationRequested();
            var name=Path.GetFileName(path);
            if(p.KnownFiles.Contains(path)||new[]{"desktop.ini","Thumbs.db","ehthumbs.db"}.Contains(name,StringComparer.OrdinalIgnoreCase)||
                IsIntegrityDiagnostic(p,path)||IsRecoveryBackup(p,path))continue;
            if(IsManagedTemporary(p,path))Add(p,IntegrityIssueCode.ManagedTemporaryFileRemoved,ProjectHealthStatus.Warning,RepairActionCode.RemoveManagedTemporaryFile,"Abandoned managed temporary file.",path);
            else Add(p,IntegrityIssueCode.UnknownProjectFile,ProjectHealthStatus.Warning,RepairActionCode.None,"Unknown file retained.",path);
        }
        return p;
    }
    private static bool IsIntegrityDiagnostic(IntegrityProject project,string path) =>
        AtomicJsonStore.PathComparer.Equals(path,Path.Combine(ProjectFolderLayout.GetSettingsFolder(project.Root),"project-integrity.log")) ||
        AtomicJsonStore.PathComparer.Equals(path,Path.Combine(ProjectFolderLayout.GetSettingsFolder(project.Root),"project-integrity.json"));
    private static bool IsRecoveryBackup(IntegrityProject project,string path)
    {
        var match=System.Text.RegularExpressions.Regex.Match(Path.GetFileName(path),@"^(?<name>.+)\.\d{8}-\d{6}-\d{7}\.defekt\.json$");
        return match.Success && project.KnownFiles.Contains(Path.Combine(Path.GetDirectoryName(path)!,match.Groups["name"].Value+".json"));
    }
    private static void RegisterSource(IntegrityProject p,JsonObject row,string path,long size,long ticks,string? hash,string? baseFolder=null)
    {
        if(string.IsNullOrWhiteSpace(path))return;
        try
        {
            if(!Path.IsPathRooted(path)&&!path.Replace('\\','/').StartsWith("originals/",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(baseFolder))path=Path.Combine(baseFolder,path);
            path=Resolve(p,path,"originals");var id=Id(row);
            if(id==Guid.Empty)Add(p,IntegrityIssueCode.UnknownTrackSourceId,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"Acquired source has no canonical identity.",path);
            var existing=p.Sources.FirstOrDefault(s=>id!=Guid.Empty&&s.Id==id||AtomicJsonStore.PathComparer.Equals(s.Path,path));
            if(existing is not null)
            {
                if(existing.Id!=id||!AtomicJsonStore.PathComparer.Equals(existing.Path,path)||existing.Size>0&&size>0&&existing.Size!=size)
                    Add(p,IntegrityIssueCode.ManifestConflict,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"Conflicting acquisition/source evidence.",path,id);
                // Identity conflicts must not hide another acquired original from validation.
                if(AtomicJsonStore.PathComparer.Equals(existing.Path,path))return;
            }
            p.Sources.Add(new(id,path,size,ticks,hash));
        }
        catch(IOException ex){Add(p,ex is ProjectPathSafetyException ? IntegrityIssueCode.UnsafeReparsePoint : ex is ProjectStaleManagedPathException ? IntegrityIssueCode.StaleManagedPath : IntegrityIssueCode.ManifestCorrupt,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,ex.Message);}
    }
    private static void ValidateAncillary(IntegrityProject p)
    {
        var metadata=ProjectFolderLayout.GetProjectMetadataPath(p.Root);p.KnownFiles.Add(metadata);
        try{if(!File.Exists(metadata))throw new FileNotFoundException("Canonical metadata is missing.");new ProjectMetadataStore().LoadOrCreate(p.Root);}
        catch(PersistentJsonSchemaException ex) when(ex.IsUnsupportedFutureVersion){Add(p,IntegrityIssueCode.UnsupportedSchema,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,ex.Message,metadata);}
        catch(Exception ex) when(ex is IOException or JsonException or InvalidOperationException){Add(p,File.Exists(metadata)?IntegrityIssueCode.ProjectMetadataCorrupt:IntegrityIssueCode.ProjectMetadataMissing,ProjectHealthStatus.Warning,RepairActionCode.RebuildProjectMetadata,ex.Message,metadata);}
        var tracks=ProjectFolderLayout.GetTrackListStatePath(p.Root);p.KnownFiles.Add(tracks);
        try
        {
            if(!File.Exists(tracks))throw new FileNotFoundException("Track list state is missing.");
            var state=Read(tracks);CheckSchema(p,tracks,state,TrackListStateService.CurrentSchemaVersion);
            if(state["Tracks"] is not JsonArray)throw new JsonException("Track list entries are missing.");
            foreach(var row in Rows(state,"Tracks"))
                if(!p.Sources.Any(s=>s.Id==Id(row)) && !Rows(p.Main,"Discs").SelectMany(d=>Rows(d,"Tracks")).Any(r=>Id(r)==Id(row)))
                {
                    var key=Text(row,"Key");var matches=p.Sources.Where(s=>MatchesLegacyTrackKey(key,s.Path)).ToArray();
                    Add(p,IntegrityIssueCode.UnknownTrackSourceId,matches.Length==1?ProjectHealthStatus.Warning:ProjectHealthStatus.RebuildRequired,
                        matches.Length==1?RepairActionCode.CorrectSourceReference:RepairActionCode.None,"Track list contains an unknown source reference.",tracks,Id(row));
                }
        }
        catch(Exception ex) when(ex is IOException or JsonException or InvalidOperationException){Add(p,IntegrityIssueCode.TrackListStateCorrupt,p.Sources.Count>0?ProjectHealthStatus.RebuildRequired:ProjectHealthStatus.Warning,RepairActionCode.RebuildTrackList,ex.Message,tracks);}
        var preset=Text(p.Export,"SelectedPreset");
        if(!string.IsNullOrWhiteSpace(preset)&&!ExportPreset.AvailableDisplayNames.Contains(preset,StringComparer.OrdinalIgnoreCase))
            Add(p,IntegrityIssueCode.PresetUnavailable,ProjectHealthStatus.Warning,RepairActionCode.None,"Selected preset is not available.");
        foreach(var cover in new[]{Text(p.Export?["Metadata"],"CoverSourcePath"),Text(p.Export?["Metadata"],"ProcessedCoverPath")}.Where(s=>!string.IsNullOrEmpty(s)))
        {if(File.Exists(cover))p.KnownFiles.Add(Path.GetFullPath(cover));else Add(p,IntegrityIssueCode.CoverMissing,ProjectHealthStatus.Warning,RepairActionCode.None,"Optional cover reference is missing.",cover);}
    }
    internal static bool MatchesLegacyTrackKey(string key, string path) =>
        key.StartsWith("path:",StringComparison.OrdinalIgnoreCase)
            ? AtomicJsonStore.PathComparer.Equals(key[5..],Path.GetFullPath(path))
            : key.Contains(":file:",StringComparison.OrdinalIgnoreCase) &&
              string.Equals(key[(key.LastIndexOf(":file:",StringComparison.OrdinalIgnoreCase)+6)..],Path.GetFileName(path),StringComparison.OrdinalIgnoreCase);
    private static void CheckSchema(IntegrityProject p,string path,JsonObject json,int max)
    {if(Number(json,"schemaVersion")<=0||Number(json,"schemaVersion")>max)Add(p,IntegrityIssueCode.UnsupportedSchema,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"Unsupported JSON schema.",path);}
    internal static bool IsManagedTemporary(IntegrityProject p,string path)
    {
        var name=Path.GetFileName(path);
        if(path.StartsWith(ProjectFolderLayout.GetConvertedFolder(p.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&name.EndsWith(".part",StringComparison.OrdinalIgnoreCase))return true;
        // originals is immutable, including partial recovery evidence.
        if(!path.StartsWith(ProjectFolderLayout.GetSettingsFolder(p.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||
            !name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase)||name.Length<=37||!Guid.TryParseExact(name[^36..^4],"N",out _))return false;
        var originalPath=path[..^37];
        return p.KnownFiles.Contains(originalPath);
    }
    internal static async Task<string> HashWithCacheAsync(IntegrityProject project,string path,CancellationToken token,ProjectIntegrityPerformance? performance=null)
    {
        token.ThrowIfCancellationRequested();
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(path);
        var file=new FileInfo(path);var size=file.Length;var ticks=file.LastWriteTimeUtc.Ticks;
        if(project.Hashes.TryGetValue(path,out var cached)&&cached.Size==size&&cached.Ticks==ticks)return cached.Hash;
        var hash=await HashAsync(path,token).ConfigureAwait(false);
        performance?.Add("FilesHashed");performance?.Add("HashBytes",size);
        file.Refresh();
        if(!file.Exists||file.Length!=size||file.LastWriteTimeUtc.Ticks!=ticks)throw new IOException("Original changed during hashing: "+path);
        project.Hashes[path]=(size,ticks,hash);
        return hash;
    }
    internal static async Task<string> HashAsync(string path,CancellationToken token)
    {using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,131072,FileOptions.Asynchronous|FileOptions.SequentialScan);return Convert.ToHexString(await SHA256.HashDataAsync(stream,token).ConfigureAwait(false));}
    private sealed class InlineProgress<T>(Action<T> callback):IProgress<T>{public void Report(T value)=>callback(value);}
}
