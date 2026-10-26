using BookStitch.Models;
using System.IO;
using System.Text.Json.Nodes;
using static BookStitch.Services.ProjectIntegrityValidator;
namespace BookStitch.Services;

internal sealed record ProjectReconciliationResult(bool Changed,bool Complete,bool SafetyBlocked)
{ public IReadOnlyList<IntegrityFinding> Repairs { get; init; } = []; }

/// <summary>Idempotent v1 reconciliation from acquisition/file evidence; never creates source identities.</summary>
public sealed class ProjectReconciliationService
{
    internal ProjectReconciliationResult Reconcile(string root,JsonObject main,string mainPath)
    {
        try { SafeFileSystemEnumeration.EnsureSafeTree(root); }
        catch(ProjectPathSafetyException) { return new(false,false,true); }
        var paths=new[]{ProjectFolderLayout.GetExportManifestPath(root),ProjectFolderLayout.GetTrackListStatePath(root),
            Path.Combine(ProjectFolderLayout.GetSettingsFolder(root),SourceCopyIdentityStore.FileName)};
        var docs=new Dictionary<string,JsonObject>(AtomicJsonStore.PathComparer) { [mainPath]=main };
        var complete=true;
        foreach(var path in paths.Where(File.Exists))
        { try { docs[path]=Read(path); } catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException) { complete=false; } }
        var export=docs.GetValueOrDefault(paths[0]);
        var acquired=Rows(main,"ImportedDiscs").Any(d=>Rows(d,"SourceFiles").Any()) || Rows(main,"Discs").Any(d=>Rows(d,"Tracks").Any(t=>Text(t,"Status")=="Ripped"));
        var needsExport=export is not null || acquired || Text(main,"PipelineState") is "ReviewBeforeMerge" or "Completed";
        if(needsExport && (export is null || export["Tracks"] is not JsonArray || export["Export"] is not JsonObject || export["State"] is not JsonObject ||
            Text(export,"FormatVersion")!=ExportWorkManifestVersions.Current || Number(export,"schemaVersion")!=ExportWorkManifestVersions.CurrentSchemaVersion))complete=false;
        if(docs.TryGetValue(paths[1],out var existingState) && (existingState["Tracks"] is not JsonArray || Number(existingState,"schemaVersion")!=TrackListStateService.CurrentSchemaVersion))complete=false;
        if(docs.TryGetValue(paths[2],out var existingCopies) && (existingCopies["Files"] is not JsonArray || Number(existingCopies,"schemaVersion")!=1))complete=false;
        var oldRoots=new[]{Text(main,"ProjectFolder"),Text(export,"ProjectWorkFolder")};
        var changes=new HashSet<string>(AtomicJsonStore.PathComparer);
        var repairs=new List<IntegrityFinding>();
        var safetyBlocked=false;
        void Rebase(string document,JsonObject row,string field,string value,string path)
        {
            if(!Path.IsPathRooted(value)||AtomicJsonStore.PathComparer.Equals(Path.GetFullPath(value),path))return;
            row[field]=field=="SourcePath"||field=="ConvertedPath"?path:
                Path.GetRelativePath(field=="RelativePath"&&Text(main,"ProjectType")==ProjectManifestTypes.Mp3DiscProject?ProjectFolderLayout.GetOriginalsFolder(root):root,path);
            changes.Add(document);
            repairs.Add(new(IntegrityIssueCode.StaleManagedPath,ProjectHealthStatus.OK,RepairActionCode.RebaseManagedPath,"Rebased an existing managed file to the current project root.",path) {WasRepaired=true});
        }
        var evidence=new List<(string Document,JsonObject Row,string Path,bool Primary)>();
        var aliases=new Dictionary<Guid,HashSet<Guid>>();
        void AddRow(string document,JsonObject row,string field,bool primary,string? relativeBase=null)
        {
            var value=Text(row,field);if(string.IsNullOrWhiteSpace(value)){complete=false;return;}
            try
            {
                var path=ProjectManagedPathService.Resolve(root,Path.IsPathRooted(value)?value:
                    relativeBase is null?value:Path.Combine(relativeBase,value),"originals",oldRoots);
                Rebase(document,row,field,value,path);
                if(!File.Exists(path))return; // The validator, not reconciliation, decides original damage.
                var size=Number(row,primary&&Text(main,"ProjectType")==ProjectManifestTypes.AudioCdProject?"OutputSizeBytes":"SourceSizeBytes");
                if(size>0 && new FileInfo(path).Length!=size)return;
                evidence.Add((document,row,path,primary));
                if(field=="SourcePath"&&!AtomicJsonStore.PathComparer.Equals(value,path)){row[field]=path;changes.Add(document);}
            }
            catch(ProjectPathSafetyException){complete=false;safetyBlocked=true;}
            catch(IOException){complete=false;}
        }
        if(Text(main,"ProjectType")==ProjectManifestTypes.Mp3DiscProject)
            foreach(var disc in Rows(main,"ImportedDiscs"))foreach(var row in Rows(disc,"SourceFiles"))
                AddRow(mainPath,row,"RelativePath",true,"originals");
        if(Text(main,"ProjectType")==ProjectManifestTypes.AudioCdProject)
            foreach(var disc in Rows(main,"Discs"))foreach(var row in Rows(disc,"Tracks").Where(r=>Text(r,"Status")=="Ripped"))
                AddRow(mainPath,row,"RelativePath",true);
        foreach(var row in Rows(export,"Tracks"))AddRow(paths[0],row,"SourcePath",false);
        foreach(var row in Rows(docs.GetValueOrDefault(paths[2]),"Files"))AddRow(paths[2],row,"TargetRelativePath",false);
        var canonical=new Dictionary<string,Guid>(AtomicJsonStore.PathComparer);
        foreach(var group in evidence.GroupBy(e=>e.Path,AtomicJsonStore.PathComparer))
        {
            var primary=group.Where(e=>e.Primary).Select(e=>Id(e.Row)).Where(id=>id!=Guid.Empty).Distinct().ToArray();
            var ids=primary.Length>0?primary:group.Select(e=>Id(e.Row)).Where(id=>id!=Guid.Empty).Distinct().ToArray();
            if(ids.Length!=1){complete=false;continue;}
            var id=ids[0];
            if(evidence.Any(e=>!AtomicJsonStore.PathComparer.Equals(e.Path,group.Key)&&Id(e.Row)==id)) {complete=false;continue;}
            canonical[group.Key]=id;
            foreach(var item in group)
            {
                var old=Id(item.Row);
                if(old!=Guid.Empty){if(!aliases.TryGetValue(old,out var mapped))aliases[old]=mapped=[];mapped.Add(id);}
                if(old==id)continue;
                item.Row["SourceId"]=id.ToString();changes.Add(item.Document);
            }
        }
        var state=docs.GetValueOrDefault(paths[1]);
        if(state is not null)
            foreach(var row in Rows(state,"Tracks"))
            {
                var old=Id(row);Guid id=Guid.Empty;
                if(aliases.TryGetValue(old,out var mapped)&&mapped.Count==1)id=mapped.Single();
                else
                {
                    var matches=canonical.Where(pair=>MatchesLegacyTrackKey(Text(row,"Key"),pair.Key)).ToArray();
                    if(matches.Length==1)id=matches[0].Value;
                }
                if(id==Guid.Empty){complete=false;continue;}
                if(old==id)continue;
                row["SourceId"]=id.ToString();row["Key"]="sourceid:"+id.ToString("N").ToUpperInvariant();changes.Add(paths[1]);
            }
        if(export is not null)
        {
            foreach(var row in Rows(export,"Tracks"))
            {
                var value=Text(row,"ConvertedPath");if(string.IsNullOrWhiteSpace(value))continue;
                try
                {
                    var path=ProjectManagedPathService.Resolve(root,value,"converted",oldRoots);
                    Rebase(paths[0],row,"ConvertedPath",value,path);
                }
                catch(ProjectPathSafetyException){complete=false;safetyBlocked=true;}
                catch(IOException){complete=false;}
            }
            if(safetyBlocked)return new(false,false,true);
            if(Text(export,"ProjectWorkFolder")!=root){export["ProjectWorkFolder"]=root;changes.Add(paths[0]);}
            if(string.IsNullOrWhiteSpace(Text(export,"ProjectId"))){export["ProjectId"]=Text(main,"ProjectId");changes.Add(paths[0]);}
            if(string.IsNullOrWhiteSpace(Text(export,"ProjectType"))){export["ProjectType"]=Text(main,"ProjectType");changes.Add(paths[0]);}
        }
        if(safetyBlocked)return new(false,false,true);
        if(Text(main,"ProjectFolder")!=root){main["ProjectFolder"]=root;changes.Add(mainPath);}
        var sourceFolder=ProjectFolderLayout.GetSourceMetadataSourcesFolder(root);
        var legacy=Path.Combine(ProjectFolderLayout.GetSettingsFolder(root),"metadata","sources");
        if(Directory.Exists(legacy))
        {
            Directory.CreateDirectory(sourceFolder);
            foreach(var path in SafeFileSystemEnumeration.FilesInDirectory(legacy))
            {
                var target=Path.Combine(sourceFolder,Path.GetFileName(path));
                if(File.Exists(target)){if(!File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(path))){complete=false;continue;}}
                else File.Copy(path,target,false);
                File.Delete(path);changes.Add(mainPath);
            }
        }
        if(Directory.Exists(sourceFolder))
            foreach(var path in SafeFileSystemEnumeration.FilesInDirectory(sourceFolder).Where(p=>p.EndsWith(".json",StringComparison.OrdinalIgnoreCase)&&!p.Contains(".defekt.")))
            {
                try
                {
                    var doc=Read(path);var snapshot=doc["Snapshot"] as JsonObject;if(snapshot is null)continue;
                    var old=Id(snapshot);if(!aliases.TryGetValue(old,out var mapped)||mapped.Count!=1)continue;
                    var id=mapped.Single();if(id==old)continue;
                    var target=ProjectFolderLayout.GetSourceMetadataSnapshotPath(root,id);
                    if(File.Exists(target))continue; // Existing canonical evidence takes priority.
                    snapshot["SourceId"]=id.ToString();
                    new AtomicJsonStore().Save(target,doc,JsonOptions);changes.Add(mainPath);
                }
                catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException){complete=false;}
            }
        if(!Directory.Exists(sourceFolder)){Directory.CreateDirectory(sourceFolder);changes.Add(mainPath);}
        foreach(var path in changes)
            if(docs.TryGetValue(path,out var doc))new AtomicJsonStore().Save(path,doc,JsonOptions);
        return new(changes.Count>0,complete,false) {Repairs=repairs};
    }
}
