using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace BookStitch.Tests.Services;

public sealed class ProjectIntegrityTests
{
    [Fact]
    public async Task Legacy_folder_manifest_conflict_rebuilds_originals_and_keeps_canonical_metadata_and_stable_ids()
    {
        var tool=Environment.GetEnvironmentVariable("BOOKSTITCH_INTEGRITY_FFPROBE");
        if(string.IsNullOrWhiteSpace(tool))return; // Same real-probe contract as the existing rebuild fixtures.
        using var f=new Fixture(true);
        var metadata=new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title,"Canonical title",MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors,new[]{"Canonical author"},MetadataOrigin.User);
        new ProjectMetadataStore().Save(f.Root,metadata);
        var metadataBytes=File.ReadAllBytes(ProjectFolderLayout.GetProjectMetadataPath(f.Root));
        var original=File.ReadAllBytes(f.Source);var timestamp=File.GetLastWriteTimeUtc(f.Source);
        var mainPath=ProjectFolderLayout.GetFolderManifestPath(f.Root);
        var main=ProjectIntegrityValidator.Read(mainPath);main["ProjectFormatVersion"]=0;
        new AtomicJsonStore().Save(mainPath,main,new());
        var legacy=ProjectFolderLayout.GetWorkManifestPath(f.Root);
        const string legacyJson="{\"schemaVersion\":1,\"ProjectType\":\"FolderProject\"}";
        File.WriteAllText(legacy,legacyJson);
        File.WriteAllText(ProjectFolderLayout.GetExportManifestPath(f.Root),"{broken");
        File.Delete(Path.Combine(ProjectFolderLayout.GetSettingsFolder(f.Root),SourceCopyIdentityStore.FileName));
        File.WriteAllText(ProjectFolderLayout.GetTrackListStatePath(f.Root),JsonSerializer.Serialize(new
        {schemaVersion=1,Tracks=new[]{new {Key="path:"+f.Source,SourceId=Guid.Empty,Order=0,IsExcluded=true}}}));
        var before=await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(tool));
        Assert.Equal(ProjectHealthStatus.RebuildRequired,before.Health);
        Assert.Contains(before.Findings,finding=>finding.IssueCode==IntegrityIssueCode.ManifestConflict &&
            finding.TechnicalDetail=="Invalid legacy work manifest.");

        var rebuilder=new ProjectRebuildService(Probes());
        var result=await rebuilder.RebuildAsync(f.Root,new(tool));
        Assert.True(result.Health is ProjectHealthStatus.OK or ProjectHealthStatus.Warning);
        Assert.True(result.WasRepaired);
        Assert.False(File.Exists(legacy));
        var legacyBackup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(legacy)!, "project.*.defekt.json"));
        Assert.Equal(legacyJson, File.ReadAllText(legacyBackup));
        Assert.Equal(metadataBytes,File.ReadAllBytes(ProjectFolderLayout.GetProjectMetadataPath(f.Root)));
        Assert.Equal(original,File.ReadAllBytes(f.Source));Assert.Equal(timestamp,File.GetLastWriteTimeUtc(f.Source));
        var workPath=ProjectFolderLayout.GetExportManifestPath(f.Root);
        var id=ProjectIntegrityValidator.Id(Assert.Single(ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(workPath),"Tracks")));
        Assert.NotEqual(Guid.Empty,id);
        Assert.True(File.Exists(ProjectFolderLayout.GetSourceMetadataSnapshotPath(f.Root,id)));
        Assert.Equal(id,Assert.Single(new SourceCopyIdentityStore().Load(f.Root)).SourceId);
        await rebuilder.RebuildAsync(f.Root,new(tool));
        Assert.Equal(id,ProjectIntegrityValidator.Id(Assert.Single(ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(workPath),"Tracks"))));
        Assert.Equal(original,File.ReadAllBytes(f.Source));Assert.Equal(timestamp,File.GetLastWriteTimeUtc(f.Source));
    }

    [Fact]
    public void Ambiguous_external_suffix_is_stale_evidence_and_never_selects_an_arbitrary_original()
    {
        using var f=new Fixture(true);using var external=new TemporaryFolder();
        var nested=Path.Combine(ProjectFolderLayout.GetOriginalsFolder(f.Root),"nested","originals","one.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);File.WriteAllBytes(nested,[1,2,3]);
        var stale=Path.Combine(external.Path,"originals","nested","originals","one.wav");
        var before=File.ReadAllBytes(f.Source);
        Assert.Throws<ProjectStaleManagedPathException>(()=>ProjectManagedPathService.Resolve(f.Root,stale,"originals"));
        Assert.Equal(before,File.ReadAllBytes(f.Source));Assert.Equal(new byte[]{1,2,3},File.ReadAllBytes(nested));
    }

    [Theory]
    [InlineData(IntegrityIssueCode.RebuildTrackStateLost,false)]
    [InlineData(IntegrityIssueCode.RebuildMetadataLost,false)]
    [InlineData(IntegrityIssueCode.UnknownProjectFile,true)]
    [InlineData(IntegrityIssueCode.UnsafeReparsePoint,true)]
    public void Folder_action_depends_on_actionable_findings(IntegrityIssueCode code,bool expected)
    {
        var item=new ProjectIndexItem {HealthStatus=ProjectHealthStatus.Warning,
            IntegrityFindings=[new(code,ProjectHealthStatus.Warning,RepairActionCode.None,"test")]};
        Assert.Equal(expected,item.ShowProjectFolder);
        item.HealthStatus=ProjectHealthStatus.Damaged;Assert.True(item.ShowProjectFolder);
        item.HealthStatus=ProjectHealthStatus.RebuildRequired;Assert.True(item.ShowProjectFolder);
    }

    [Fact]
    public async Task Unresolved_external_paths_remain_rebuildable_without_reading_external_files()
    {
        using var f=new Fixture(true);using var external=new TemporaryFolder();
        var outside=Path.Combine(external.Path,"one.wav");File.WriteAllBytes(outside,[9,8,7]);
        var exportPath=ProjectFolderLayout.GetExportManifestPath(f.Root);var export=ProjectIntegrityValidator.Read(exportPath);
        ProjectIntegrityValidator.Rows(export,"Tracks").Single()["SourcePath"]=outside;
        ProjectIntegrityValidator.Rows(export,"Tracks").Single()["ConvertedPath"]=Path.Combine(external.Path,"missing.m4a");
        new AtomicJsonStore().Save(exportPath,export,new());
        var original=File.ReadAllBytes(f.Source);var outsideBefore=File.ReadAllBytes(outside);
        var upgrade=new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(f.Root);
        Assert.False(upgrade.SafetyBlocked);Assert.False(upgrade.IsCurrent);
        var inspected=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,f.Cache());
        Assert.DoesNotContain(inspected.Findings,x=>x.IssueCode==IntegrityIssueCode.UnsafeReparsePoint);
        Assert.Contains(inspected.Findings,x=>x.IssueCode==IntegrityIssueCode.StaleManagedPath&&x.HealthImpact==ProjectHealthStatus.RebuildRequired);
        Assert.Equal(f.Source,Assert.Single(inspected.Sources).Path);
        var tool=Environment.GetEnvironmentVariable("BOOKSTITCH_INTEGRITY_FFPROBE");
        if(!string.IsNullOrWhiteSpace(tool))
        {
            var rebuilt=await new ProjectRebuildService(Probes()).RebuildAsync(f.Root,new(tool));
            Assert.False(rebuilt.SafetyBlocked);Assert.True(rebuilt.WasRepaired);
            Assert.True(rebuilt.Health is ProjectHealthStatus.OK or ProjectHealthStatus.Warning);
            Assert.Equal(f.Id,Assert.Single(new WorkManifestService().ReadOnly(exportPath).Tracks).SourceId);
            Assert.False(new ProjectIndexItem {HealthStatus=rebuilt.Health,IntegrityFindings=rebuilt.Findings}.ShowProjectFolder);
            var revalidated=await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(tool));
            Assert.False(revalidated.SafetyBlocked);
            Assert.True(revalidated.Health is ProjectHealthStatus.OK or ProjectHealthStatus.Warning);
        }
        Assert.Equal(original,File.ReadAllBytes(f.Source));Assert.Equal(outsideBefore,File.ReadAllBytes(outside));
    }

    private static AudioProbeBatchService Probes() => new(new AudioInfoService(),new FfprobeConcurrencyPolicy(),new FfprobeCoordinator());
    internal sealed class Fixture:IDisposable
    {
        private readonly TemporaryFolder _folder=new();
        public string Root=>_folder.Path;
        public string Source=>Path.Combine(ProjectFolderLayout.GetOriginalsFolder(Root),"one.wav");
        public Guid Id {get;}=Guid.NewGuid();
        public Fixture(bool source=false)
        {
            ProjectFolderLayout.EnsureProjectFolders(Root);
            var projectId=Guid.NewGuid().ToString();
            new AtomicJsonStore().Save(ProjectFolderLayout.GetFolderManifestPath(Root),new FolderProjectManifest {ProjectId=projectId,ProjectFolder=Root},new());
            if(source)
            {
                using var writer=new BinaryWriter(File.Create(Source));
                writer.Write("RIFF"u8);writer.Write(88236);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);
                writer.Write(44100);writer.Write(88200);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(88200);writer.Write(new byte[88200]);
            }
            var file=new FileInfo(Source);
            var work=new ExportWorkManifest {ProjectId=projectId,ProjectWorkFolder=Root,SelectedPreset="AAC Mono 64 kbps",
                Tracks=source?[new(){SourceId=Id,TrackIndex=1,SourcePath=Source,SourceSizeBytes=file.Length,SourceLastWriteUtcTicks=file.LastWriteTimeUtc.Ticks}]:[]};
            new AtomicJsonStore().Save(ProjectFolderLayout.GetExportManifestPath(Root),work,new());
            new ProjectMetadataStore().Save(Root,new ProjectMetadata());
            new TrackListStateService().Save(Root,source?[new TrackInfo {SourceId=Id,FilePath=Source,Index=1}]:[]);
            if(source)
            {
                new SourceMetadataSnapshotStore().Save(Root,new SourceMetadataSnapshot {SourceId=Id});
                new SourceCopyIdentityStore().RecordCopiedFile(Root,Source,Source,Id);
            }
        }
        public IntegrityProject Cache()
        {
            var p=new IntegrityProject {Root=Root,MainPath=ProjectFolderLayout.GetFolderManifestPath(Root),Main=ProjectIntegrityValidator.Read(ProjectFolderLayout.GetFolderManifestPath(Root))};
            if(File.Exists(Source))p.Probes[Source]=new AudioProbeInfo {Success=true,CodecType="audio",CodecName="pcm_s16le",Channels=1,SampleRateHz=44100,Duration=TimeSpan.FromSeconds(1),StreamCount=1};
            return p;
        }
        public void Dispose()=>_folder.Dispose();
    }
    [Fact] public async Task Copy_identity_and_timestamp_repairs_save_each_document_once_and_reuse_cached_hashes()
    {
        using var f=new Fixture();var p=f.Cache();p.Export=ProjectIntegrityValidator.Read(ProjectFolderLayout.GetExportManifestPath(f.Root));
        var copyPath=Path.Combine(ProjectFolderLayout.GetSettingsFolder(f.Root),SourceCopyIdentityStore.FileName);
        for(var index=0;index<24;index++)
        {
            var path=Path.Combine(ProjectFolderLayout.GetOriginalsFolder(f.Root),index+".wav");File.WriteAllBytes(path,[1,2,3]);
            var id=Guid.NewGuid();var ticks=File.GetLastWriteTimeUtc(path).Ticks;p.Sources.Add(new(id,path,3,ticks,null));
            ((JsonArray)p.Export["Tracks"]!).Add(new JsonObject {["SourceId"]=id.ToString(),["SourceLastWriteUtcTicks"]=0});
            p.Findings.Add(new(IntegrityIssueCode.SourceCopyIdentityMissing,ProjectHealthStatus.Warning,RepairActionCode.RebuildCopyIdentity,"missing",copyPath,id));
            p.Findings.Add(new(IntegrityIssueCode.SourceMetadataMismatch,ProjectHealthStatus.Warning,RepairActionCode.UpdateSourceEvidence,"timestamp",path,id));
            await ProjectIntegrityValidator.HashWithCacheAsync(p,path,default);
        }
        var exportPath=ProjectFolderLayout.GetExportManifestPath(f.Root);
        var revisions=new[]{p.MainPath,exportPath,copyPath}.ToDictionary(path=>path,path=>AtomicJsonStore.ForPath(path).Revision);
        var trace=new PerformanceTraceService(enabled:true);trace.TryStartSession(new());var performance=new ProjectIntegrityPerformance(trace);performance.Begin();
        Assert.Equal(48,(await new ProjectRepairService().RepairAsync(p,default,performance)).Count);performance.End();
        foreach(var (path,before) in revisions)Assert.Equal(before+1,AtomicJsonStore.ForPath(path).Revision);
        Assert.Equal(24,ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(copyPath),"Files").Count());
        Assert.All(ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(exportPath),"Tracks"),row=>Assert.True(ProjectIntegrityValidator.Number(row,"SourceLastWriteUtcTicks")>0));
        var operation=Assert.Single(trace.GetCurrentSnapshot()!.Operations);
        Assert.Equal(0,Assert.Single(operation.Metrics,m=>m.Name=="FilesHashed").Value);
        Assert.Equal(0,Assert.Single(operation.Metrics,m=>m.Name=="HashBytes").Value);
    }

    [Fact] public async Task Prepared_invalidations_are_batched_and_never_delete_files_before_manifest_commit()
    {
        using var f=new Fixture();var p=f.Cache();p.Export=ProjectIntegrityValidator.Read(ProjectFolderLayout.GetExportManifestPath(f.Root));
        var paths=new List<string>();
        for(var index=0;index<24;index++)
        {
            var path=Path.Combine(ProjectFolderLayout.GetConvertedFolder(f.Root),index+".m4a");File.WriteAllBytes(path,[1]);paths.Add(path);var id=Guid.NewGuid();
            ((JsonArray)p.Export["Tracks"]!).Add(new JsonObject {["SourceId"]=id.ToString(),["ConvertedPath"]=path,["Status"]="Converted"});
            p.Findings.Add(new(IntegrityIssueCode.PreparedUnreadable,ProjectHealthStatus.Warning,RepairActionCode.InvalidatePrepared,"broken",path,id));
        }
        var manifest=ProjectFolderLayout.GetExportManifestPath(f.Root);var before=AtomicJsonStore.ForPath(manifest).Revision;
        Assert.Equal(24,(await new ProjectRepairService().RepairAsync(p,default)).Count);
        Assert.Equal(before+1,AtomicJsonStore.ForPath(manifest).Revision);
        Assert.All(paths,path=>Assert.False(File.Exists(path)));
        Assert.All(ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(manifest),"Tracks"),row=>Assert.Equal("Pending",ProjectIntegrityValidator.Text(row,"Status")));
        foreach(var path in paths)File.WriteAllBytes(path,[1]);
        File.Delete(manifest);Directory.CreateDirectory(manifest);
        await Assert.ThrowsAnyAsync<IOException>(()=>new ProjectRepairService().RepairAsync(p,default));
        Assert.All(paths,path=>Assert.True(File.Exists(path)));
    }

    [Fact] public async Task Legacy_source_reference_repairs_share_one_track_state_save()
    {
        using var f=new Fixture();var p=f.Cache();var path=ProjectFolderLayout.GetTrackListStatePath(f.Root);var state=ProjectIntegrityValidator.Read(path);
        for(var index=0;index<24;index++)
        {
            var sourcePath=Path.Combine(ProjectFolderLayout.GetOriginalsFolder(f.Root),index+".wav");var canonical=Guid.NewGuid();var legacy=Guid.NewGuid();
            p.Sources.Add(new(canonical,sourcePath,1,0,null));
            ((JsonArray)state["Tracks"]!).Add(new JsonObject {["SourceId"]=legacy.ToString(),["Key"]="path:"+sourcePath});
            p.Findings.Add(new(IntegrityIssueCode.UnknownTrackSourceId,ProjectHealthStatus.Warning,RepairActionCode.CorrectSourceReference,"legacy",path,legacy));
        }
        new AtomicJsonStore().Save(path,state,new());var before=AtomicJsonStore.ForPath(path).Revision;
        Assert.Equal(24,(await new ProjectRepairService().RepairAsync(p,default)).Count);Assert.Equal(before+1,AtomicJsonStore.ForPath(path).Revision);
        var ids=p.Sources.Select(source=>source.Id).ToHashSet();Assert.All(ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(path),"Tracks"),row=>Assert.Contains(ProjectIntegrityValidator.Id(row),ids));
    }

    [Fact] public async Task Hash_cache_reuses_only_unchanged_file_evidence_and_counts_actual_reads()
    {
        using var f=new Fixture(true);var p=f.Cache();var trace=new PerformanceTraceService(enabled:true);trace.TryStartSession(new());var performance=new ProjectIntegrityPerformance(trace);performance.Begin();
        var first=await ProjectIntegrityValidator.HashWithCacheAsync(p,f.Source,default,performance);
        Assert.Equal(first,await ProjectIntegrityValidator.HashWithCacheAsync(p,f.Source,default,performance));
        var oldLength=new FileInfo(f.Source).Length;File.WriteAllBytes(f.Source,[1,2,3]);
        Assert.NotEqual(first,await ProjectIntegrityValidator.HashWithCacheAsync(p,f.Source,default,performance));performance.End();
        var operation=Assert.Single(trace.GetCurrentSnapshot()!.Operations);
        Assert.Equal(2,Assert.Single(operation.Metrics,m=>m.Name=="FilesHashed").Value);
        Assert.Equal(oldLength+3,Assert.Single(operation.Metrics,m=>m.Name=="HashBytes").Value);
    }

    [Theory][InlineData(0,"0 Sek.")][InlineData(59.4,"59 Sek.")][InlineData(59.5,"01:00 Min.")][InlineData(334.8,"05:35 Min.")][InlineData(3660,"61:00 Min.")]
    public void Integrity_duration_rounds_to_whole_seconds_and_keeps_total_minutes(double seconds,string expected)
    {
        var previous=System.Globalization.CultureInfo.CurrentUICulture;
        try {System.Globalization.CultureInfo.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("de-DE");Assert.Equal(expected,ProjectIntegrityPresentation.FormatDuration(TimeSpan.FromSeconds(seconds)));}
        finally {System.Globalization.CultureInfo.CurrentUICulture=previous;}
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void Integrity_summary_keeps_five_compact_lines_and_unambiguous_dates(bool nextDay)
    {
        var culture=System.Globalization.CultureInfo.CurrentCulture;var ui=System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            var start=new DateTime(2026,10,6,14,24,0,DateTimeKind.Local);var end=nextDay?new DateTime(2026,10,7,0,30,0,DateTimeKind.Local):start.AddMinutes(6);
            Assert.Equal(nextDay?"06.10.2026 14:24 – 07.10.2026 00:30":"06.10.2026 14:24 – 14:30",ProjectIntegrityPresentation.FormatRunRange(start.ToUniversalTime(),end.ToUniversalTime()));
            var text=ProjectIntegrityPresentation.Format(new() {RunStartedUtc=start.ToUniversalTime(),RunEndedUtc=end.ToUniversalTime(),RunStatus=ProjectFormatSweepRunStatus.Completed,
                ProcessedProjects=34,TotalProjects=34,CountRepaired=19,CountOK=8,CountWarning=1,CountRebuildRequired=25});
            Assert.Equal(5,text.Split('\n').Length);Assert.Contains("In Ordnung: 8",text);Assert.Contains("Bearbeitet: 34 / 34",text);Assert.Contains("Neuaufbau erforderlich: 25",text);Assert.DoesNotContain("Gesund",text);
        }
        finally {System.Globalization.CultureInfo.CurrentCulture=culture;System.Globalization.CultureInfo.CurrentUICulture=ui;}
    }

    [Fact] public async Task Rebuild_reuses_hash_and_probe_evidence_through_final_revalidation()
    {
        var tool=Environment.GetEnvironmentVariable("BOOKSTITCH_INTEGRITY_FFPROBE");if(string.IsNullOrWhiteSpace(tool))return;
        using var f=new Fixture(true);var trace=new PerformanceTraceService(enabled:true);trace.TryStartSession(new());
        var performance=new ProjectIntegrityPerformance(trace);performance.Begin();
        var result=await new ProjectRebuildService(Probes()).RebuildAsync(f.Root,new(tool,FullContentHashes:true) {Performance=performance});performance.End();
        Assert.Equal(ProjectHealthStatus.OK,result.Health);var operation=Assert.Single(trace.GetCurrentSnapshot()!.Operations);
        Assert.Equal(1,Assert.Single(operation.Metrics,m=>m.Name=="FilesHashed").Value);
        Assert.Equal(1,Assert.Single(operation.Metrics,m=>m.Name=="FilesProbed").Value);
    }

    [Fact] public async Task Empty_unacquired_project_is_OK_and_health_is_persisted()
    {
        using var f=new Fixture();var result=await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(""));
        Assert.Equal(ProjectHealthStatus.OK,result.Health);Assert.False(result.WasRepaired);
        Assert.Equal("OK",ProjectIntegrityValidator.Text(ProjectIntegrityValidator.Read(ProjectFolderLayout.GetFolderManifestPath(f.Root))["Health"],"LastIntegrityStatus"));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Missing_or_corrupt_metadata_is_rebuilt_with_warning_and_backup(bool corrupt)
    {
        using var f=new Fixture();var path=ProjectFolderLayout.GetProjectMetadataPath(f.Root);
        if(corrupt)File.WriteAllText(path,"{broken");else File.Delete(path);
        var result=await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(""));
        Assert.True(result.WasRepaired);Assert.Equal(ProjectHealthStatus.Warning,result.Health);
        Assert.NotNull(new ProjectMetadataStore().LoadOrCreate(f.Root));
        Assert.Equal(corrupt,Directory.EnumerateFiles(Path.GetDirectoryName(path)!,"project-metadata.*.defekt.json").Any());
        if(corrupt)Assert.Contains("Recovery backup:",File.ReadAllText(Path.Combine(ProjectFolderLayout.GetSettingsFolder(f.Root),"project-integrity.log")));
    }
    [Theory][InlineData("missing",IntegrityIssueCode.SourceMissing)][InlineData("empty",IntegrityIssueCode.SourceZeroLength)][InlineData("size",IntegrityIssueCode.SourceSizeMismatch)]
    public async Task Damaged_originals_are_never_repaired_or_replaced(string damage,IntegrityIssueCode code)
    {
        using var f=new Fixture(true);if(damage=="missing")File.Delete(f.Source);else File.WriteAllBytes(f.Source,damage=="empty"?[]:[1]);
        var before=File.Exists(f.Source)?File.ReadAllBytes(f.Source):null;
        var p=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,f.Cache());
        Assert.Contains(p.Findings,x=>x.IssueCode==code&&x.HealthImpact==ProjectHealthStatus.Damaged);
        await new ProjectRepairService().RepairAsync(p,default);
        if(before is null)Assert.False(File.Exists(f.Source));else Assert.Equal(before,File.ReadAllBytes(f.Source));
    }
    [Theory][InlineData("missing")][InlineData("empty")][InlineData("unreadable")]
    public async Task Broken_prepared_is_reset_without_deleting_preset_or_inventing_identity(string kind)
    {
        using var f=new Fixture(true);var presetFolder=Directory.CreateDirectory(Path.Combine(ProjectFolderLayout.GetConvertedFolder(f.Root),"aac_mono_64k")).FullName;
        var path=Path.Combine(presetFolder,"one.m4a");if(kind!="missing")File.WriteAllBytes(path,kind=="empty"?[]:[1,2,3]);
        var work=ProjectIntegrityValidator.Read(ProjectFolderLayout.GetExportManifestPath(f.Root));var row=ProjectIntegrityValidator.Rows(work,"Tracks").Single();
        row["Status"]="Converted";row["Preset"]="AAC Mono 64 kbps";row["ConvertedPath"]=path;row["PreparationId"]=Guid.NewGuid().ToString();row["PreparationVersion"]=1;
        new AtomicJsonStore().Save(ProjectFolderLayout.GetExportManifestPath(f.Root),work,new());
        var cached=f.Cache();cached.Probes[path]=new AudioProbeInfo {Success=false};
        var validator=new ProjectIntegrityValidator(Probes());var p=await validator.ValidateAsync(f.Root,new(""),null,default,cached);
        var repaired=await new ProjectRepairService().RepairAsync(p,default);Assert.Contains(repaired,x=>x.RepairAction==RepairActionCode.InvalidatePrepared);
        var final=await validator.ValidateAsync(f.Root,new(""),null,default,p);
        Assert.DoesNotContain(final.Findings,x=>x.IssueCode is IntegrityIssueCode.PreparedMissing or IntegrityIssueCode.PreparedUnreadable);
        row=ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(ProjectFolderLayout.GetExportManifestPath(f.Root)),"Tracks").Single();
        Assert.Equal("Pending",row["Status"]!.ToString());Assert.Equal(Guid.Empty,Guid.Parse(row["PreparationId"]!.ToString()));
        Assert.True(Directory.Exists(presetFolder));Assert.False(File.Exists(path));
    }
    [Fact] public async Task Timestamp_change_with_matching_reference_hash_is_repaired_without_changing_original()
    {
        using var f=new Fixture(true);var bytes=File.ReadAllBytes(f.Source);File.SetLastWriteTimeUtc(f.Source,DateTime.UtcNow.AddHours(-2));
        var p=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,f.Cache());
        Assert.Contains(p.Findings,x=>x.RepairAction==RepairActionCode.UpdateSourceEvidence);
        await new ProjectRepairService().RepairAsync(p,default);
        var final=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,p);
        Assert.DoesNotContain(final.Findings,x=>x.IssueCode==IntegrityIssueCode.SourceMetadataMismatch);Assert.Equal(bytes,File.ReadAllBytes(f.Source));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Full_hash_option_controls_content_verification_without_prepared_hashes(bool enabled)
    {
        using var f=new Fixture(true);var timestamp=File.GetLastWriteTimeUtc(f.Source);var bytes=File.ReadAllBytes(f.Source);bytes[^1]=1;File.WriteAllBytes(f.Source,bytes);File.SetLastWriteTimeUtc(f.Source,timestamp);
        var p=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new("",FullContentHashes:enabled),null,default,f.Cache());
        Assert.Equal(enabled,p.Findings.Any(x=>x.IssueCode==IntegrityIssueCode.SourceHashMismatch));
    }
    [Fact] public async Task Source_metadata_reconstruction_reuses_probe_cache()
    {
        using var f=new Fixture(true);var path=ProjectFolderLayout.GetSourceMetadataSnapshotPath(f.Root,f.Id);File.WriteAllText(path,"broken");
        var validator=new ProjectIntegrityValidator(Probes());var p=await validator.ValidateAsync(f.Root,new("missing-tool"),null,default,f.Cache());
        var repaired=await new ProjectRepairService().RepairAsync(p,default);Assert.Contains(repaired,x=>x.RepairAction==RepairActionCode.RebuildSourceMetadata);
        Assert.Equal(f.Id,new SourceMetadataSnapshotStore().TryLoad(f.Root,f.Id)!.SourceId);
        var final=await validator.ValidateAsync(f.Root,new("missing-tool"),null,default,p);
        Assert.DoesNotContain(final.Findings,x=>x.IssueCode==IntegrityIssueCode.SourceMetadataCorrupt);
        Assert.Single(final.Probes);
    }
    [Theory][InlineData("desktop.ini",false)][InlineData("Thumbs.db",false)][InlineData("ehthumbs.db",false)][InlineData("notes.ini",true)][InlineData("notes.defekt.json",true)][InlineData("project-integrity.log",true)]
    public async Task Unknown_files_are_retained_and_only_explicit_OS_names_are_ignored(string name,bool warning)
    {
        using var f=new Fixture();var path=Path.Combine(f.Root,name);File.WriteAllText(path,"user content");
        var result=await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(""));
        Assert.Equal(warning,result.Findings.Any(x=>x.IssueCode==IntegrityIssueCode.UnknownProjectFile));Assert.Equal("user content",File.ReadAllText(path));
    }
    [Fact] public async Task Managed_temp_cleanup_is_narrow_and_never_deletes_unknown_tmp()
    {
        using var f=new Fixture();var part=Path.Combine(ProjectFolderLayout.GetConvertedFolder(f.Root),"track.m4a.part");File.WriteAllText(part,"partial");
        var unknown=Path.Combine(f.Root,"user.tmp");File.WriteAllText(unknown,"user");
        var result=await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(""));Assert.True(result.WasRepaired);Assert.False(File.Exists(part));Assert.True(File.Exists(unknown));
    }
    [Fact] public async Task Integrity_log_preserves_history_newest_first_when_warning_resolves()
    {
        using var f=new Fixture();var note=Path.Combine(f.Root,"note.txt");File.WriteAllText(note,"hello");var service=new ProjectIntegrityService(Probes());
        await service.InspectAsync(f.Root,new(""));File.Delete(note);Assert.Equal(ProjectHealthStatus.OK,(await service.InspectAsync(f.Root,new(""))).Health);
        var log=File.ReadAllText(Path.Combine(ProjectFolderLayout.GetSettingsFolder(f.Root),"project-integrity.log"));Assert.True(log.IndexOf("Result: OK")<log.IndexOf("Result: WARNING"));
    }
    [Fact] public async Task Cancellation_is_propagated_without_publishing_checked_health()
    {
        using var f=new Fixture();using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(""),token:cancel.Token));
        Assert.Equal("NotChecked",ProjectIntegrityValidator.Text(ProjectIntegrityValidator.Read(ProjectFolderLayout.GetFolderManifestPath(f.Root))["Health"],"LastIntegrityStatus"));
    }
    [Theory][InlineData("../outside.wav")][InlineData("converted/not-original.wav")]
    public async Task Source_traversal_is_damaged_and_never_touched(string path)
    {
        using var f=new Fixture(true);var work=ProjectIntegrityValidator.Read(ProjectFolderLayout.GetExportManifestPath(f.Root));ProjectIntegrityValidator.Rows(work,"Tracks").Single()["SourcePath"]=path;
        new AtomicJsonStore().Save(ProjectFolderLayout.GetExportManifestPath(f.Root),work,new());
        var p=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,f.Cache());Assert.Contains(p.Findings,x=>x.IssueCode==IntegrityIssueCode.UnsafeReparsePoint);
        Assert.Empty(await new ProjectRepairService().RepairAsync(p,default));
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Invalid_timing_is_discarded_without_reconverting_a_valid_artifact(bool corruptJson)
    {
        using var f=new Fixture(true);var folder=Directory.CreateDirectory(Path.Combine(ProjectFolderLayout.GetConvertedFolder(f.Root),"aac_mono_64k")).FullName;
        var path=Path.Combine(folder,"one.m4a");File.WriteAllBytes(path,[1,2,3]);var file=new FileInfo(path);var preparation=Guid.NewGuid();
        var work=ProjectIntegrityValidator.Read(ProjectFolderLayout.GetExportManifestPath(f.Root));var row=ProjectIntegrityValidator.Rows(work,"Tracks").Single();
        row["Status"]="Converted";row["Preset"]="AAC Mono 64 kbps";row["ConvertedPath"]=path;row["PreparationId"]=preparation.ToString();row["PreparationVersion"]=1;
        row["ConvertedSizeBytes"]=file.Length;row["ConvertedLastWriteUtcTicks"]=file.LastWriteTimeUtc.Ticks;
        row["AacTiming"]=corruptJson?JsonValue.Create("corrupt timing"):JsonSerializer.SerializeToNode(new ConvertedAacTiming(1,100,1,1,1,44100));
        new AtomicJsonStore().Save(ProjectFolderLayout.GetExportManifestPath(f.Root),work,new());
        var cache=f.Cache();cache.Probes[path]=new AudioProbeInfo { Success=true,CodecType="audio",CodecName="aac",Channels=1,SampleRateHz=44100,Duration=TimeSpan.FromSeconds(1) };
        var p=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,cache);
        Assert.Contains(p.Findings,f=>f.RepairAction==RepairActionCode.ResetTiming);await new ProjectRepairService().RepairAsync(p,default);
        row=ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(ProjectFolderLayout.GetExportManifestPath(f.Root)),"Tracks").Single();
        Assert.Equal("Converted",row["Status"]!.ToString());Assert.Equal(preparation,Guid.Parse(row["PreparationId"]!.ToString()));Assert.Null(row["AacTiming"]);Assert.True(File.Exists(path));
    }
    [Fact] public async Task Excluded_originals_remain_in_the_inspection_inventory()
    {
        using var f=new Fixture(true);var work=ProjectIntegrityValidator.Read(ProjectFolderLayout.GetExportManifestPath(f.Root));work["Tracks"]=new JsonArray();
        new AtomicJsonStore().Save(ProjectFolderLayout.GetExportManifestPath(f.Root),work,new());
        var p=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,f.Cache());Assert.Single(p.Sources);Assert.DoesNotContain(p.Findings,x=>x.HealthImpact==ProjectHealthStatus.Damaged);
    }
    [Fact] public async Task Integrity_sweep_resume_skips_completed_inspections_and_publishes_marker_last()
    {
        using var root=new TemporaryFolder();using var one=new Fixture();using var two=new Fixture();using var cancel=new CancellationTokenSource();
        var inspected=0;var integrity=new ProjectIntegrityService(Probes(),_=>{if(++inspected==1)cancel.Cancel();});var state=Path.Combine(root.Path,"sweep.json");
        var first=new ProjectFormatSweepService(state,_=>[one.Root,two.Root],integrity,new(""));
        var summary=await first.RunAsync(root.Path,cancel.Token);Assert.Equal(ProjectFormatSweepRunStatus.Canceled,summary.RunStatus);Assert.Equal(1,summary.ProcessedProjects);Assert.Equal(0,first.ReadState()!.LastIntegritySweepVersion);
        var resumed=new ProjectFormatSweepService(state,_=>throw new Exception("Discovery must not be repeated"),new ProjectIntegrityService(Probes(),_=>inspected++),new(""));
        summary=await resumed.RunAsync(root.Path);Assert.Equal(ProjectFormatSweepRunStatus.Completed,summary.RunStatus);Assert.Equal(2,inspected);Assert.Equal(ProjectIntegrityContract.CurrentVersion,resumed.ReadState()!.LastIntegritySweepVersion);
    }
    [Fact] public async Task Missing_FFprobe_does_not_complete_an_audio_inspection()
    {using var f=new Fixture(true);await Assert.ThrowsAsync<FileNotFoundException>(()=>new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new("not-installed-ffprobe")));}
    [Fact] public async Task Unavailable_preset_uses_the_same_catalog_as_the_UI()
    {
        using var f=new Fixture();var path=ProjectFolderLayout.GetExportManifestPath(f.Root);
        var work=ProjectIntegrityValidator.Read(path);work["SelectedPreset"]="AAC Mono 320 kbps";
        new AtomicJsonStore().Save(path,work,new());
        var result=await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(""));
        Assert.Equal(ProjectHealthStatus.Warning,result.Health);
        Assert.Contains(result.Findings,x=>x.IssueCode==IntegrityIssueCode.PresetUnavailable);
    }
    [Fact] public async Task Diagnostic_log_failure_does_not_undo_persisted_health()
    {
        using var f=new Fixture();File.WriteAllText(Path.Combine(f.Root,"note.txt"),"retained");
        Directory.CreateDirectory(Path.Combine(ProjectFolderLayout.GetSettingsFolder(f.Root),"project-integrity.log"));
        var errors=0;var result=await new ProjectIntegrityService(Probes(),error:(_,_)=>errors++).InspectAsync(f.Root,new(""));
        Assert.Equal(ProjectHealthStatus.Warning,result.Health);Assert.Equal(1,errors);
        Assert.Equal("Warning",ProjectIntegrityValidator.Text(ProjectIntegrityValidator.Read(ProjectFolderLayout.GetFolderManifestPath(f.Root))["Health"],"LastIntegrityStatus"));
    }
    [Fact] public async Task Real_FFprobe_inspection_reuses_normalized_probe_after_metadata_repair()
    {
        var tool=Environment.GetEnvironmentVariable("BOOKSTITCH_INTEGRITY_FFPROBE");
        if(string.IsNullOrWhiteSpace(tool))return;
        Assert.True(File.Exists(tool));using var f=new Fixture(true);
        File.Delete(ProjectFolderLayout.GetSourceMetadataSnapshotPath(f.Root,f.Id));
        var progress=new List<IntegrityProgress>();
        var result=await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(tool),new InlineProgress(progress.Add));
        Assert.Equal(ProjectHealthStatus.OK,result.Health);Assert.True(result.WasRepaired);
        Assert.Single(progress,x=>x.TotalFiles>0&&x.CompletedFiles==x.TotalFiles);
        Assert.NotNull(new SourceMetadataSnapshotStore().TryLoad(f.Root,f.Id));
    }
    private sealed class InlineProgress(Action<IntegrityProgress> report):IProgress<IntegrityProgress>
    {public void Report(IntegrityProgress value)=>report(value);}

    [Theory][InlineData("{broken")][InlineData("{\"schemaVersion\":1}")]
    public async Task Malformed_copy_identity_is_backed_up_and_rebuilt_only_from_known_sources(string json)
    {
        using var f=new Fixture(true);var path=Path.Combine(ProjectFolderLayout.GetSettingsFolder(f.Root),SourceCopyIdentityStore.FileName);
        File.WriteAllText(path,json);var validator=new ProjectIntegrityValidator(Probes());
        var p=await validator.ValidateAsync(f.Root,new(""),null,default,f.Cache());
        Assert.Contains(await new ProjectRepairService().RepairAsync(p,default),x=>x.RepairAction==RepairActionCode.RebuildCopyIdentity);
        Assert.Single(new SourceCopyIdentityStore().Load(f.Root));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(path)!,"source-copy-identities.*.defekt.json"));
        var final=await validator.ValidateAsync(f.Root,new(""),null,default,p);
        Assert.DoesNotContain(final.Findings,x=>x.IssueCode==IntegrityIssueCode.SourceCopyIdentityCorrupt);
    }

    [Fact] public void Mp3_relative_path_is_relative_to_originals_and_does_not_duplicate_disc()
    {
        using var f=new Fixture();var expected=Path.Combine(ProjectFolderLayout.GetOriginalsFolder(f.Root),"CD 01","one.wav");
        Assert.Equal(expected,Mp3DiscProjectService.GetOriginalSourcePath(f.Root,Path.Combine("CD 01","one.wav")));
    }
    [Fact] public async Task Old_managed_root_is_safely_rebased_when_the_same_relative_source_exists()
    {
        using var f=new Fixture(true);var work=ProjectIntegrityValidator.Read(ProjectFolderLayout.GetExportManifestPath(f.Root));
        var old=Path.Combine(Path.GetTempPath(),"old-bookstitch-root",Guid.NewGuid().ToString());
        work["ProjectWorkFolder"]=old;ProjectIntegrityValidator.Rows(work,"Tracks").Single()["SourcePath"]=Path.Combine(old,"originals","one.wav");
        new AtomicJsonStore().Save(ProjectFolderLayout.GetExportManifestPath(f.Root),work,new());
        var p=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,f.Cache());
        Assert.DoesNotContain(p.Findings,x=>x.IssueCode==IntegrityIssueCode.UnsafeReparsePoint);Assert.Equal(f.Source,Assert.Single(p.Sources).Path);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Stale_paths_reconcile_all_references_before_unknown_file_detection(bool rootsAlreadyCurrent)
    {
        using var f=new Fixture(true);
        var old=Path.Combine(Path.GetTempPath(),"previous-bookstitch",Guid.NewGuid().ToString());
        var mainPath=ProjectFolderLayout.GetFolderManifestPath(f.Root);
        var main=ProjectIntegrityValidator.Read(mainPath);main["ProjectFolder"]=rootsAlreadyCurrent?f.Root:old;
        new AtomicJsonStore().Save(mainPath,main,new());
        var exportPath=ProjectFolderLayout.GetExportManifestPath(f.Root);var export=ProjectIntegrityValidator.Read(exportPath);
        export["ProjectWorkFolder"]=rootsAlreadyCurrent?f.Root:old;
        var row=ProjectIntegrityValidator.Rows(export,"Tracks").Single();row["SourcePath"]=Path.Combine(old,"originals","one.wav");
        var converted=Path.Combine(ProjectFolderLayout.GetConvertedFolder(f.Root),"one.m4a");File.WriteAllBytes(converted,[1]);
        row["ConvertedPath"]=Path.Combine(old,"converted","one.m4a");
        new AtomicJsonStore().Save(exportPath,export,new());
        var copiesPath=Path.Combine(ProjectFolderLayout.GetSettingsFolder(f.Root),SourceCopyIdentityStore.FileName);
        var copies=ProjectIntegrityValidator.Read(copiesPath);ProjectIntegrityValidator.Rows(copies,"Files").Single()["TargetRelativePath"]=Path.Combine(old,"originals","one.wav");
        new AtomicJsonStore().Save(copiesPath,copies,new());
        var upgrade=new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(f.Root);
        Assert.False(upgrade.SafetyBlocked);Assert.True(upgrade.IsCurrent);
        Assert.Equal(3,upgrade.Repairs.Count);Assert.All(upgrade.Repairs,r=>Assert.Equal(IntegrityIssueCode.StaleManagedPath,r.IssueCode));
        row=ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(exportPath),"Tracks").Single();
        Assert.Equal(f.Source,ProjectIntegrityValidator.Text(row,"SourcePath"));Assert.Equal(converted,ProjectIntegrityValidator.Text(row,"ConvertedPath"));
        var p=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,f.Cache());
        Assert.DoesNotContain(p.Findings,x=>x.IssueCode is IntegrityIssueCode.UnsafeReparsePoint or IntegrityIssueCode.UnknownProjectFile);
        Assert.Empty(new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(f.Root).Repairs);
        var tool=Environment.GetEnvironmentVariable("BOOKSTITCH_INTEGRITY_FFPROBE");
        if(!string.IsNullOrWhiteSpace(tool))
        {
            row["SourcePath"]=Path.Combine(old,"originals","one.wav");new AtomicJsonStore().Save(exportPath,export,new());
            var inspected=await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(tool));
            Assert.True(inspected.WasRepaired);Assert.False(inspected.SafetyBlocked);Assert.Equal(ProjectHealthStatus.OK,inspected.Health);
            Assert.Contains(inspected.Findings,r=>r.IssueCode==IntegrityIssueCode.StaleManagedPath&&r.WasRepaired);
            Assert.DoesNotContain(inspected.Findings,r=>r.IssueCode==IntegrityIssueCode.UnknownProjectFile);
        }
    }

    [Theory][InlineData("../outside.wav")][InlineData("converted/originals/one.wav")]
    public async Task Unsafe_paths_block_the_common_inspection_without_reconciliation_writes(string unsafePath)
    {
        using var f=new Fixture(true);var path=ProjectFolderLayout.GetExportManifestPath(f.Root);
        var export=ProjectIntegrityValidator.Read(path);ProjectIntegrityValidator.Rows(export,"Tracks").Single()["SourcePath"]=unsafePath;
        new AtomicJsonStore().Save(path,export,new());var before=File.ReadAllText(path);
        var result=await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(""));
        Assert.True(result.SafetyBlocked);Assert.Equal(before,File.ReadAllText(path));
    }

    [Fact] public async Task Real_reparse_entry_remains_safety_blocked()
    {
        using var f=new Fixture();using var outside=new TemporaryFolder();var link=Path.Combine(f.Root,"linked");
        if(OperatingSystem.IsWindows())
        {
            var start=new System.Diagnostics.ProcessStartInfo("cmd.exe") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in new[]{"/c","mklink","/J",link,outside.Path})start.ArgumentList.Add(argument);
            using var process=System.Diagnostics.Process.Start(start)!;process.WaitForExit();Assert.Equal(0,process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link,outside.Path);
        try {Assert.True((await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(""))).SafetyBlocked);}
        finally {Directory.Delete(link);}
    }

    [Fact] public async Task Empty_rebuild_reports_real_steps_without_loss_warnings()
    {
        using var f=new Fixture();var updates=new List<IntegrityProgress>();
        var result=await new ProjectRebuildService(Probes()).RebuildAsync(f.Root,new(""),progress:new ImmediateProgress(updates.Add));
        Assert.True(result.WasRepaired);Assert.Equal(ProjectHealthStatus.OK,result.Health);
        Assert.Equal(Enumerable.Range(0,9),updates.Select(p=>p.CompletedFiles));Assert.All(updates,p=>Assert.Equal(8,p.TotalFiles));
        Assert.DoesNotContain(result.Findings,f=>f.IssueCode is IntegrityIssueCode.RebuildTrackStateLost or IntegrityIssueCode.RebuildMetadataLost);
    }
    private sealed class ImmediateProgress(Action<IntegrityProgress> report):IProgress<IntegrityProgress>
    {public void Report(IntegrityProgress value)=>report(value);}

    [Fact] public async Task Known_merge_artifacts_are_not_unknown_files()
    {
        using var f=new Fixture();foreach(var name in new[]{"concat-list.txt","chapters.ffmetadata"})File.WriteAllText(Path.Combine(ProjectFolderLayout.GetMergeFolder(f.Root),name),"managed");
        Assert.Equal(ProjectHealthStatus.OK,(await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(""))).Health);
    }

    [Fact] public async Task Legacy_mp3_empty_and_conflicting_ids_reconcile_from_acquisition_evidence()
    {
        using var f=new Fixture(true);var cache=f.Cache();var main=cache.Main;
        main["ProjectType"]=ProjectManifestTypes.Mp3DiscProject;main["FormatVersion"]="2";
        main["ImportedDiscs"]=new JsonArray(new JsonObject { ["LocalFolder"]=Path.Combine("originals","CD 01"),
            ["SourceFiles"]=new JsonArray(new JsonObject { ["SourceId"]=f.Id.ToString(),["RelativePath"]="one.wav",["SourceSizeBytes"]=new FileInfo(f.Source).Length }) });
        var mainPath=ProjectFolderLayout.GetMp3DiscManifestPath(f.Root);new AtomicJsonStore().Save(mainPath,main,new());File.Delete(ProjectFolderLayout.GetFolderManifestPath(f.Root));
        var exportPath=ProjectFolderLayout.GetExportManifestPath(f.Root);var export=ProjectIntegrityValidator.Read(exportPath);
        export["ProjectType"]=ProjectManifestTypes.Mp3DiscProject;ProjectIntegrityValidator.Rows(export,"Tracks").Single()["SourceId"]=Guid.Empty.ToString();new AtomicJsonStore().Save(exportPath,export,new());
        var result=new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(f.Root);Assert.True(result.IsCurrent);
        Assert.Equal(f.Id,ProjectIntegrityValidator.Id(ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(exportPath),"Tracks").Single()));
        var inspected=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,cache);
        Assert.Empty(inspected.Findings);Assert.False(new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(f.Root).Outcome==ProjectUpgradeOutcome.Upgraded);
    }
    [Fact] public async Task All_empty_source_ids_with_healthy_originals_require_rebuild_not_damage()
    {
        using var f=new Fixture(true);var exportPath=ProjectFolderLayout.GetExportManifestPath(f.Root);var export=ProjectIntegrityValidator.Read(exportPath);
        ProjectIntegrityValidator.Rows(export,"Tracks").Single()["SourceId"]=Guid.Empty.ToString();new AtomicJsonStore().Save(exportPath,export,new());
        File.Delete(Path.Combine(ProjectFolderLayout.GetSettingsFolder(f.Root),SourceCopyIdentityStore.FileName));
        var inspected=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,f.Cache());
        Assert.Contains(inspected.Findings,x=>x.HealthImpact==ProjectHealthStatus.RebuildRequired);Assert.DoesNotContain(inspected.Findings,x=>x.HealthImpact==ProjectHealthStatus.Damaged);
    }
    [Fact] public async Task Original_copying_recovery_evidence_is_never_cleaned_up()
    {
        using var f=new Fixture();var path=Path.Combine(ProjectFolderLayout.GetOriginalsFolder(f.Root),"source.copying");File.WriteAllText(path,"recovery evidence");
        await new ProjectIntegrityService(Probes()).InspectAsync(f.Root,new(""));Assert.Equal("recovery evidence",File.ReadAllText(path));
    }

    [Fact] public async Task Explicit_rebuild_preserves_original_bytes_names_and_user_track_state()
    {
        var tool=Environment.GetEnvironmentVariable("BOOKSTITCH_INTEGRITY_FFPROBE");if(string.IsNullOrWhiteSpace(tool))return;
        using var f=new Fixture(true);var bytes=File.ReadAllBytes(f.Source);var ticks=File.GetLastWriteTimeUtc(f.Source);
        new TrackListStateService().Save(f.Root,[new TrackInfo {SourceId=f.Id,FilePath=f.Source,Index=1,IsExcluded=true,ChapterTitle="User chapter",ExcludedChapterTitle="Excluded user chapter"}]);
        var workPath=ProjectFolderLayout.GetExportManifestPath(f.Root);var export=ProjectIntegrityValidator.Read(workPath);export["State"]="broken";new AtomicJsonStore().Save(workPath,export,new());
        var result=await new ProjectRebuildService(Probes()).RebuildAsync(f.Root,new(tool));
        Assert.True(result.WasRepaired);Assert.DoesNotContain(result.Findings,x=>x.HealthImpact==ProjectHealthStatus.Damaged);
        Assert.Equal(bytes,File.ReadAllBytes(f.Source));Assert.Equal(ticks,File.GetLastWriteTimeUtc(f.Source));Assert.Single(Directory.GetFiles(ProjectFolderLayout.GetOriginalsFolder(f.Root)));
        var track=ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(ProjectFolderLayout.GetTrackListStatePath(f.Root)),"Tracks").Single();
        Assert.Equal("true",track["IsExcluded"]!.ToString().ToLowerInvariant());Assert.Equal("User chapter",track["ChapterTitle"]!.ToString());
        Assert.Equal("Excluded user chapter",track["ExcludedChapterTitle"]!.ToString());
        var pending=ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(workPath),"Tracks").Single();Assert.Equal("Pending",pending["Status"]!.ToString());Assert.Equal(Guid.Empty,Guid.Parse(pending["PreparationId"]!.ToString()));
    }
    [Fact] public async Task Derived_manifest_conflict_does_not_block_independent_directory_repair()
    {
        using var f=new Fixture(true);var path=ProjectFolderLayout.GetMergeFolder(f.Root);Directory.Delete(path);
        var project=f.Cache();project.Sources.Add(new(f.Id,f.Source,new FileInfo(f.Source).Length,File.GetLastWriteTimeUtc(f.Source).Ticks,null));
        project.Findings.Add(new(IntegrityIssueCode.ManifestConflict,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"conflict"));
        project.Findings.Add(new(IntegrityIssueCode.StandardDirectoryMissing,ProjectHealthStatus.Warning,RepairActionCode.CreateDirectory,"missing",path));
        Assert.Single(await new ProjectRepairService().RepairAsync(project,default));Assert.True(Directory.Exists(path));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task Integrity_performance_reports_real_probe_and_optional_reference_hash_work(bool fullHashes)
    {
        var tool=Environment.GetEnvironmentVariable("BOOKSTITCH_INTEGRITY_FFPROBE");if(string.IsNullOrWhiteSpace(tool))return;
        using var f=new Fixture(true);using var state=new TemporaryFolder();var trace=new PerformanceTraceService(enabled:true);trace.TryStartSession(new());
        var options=new ProjectIntegrityOptions(tool,FullContentHashes:fullHashes) {Performance=new ProjectIntegrityPerformance(trace)};
        var sweep=new ProjectFormatSweepService(Path.Combine(state.Path,"sweep.json"),_=>[f.Root],new ProjectIntegrityService(Probes()),options);
        await sweep.RunAsync(state.Path);
        var operation=Assert.Single(trace.GetCurrentSnapshot()!.Operations);Assert.Equal("project.integrity.sweep",operation.Name);
        Assert.Equal(1,Assert.Single(operation.Metrics,m=>m.Name=="FilesProbed").Value);
        Assert.Equal(fullHashes?1:0,Assert.Single(operation.Metrics,m=>m.Name=="FilesHashed").Value);
        Assert.Contains(operation.Phases,p=>p.Name=="ffprobe");Assert.Contains(operation.Phases,p=>p.Name=="persist");Assert.Equal(0,trace.ActiveScopeCount);
    }

    [Fact] public async Task Early_audio_cd_requires_only_ripped_originals_not_pending_tracks()
    {
        using var f=new Fixture(true);var cache=f.Cache();var main=cache.Main;var next=Guid.NewGuid();
        main["ProjectType"]=ProjectManifestTypes.AudioCdProject;main["FormatVersion"]="4";
        main["Discs"]=new JsonArray(new JsonObject {["Tracks"]=new JsonArray(
            new JsonObject {["SourceId"]=f.Id.ToString(),["Status"]="Ripped",["RelativePath"]="originals/one.wav",["OutputSizeBytes"]=new FileInfo(f.Source).Length},
            new JsonObject {["SourceId"]=next.ToString(),["Status"]="Pending",["RelativePath"]="originals/not-ripped.wav"})});
        var mainPath=ProjectFolderLayout.GetAudioDiscManifestPath(f.Root);new AtomicJsonStore().Save(mainPath,main,new());File.Delete(ProjectFolderLayout.GetFolderManifestPath(f.Root));
        File.Delete(Path.Combine(ProjectFolderLayout.GetSettingsFolder(f.Root),SourceCopyIdentityStore.FileName));
        var path=ProjectFolderLayout.GetExportManifestPath(f.Root);var export=ProjectIntegrityValidator.Read(path);export["ProjectType"]=ProjectManifestTypes.AudioCdProject;
        ((JsonArray)export["Tracks"]!).Add(new JsonObject {["SourceId"]=next.ToString(),["SourcePath"]=Path.Combine(f.Root,"originals","not-ripped.wav"),["Status"]="Pending"});new AtomicJsonStore().Save(path,export,new());
        var inspected=await new ProjectIntegrityValidator(Probes()).ValidateAsync(f.Root,new(""),null,default,cache);
        Assert.Single(inspected.Sources);Assert.DoesNotContain(inspected.Findings,x=>x.HealthImpact==ProjectHealthStatus.Damaged);
    }
    [Fact] public async Task Old_integrity_contract_sweep_is_restarted_and_does_not_skip_old_finished_projects()
    {
        using var f=new Fixture();using var state=new TemporaryFolder();var path=Path.Combine(state.Path,"sweep.json");
        new AtomicJsonStore().Save(path,new ProjectFormatSweepState {WorkingRoot=state.Path,IsIntegrityRun=true,IntegrityContractVersion=1,
            LastIntegritySweepVersion=1,DiscoveryCompleted=true,LastRun=new(){RunStatus=ProjectFormatSweepRunStatus.Canceled,ProcessedProjects=1},FinishedProjects=new() {[f.Root]=ProjectUpgradeOutcome.OK}},new());
        var discovered=0;var sweep=new ProjectFormatSweepService(path,_=>{discovered++;return [f.Root];},new ProjectIntegrityService(Probes()),new(""));
        await sweep.RunAsync(state.Path);Assert.Equal(1,discovered);Assert.Equal(ProjectIntegrityContract.CurrentVersion,sweep.ReadState()!.LastIntegritySweepVersion);
        Assert.Equal(ProjectIntegrityContract.CurrentVersion,ProjectIntegrityValidator.Number(ProjectIntegrityValidator.Read(ProjectFolderLayout.GetFolderManifestPath(f.Root))["Health"],"LastIntegrityContractVersion"));
    }
    [Theory][InlineData(ProjectHealthStatus.Damaged)][InlineData(ProjectHealthStatus.RebuildRequired)]
    public void Problematic_health_is_loadable_but_real_safety_remains_blocked(ProjectHealthStatus health)
    {
        var project=new ProjectIndexItem {HealthStatus=health};
        Assert.True(project.CanAttemptLoad);
        project.IntegrityFindings=[new(IntegrityIssueCode.UnsafeReparsePoint,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"safety")];
        Assert.False(project.CanAttemptLoad);Assert.True(project.ShowProjectFolder);
    }

    [Fact] public async Task Explicit_folder_rebuild_recovers_unindexed_originals_with_stable_identity_and_no_original_writes()
    {
        var tool=Environment.GetEnvironmentVariable("BOOKSTITCH_INTEGRITY_FFPROBE");if(string.IsNullOrWhiteSpace(tool))return;
        using var f=new Fixture(true);var before=File.ReadAllBytes(f.Source);var timestamp=File.GetLastWriteTimeUtc(f.Source);
        var exportPath=ProjectFolderLayout.GetExportManifestPath(f.Root);File.WriteAllText(exportPath,"{corrupt");
        File.WriteAllText(Path.Combine(ProjectFolderLayout.GetSettingsFolder(f.Root),SourceCopyIdentityStore.FileName),"{corrupt");
        var rebuilder=new ProjectRebuildService(Probes());var first=await rebuilder.RebuildAsync(f.Root,new(tool));
        Assert.True(first.WasRepaired);Assert.Equal(ProjectHealthStatus.Warning,first.Health);
        var id=ProjectIntegrityValidator.Id(Assert.Single(ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(exportPath),"Tracks")));Assert.NotEqual(Guid.Empty,id);
        await rebuilder.RebuildAsync(f.Root,new(tool));
        Assert.Equal(id,ProjectIntegrityValidator.Id(Assert.Single(ProjectIntegrityValidator.Rows(ProjectIntegrityValidator.Read(exportPath),"Tracks"))));
        Assert.Equal(before,File.ReadAllBytes(f.Source));Assert.Equal(timestamp,File.GetLastWriteTimeUtc(f.Source));
    }

}
