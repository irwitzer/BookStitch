using BookStitch.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using static BookStitch.Services.ProjectIntegrityValidator;
namespace BookStitch.Services;

/// <summary>Explicit user recovery. Originals are read-only; all replaced state is backed up.</summary>
public sealed class ProjectRebuildService
{
    private readonly AudioProbeBatchService _probes;
    public ProjectRebuildService(AudioProbeBatchService probes)=>_probes=probes;
    public async Task<ProjectIntegrityResult> RebuildAsync(string root,ProjectIntegrityOptions options,
        CancellationToken token=default,IProgress<IntegrityProgress>? progress=null)
    {
        using var rebuildPhase=options.Performance?.Phase("rebuild");
        progress?.Report(new("Rebuild.Phase.Inspect",root,0,8));
        token.ThrowIfCancellationRequested();SafeFileSystemEnumeration.EnsureSafeTree(root,token);
        var mainPath=new[]{ProjectFolderLayout.GetFolderManifestPath(root),ProjectFolderLayout.GetMp3DiscManifestPath(root),
            ProjectFolderLayout.GetAudioDiscManifestPath(root)}.Where(File.Exists).Single();
        var main=Read(mainPath);
        var reconciled=new ProjectReconciliationService().Reconcile(root,main,mainPath);
        if(reconciled.SafetyBlocked)throw new IOException("Project filesystem safety is blocked.");
        var project=await new ProjectIntegrityValidator(_probes).ValidateAsync(root,options,null,token).ConfigureAwait(false);
        if(!project.Safe||project.Findings.Any(f=>f.IssueCode==IntegrityIssueCode.UnsafeReparsePoint || f.HealthImpact==ProjectHealthStatus.Damaged))
            return new(root,project.Findings.Any(f=>f.HealthImpact==ProjectHealthStatus.Damaged)?ProjectHealthStatus.Damaged:ProjectHealthStatus.RebuildRequired,false,false,project.Findings);
        // Explicit total recovery may recover unindexed folder originals, but never pending disc tracks.
        if(Text(main,"ProjectType")==ProjectManifestTypes.FolderProject)
            foreach(var path in SafeFileSystemEnumeration.EnumerateFiles(ProjectFolderLayout.GetOriginalsFolder(root),token))
                if(AudioInputFormatCatalog.IsLocalImportExtension(Path.GetExtension(path)) && !project.Sources.Any(source=>AtomicJsonStore.PathComparer.Equals(source.Path,path)))
                    project.Sources.Add(new(Guid.Empty,path,new FileInfo(path).Length,File.GetLastWriteTimeUtc(path).Ticks,null));
        var unprobed=project.Sources.Select(source=>source.Path).Distinct(AtomicJsonStore.PathComparer).Where(path=>!project.Probes.ContainsKey(path)).ToArray();
        if(unprobed.Length>0)
        {
            if(!File.Exists(options.FfprobePath))throw new FileNotFoundException("FFprobe is required for recovery.",options.FfprobePath);
            using var probePhase=options.Performance?.Phase("ffprobe");
            var results=await _probes.ProbeAsync(unprobed.Select((path,index)=>new AudioProbeBatchRequest(index,path)).ToArray(),options.FfprobePath,options.ParallelJobs,options.Performance?.Processes,token:token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            for(var index=0;index<unprobed.Length;index++){project.Probes[unprobed[index]]=results[index];options.Performance?.Add("FilesProbed");options.Performance?.Add("ProbeBytes",new FileInfo(unprobed[index]).Length);}
        }
        foreach(var source in project.Sources)
            if(!File.Exists(source.Path)||new FileInfo(source.Path).Length==0||!project.Probes.TryGetValue(source.Path,out var probe)||probe?.HasPlausibleAudioProperties!=true)
                return new(root,ProjectHealthStatus.Damaged,false,false,[new(IntegrityIssueCode.SourceUnreadable,ProjectHealthStatus.Damaged,RepairActionCode.None,"Required recovery original is not readable.",source.Path)]);
        progress?.Report(new("Rebuild.Phase.Identity",root,1,8));
        var recoveredIds=false;var mainChanged=false;
        var conflicts=project.Sources.GroupBy(source=>source.Id).Where(group=>group.Key==Guid.Empty||group.Select(source=>source.Path).Distinct(AtomicJsonStore.PathComparer).Count()>1).Select(group=>group.Key).ToHashSet();
        using(options.Performance?.Phase("hash"))
        for(var index=0;index<project.Sources.Count;index++)
        {
            var source=project.Sources[index];if(!conflicts.Contains(source.Id))continue;
            var hash=await HashWithCacheAsync(project,source.Path,token,options.Performance).ConfigureAwait(false);
            // Stable identity only for explicitly requested recovery; never Guid.NewGuid().
            var evidence=Text(main,"ProjectType")+"|"+Path.GetRelativePath(root,source.Path).Replace('\\','/')+"|"+hash;
            var id=new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("BookStitch/recovered-source/v1|"+evidence))[..16]);
            project.Sources[index]=source with {Id=id,Hash=hash};recoveredIds=true;
            foreach(var disc in Rows(main,"ImportedDiscs"))foreach(var row in Rows(disc,"SourceFiles"))
                if(AtomicJsonStore.PathComparer.Equals(Mp3DiscProjectService.GetOriginalSourcePath(root,Text(row,"RelativePath")),source.Path)){row["SourceId"]=id.ToString();mainChanged=true;}
            foreach(var disc in Rows(main,"Discs"))foreach(var row in Rows(disc,"Tracks").Where(row=>Text(row,"Status")=="Ripped"))
                if(AtomicJsonStore.PathComparer.Equals(ProjectManagedPathService.Resolve(root,Text(row,"RelativePath"),"originals"),source.Path)){row["SourceId"]=id.ToString();mainChanged=true;}
        }
        if(mainChanged){ProjectRepairService.Backup(mainPath);new AtomicJsonStore().Save(mainPath,main,JsonOptions);}
        progress?.Report(new("Rebuild.Phase.Tracks",root,2,8));
        var store=new AtomicJsonStore();var lostTracks=recoveredIds;var lostMetadata=false;
        var trackPath=ProjectFolderLayout.GetTrackListStatePath(root);JsonObject? trackState=null;
        try
        {
            trackState=Read(trackPath);
            if(Number(trackState,"schemaVersion")!=TrackListStateService.CurrentSchemaVersion || trackState["Tracks"] is not JsonArray ||
                Rows(trackState,"Tracks").Any(r=>!project.Sources.Any(s=>s.Id==Id(r))))trackState=null;
        }
        catch(Exception ex) when(ex is IOException or JsonException){ }
        var work=new ExportWorkManifest {ProjectId=Text(main,"ProjectId"),ProjectType=Text(main,"ProjectType"),
            ProjectWorkFolder=root,SourceFolder=Text(main,"SourceFolder"),SelectedPreset=Text(project.Export,"SelectedPreset"),
            State=new(){Status=ProjectManifestStatuses.ReviewBeforeMerge}};
        if(string.IsNullOrWhiteSpace(work.SelectedPreset))work.SelectedPreset=ExportPreset.DefaultDisplayName;
        try
        {
            work.Export=project.Export?["Export"]?.Deserialize<ExportManifestExportSettings>()??new();
            work.Metadata=project.Export?["Metadata"]?.Deserialize<ExportManifestBookMetadata>()??new();
        }
        catch(JsonException){lostMetadata=true;}
        var ordered=trackState is null?project.Sources.Select(s=>s.Id).ToArray():Rows(trackState,"Tracks").OrderBy(r=>Number(r,"Order")).Select(Id).ToArray();
        if(trackState is null)lostTracks|=project.Sources.Count>0;
        var sources=project.Sources.ToDictionary(s=>s.Id);
        var occurrenceIndex=0;
        var savedRows=Rows(trackState,"Tracks").OrderBy(r=>Number(r,"Order")).ToArray();
        foreach(var id in ordered)
        {
            token.ThrowIfCancellationRequested();var source=sources[id];var probe=project.Probes[source.Path]!;
            var state=trackState is null?null:savedRows[occurrenceIndex++];
            work.Tracks.Add(new(){SourceId=id,TrackIndex=work.Tracks.Count+1,SourcePath=source.Path,SourceFileName=Path.GetFileName(source.Path),
                SourceSizeBytes=new FileInfo(source.Path).Length,SourceLastWriteUtcTicks=File.GetLastWriteTimeUtc(source.Path).Ticks,
                SourceCodec=probe.CodecName??"",SourceExtension=Path.GetExtension(source.Path),DurationTicks=probe.Duration?.Ticks,
                ChapterTitle=Text(state,"ChapterTitle"),Preset=work.SelectedPreset,Status=ProjectManifestTrackStatuses.Pending});
        }
        progress?.Report(new("Rebuild.Phase.Metadata",root,3,8));
        // Preserve valid canonical user metadata, including fields not projected into export metadata.
        try {if(!File.Exists(ProjectFolderLayout.GetProjectMetadataPath(root)))throw new FileNotFoundException();new ProjectMetadataStore().LoadOrCreate(root);}
        catch(Exception ex) when(ex is IOException or JsonException or InvalidOperationException)
        {
            ProjectRepairService.Backup(ProjectFolderLayout.GetProjectMetadataPath(root));
            var metadata=new ProjectMetadata();new ProjectMetadataInitializationService().Apply(metadata,new SourceMetadataSnapshotStore().LoadAll(root),ProjectMetadataInitializationState.InitialSetComplete);
            new ProjectMetadataStore().Save(root,metadata);lostMetadata=true;
        }
        progress?.Report(new("Rebuild.Phase.Sources",root,4,8));
        foreach(var source in project.Sources)
        {
            token.ThrowIfCancellationRequested();var path=ProjectFolderLayout.GetSourceMetadataSnapshotPath(root,source.Id);
            ProjectRepairService.Backup(path);
            new SourceMetadataSnapshotStore().Save(root,new SourceMetadataCaptureService().Capture(new(source.Id,source.Path,
                ProbeInfo:project.Probes[source.Path],Evidence:"Explicit project rebuild; originals unchanged.")));
        }
        progress?.Report(new("Rebuild.Phase.Copies",root,5,8));
        if(work.ProjectType!=ProjectManifestTypes.AudioCdProject)
        {
            var identities=new JsonObject { ["schemaVersion"]=1,["Files"]=new JsonArray() };
            using var copyHashPhase=options.Performance?.Phase("hash");
            foreach(var source in project.Sources)
            {
                token.ThrowIfCancellationRequested();
                var hash=await HashWithCacheAsync(project,source.Path,token,options.Performance).ConfigureAwait(false);

                ((JsonArray)identities["Files"]!).Add(new JsonObject {
                    ["SourceId"]=source.Id.ToString(),["TargetRelativePath"]=Path.GetRelativePath(root,source.Path),["SourceFileName"]=Path.GetFileName(source.Path),
                    ["SourceSizeBytes"]=new FileInfo(source.Path).Length,["SourceLastWriteUtcTicks"]=File.GetLastWriteTimeUtc(source.Path).Ticks,
                    ["ContentSha256"]=hash });
            }
            var path=Path.Combine(ProjectFolderLayout.GetSettingsFolder(root),SourceCopyIdentityStore.FileName);
            ProjectRepairService.Backup(path);store.Save(path,identities,JsonOptions);
        }
        progress?.Report(new("Rebuild.Phase.Save",root,6,8));
        if(trackState is null)
        {
            ProjectRepairService.Backup(trackPath);
            new TrackListStateService().Save(root,work.Tracks.Select(t=>new TrackInfo {SourceId=t.SourceId,FilePath=t.SourcePath,Index=t.TrackIndex,ChapterTitle=t.ChapterTitle}).ToList());
        }
        ProjectRepairService.Backup(ProjectFolderLayout.GetExportManifestPath(root));
        store.Save(ProjectFolderLayout.GetExportManifestPath(root),work,JsonOptions);
        // Only an explicit folder rebuild supersedes the legacy work state. Preserve it first.
        if (Text(main, "ProjectType") == ProjectManifestTypes.FolderProject)
        {
            foreach (var legacy in new[] { ProjectFolderLayout.GetWorkManifestPath(root), Path.Combine(root, "project.json") }.Distinct())
            {
                if (!File.Exists(legacy)) continue;
                SafeFileSystemEnumeration.EnsureNoReparseAncestors(legacy);
                ProjectRepairService.Backup(legacy);
                File.Delete(legacy);
            }
        }
        // Retain replaceable audio files physically. They are no longer reusable without manifest evidence.
        progress?.Report(new("Rebuild.Phase.Validate",root,7,8));
        var result=await new ProjectIntegrityService(_probes).InspectAsync(root,options,null,token,project).ConfigureAwait(false);
        var findings=result.Findings.ToList();
        if(lostTracks)findings.Add(new(IntegrityIssueCode.RebuildTrackStateLost,ProjectHealthStatus.Warning,RepairActionCode.None,"Track ordering or exclusion state could not be retained."));
        if(lostMetadata)findings.Add(new(IntegrityIssueCode.RebuildMetadataLost,ProjectHealthStatus.Warning,RepairActionCode.None,"Project metadata could not be retained completely."));
        result=result with { WasRepaired=true,Health=(lostTracks||lostMetadata)&&result.Health==ProjectHealthStatus.OK?ProjectHealthStatus.Warning:result.Health,Findings=findings };
        new ProjectIntegrityService(_probes).PersistResult(root,result);
        progress?.Report(new("Rebuild.Phase.Validate",root,8,8));
        return result;
    }
}
