using BookStitch.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using static BookStitch.Services.ProjectIntegrityValidator;
namespace BookStitch.Services;

/// <summary>Explicit, conservative whitelist. Never modifies originals or guesses acquisition identities.</summary>
public sealed class ProjectRepairService
{
    private readonly AtomicJsonStore _store = new();
    internal async Task<IReadOnlyList<IntegrityFinding>> RepairAsync(IntegrityProject p, CancellationToken token,ProjectIntegrityPerformance? performance=null)
    {
        var repaired = new List<IntegrityFinding>();
        if(!p.Safe || p.Findings.Any(f=>f.IssueCode==IntegrityIssueCode.UnsafeReparsePoint))return repaired;
        var exportChanged=false;var mainChanged=false;var copiesChanged=false;
        var referenceStates=new Dictionary<string,JsonObject>(AtomicJsonStore.PathComparer);
        var pendingInvalidations=new List<IntegrityFinding>();
        var copyGroupsCompleted=new HashSet<string>(AtomicJsonStore.PathComparer);
        var groups=p.Findings.Where(f=>f.RepairAction!=RepairActionCode.None)
            .DistinctBy(f=>(f.RepairAction,f.Path,f.SourceId)).GroupBy(f=>f.RepairAction).ToArray();
        foreach(var group in groups)
        foreach(var finding in group)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if(finding.Path is not null) SafeFileSystemEnumeration.EnsureNoReparseAncestors(finding.Path);
                if(p.Findings.Any(f=>f.IssueCode==IntegrityIssueCode.UnsupportedSchema && f.Path==finding.Path))continue;
                if(finding.RepairAction is RepairActionCode.ResetTiming or RepairActionCode.InvalidatePrepared or RepairActionCode.UpdateSourceEvidence &&
                    p.Findings.Any(f=>f.IssueCode is IntegrityIssueCode.ManifestCorrupt or IntegrityIssueCode.ProjectIdConflict or IntegrityIssueCode.ProjectTypeConflict))continue;
                if(finding.RepairAction==RepairActionCode.RebuildTrackList && p.Sources.Count>0)continue;
                switch(finding.RepairAction)
                {
                    case RepairActionCode.CreateDirectory:
                        SafeFileSystemEnumeration.EnsureNoReparseAncestors(finding.Path!);Directory.CreateDirectory(finding.Path!);break;
                    case RepairActionCode.RebuildSourceMetadata:
                    {
                        var source=p.Sources.Single(s=>s.Id==finding.SourceId);
                        if(!p.Probes.TryGetValue(source.Path,out var probe)||probe?.HasPlausibleAudioProperties!=true)continue;
                        RecordBackup(p,finding.Path!);
                        var snapshot=new SourceMetadataCaptureService().Capture(new(source.Id,source.Path,ProbeInfo:probe,Evidence:"Integrity repair; existing probe reused."));
                        new SourceMetadataSnapshotStore().Save(p.Root,snapshot);break;
                    }
                    case RepairActionCode.RebuildProjectMetadata:
                    {
                        RecordBackup(p,finding.Path!);
                        var metadata=new ProjectMetadata();
                        var snapshots=new SourceMetadataSnapshotStore().LoadAll(p.Root);
                        new ProjectMetadataInitializationService().Apply(metadata,snapshots,ProjectMetadataInitializationState.InitialSetComplete);
                        new ProjectMetadataStore().Save(p.Root,metadata);break;
                    }
                    case RepairActionCode.RebuildTrackList:
                    {
                        RecordBackup(p,finding.Path!);
                        var occurrences=Rows(p.Export,"Tracks").GroupBy(r=>(Id(r),Number(r,"TrackIndex"))).Select(g=>g.First()).OrderBy(r=>Number(r,"TrackIndex")).ToArray();
                        var tracks=occurrences.Where(r=>p.Sources.Any(s=>s.Id==Id(r))).Select((row,index)=>new TrackInfo {
                            SourceId=Id(row),FilePath=p.Sources.First(s=>s.Id==Id(row)).Path,Index=index+1,ChapterTitle=Text(row,"ChapterTitle") }).ToList();
                        if(tracks.Count==0) tracks=p.Sources.Select((source,index)=>new TrackInfo { SourceId=source.Id,FilePath=source.Path,Index=index+1 }).ToList();
                        new TrackListStateService().Save(p.Root,tracks);break;
                    }
                    case RepairActionCode.CorrectSourceReference:
                    {
                        if(!referenceStates.TryGetValue(finding.Path!,out var state))state=Read(finding.Path!);
                        var changes=new List<(JsonObject Row,IntegritySource Source)>();
                        foreach(var row in Rows(state,"Tracks").Where(r=>Id(r)==finding.SourceId))
                        {
                            var matches=p.Sources.Where(s=>MatchesLegacyTrackKey(Text(row,"Key"),s.Path)).ToArray();
                            if(matches.Length!=1)throw new InvalidDataException("Reference cannot be uniquely resolved.");
                            changes.Add((row,matches[0]));
                        }
                        foreach(var (row,source) in changes)
                        {row["SourceId"]=source.Id.ToString();row["Key"]="sourceid:"+source.Id.ToString("N").ToUpperInvariant();}
                        referenceStates[finding.Path!]=state;break;
                    }
                    case RepairActionCode.RebuildCopyIdentity:
                    {
                        if(copyGroupsCompleted.Contains(finding.Path!))break;
                        if(p.Sources.Any(s=>s.Id==Guid.Empty||!File.Exists(s.Path)||s.Size<=0||new FileInfo(s.Path).Length!=s.Size))continue;
                        var identities=p.CopyIdentities??new JsonObject { ["schemaVersion"]=1,["Files"]=new JsonArray() };
                        using var hashPhase=performance?.Phase("hash");
                        var knownIds=Rows(identities,"Files").Select(Id).ToHashSet();
                        foreach(var source in p.Sources.Where(s=>!knownIds.Contains(s.Id)))
                        {
                            var hash=await HashWithCacheAsync(p,source.Path,token,performance).ConfigureAwait(false);
                            ((JsonArray)identities["Files"]!).Add(new JsonObject { ["SourceId"]=source.Id.ToString(),
                                ["TargetRelativePath"]=Path.GetRelativePath(p.Root,source.Path),["SourceFileName"]=Path.GetFileName(source.Path),
                                ["SourceSizeBytes"]=source.Size,["SourceLastWriteUtcTicks"]=source.LastWrite,
                                ["ContentSha256"]=hash });
                            knownIds.Add(source.Id);
                        }
                        if(p.CopyIdentities is null)RecordBackup(p,finding.Path!);
                        p.CopyIdentities=identities;copiesChanged=true;copyGroupsCompleted.Add(finding.Path!);break;
                    }
                    case RepairActionCode.UpdateSourceEvidence:
                    {
                        var ticks=new FileInfo(finding.Path!).LastWriteTimeUtc.Ticks;
                        foreach(var row in Rows(p.Export,"Tracks").Where(r=>Id(r)==finding.SourceId))row["SourceLastWriteUtcTicks"]=ticks;
                        foreach(var disc in Rows(p.Main,"ImportedDiscs"))foreach(var row in Rows(disc,"SourceFiles").Where(r=>Id(r)==finding.SourceId))row["SourceLastWriteUtcTicks"]=ticks;
                        foreach(var row in Rows(p.CopyIdentities,"Files").Where(r=>Id(r)==finding.SourceId))row["SourceLastWriteUtcTicks"]=ticks;
                        copiesChanged|=p.CopyIdentities is not null;
                        exportChanged=p.Export is not null;mainChanged=true;break;
                    }
                    case RepairActionCode.ResetTiming:
                        foreach(var row in Rows(p.Export,"Tracks").Where(r=>Id(r)==finding.SourceId && AtomicJsonStore.PathComparer.Equals(Resolve(p,Text(r,"ConvertedPath"),"converted"),finding.Path)))
                            row["AacTiming"]=null;
                        exportChanged=true;break;
                    case RepairActionCode.InvalidatePrepared:
                    {
                        // Persist the invalidation before removing the replaceable artifact.
                        foreach(var row in Rows(p.Export,"Tracks").Where(r=>Id(r)==finding.SourceId&&
                            AtomicJsonStore.PathComparer.Equals(Resolve(p,Text(r,"ConvertedPath"),"converted"),finding.Path)))
                        {
                            row["Status"]="Pending";row["PreparationId"]=Guid.Empty.ToString();row["PreparationVersion"]=0;
                            row["AacTiming"]=null;row["ConvertedSizeBytes"]=0;row["ConvertedLastWriteUtcTicks"]=0;
                        }
                        pendingInvalidations.Add(finding);exportChanged=true;continue;
                    }
                    case RepairActionCode.RemoveManagedTemporaryFile:
                        if(!IsManagedTemporary(p,finding.Path!))continue;
                        File.Delete(finding.Path!);break;
                    default:continue;
                }
                repaired.Add(finding);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                System.Diagnostics.Trace.TraceWarning("Integrity repair failed: {0}: {1}",finding.Path,ex.Message);
            }
        }
        token.ThrowIfCancellationRequested();
        foreach(var (path,state) in referenceStates)_store.Save(path,state,JsonOptions);
        if(copiesChanged&&p.CopyIdentities is not null)_store.Save(Path.Combine(ProjectFolderLayout.GetSettingsFolder(p.Root),SourceCopyIdentityStore.FileName),p.CopyIdentities,JsonOptions);
        if(exportChanged&&p.Export is not null)_store.Save(ProjectFolderLayout.GetExportManifestPath(p.Root),p.Export,JsonOptions);
        if(mainChanged)_store.Save(p.MainPath,p.Main,JsonOptions);
        foreach(var finding in pendingInvalidations)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                SafeFileSystemEnumeration.EnsureNoReparseAncestors(finding.Path!);
                if(File.Exists(finding.Path))File.Delete(finding.Path!);
                repaired.Add(finding);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {System.Diagnostics.Trace.TraceWarning("Integrity repair failed: {0}: {1}",finding.Path,ex.Message);}
        }
        return repaired;
    }
    private static void RecordBackup(IntegrityProject project,string path)
    {
        var backup=Backup(path);
        if(backup is not null)project.RecoveryBackups.Add(backup);
    }
    public static string? Backup(string path)
    {
        if(!File.Exists(path))return null;
        SafeFileSystemEnumeration.EnsureNoReparseAncestors(path);
        var target=Path.Combine(Path.GetDirectoryName(path)!,Path.GetFileNameWithoutExtension(path)+"."+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff",System.Globalization.CultureInfo.InvariantCulture)+".defekt"+Path.GetExtension(path));
        File.Copy(path,target,overwrite:false);return target;
    }
}
