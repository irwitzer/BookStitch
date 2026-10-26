using BookStitch.Models;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using static BookStitch.Services.ProjectIntegrityValidator;
namespace BookStitch.Services;

/// <summary>Owns upgrade, inspection, whitelist repairs, revalidation and health persistence.</summary>
public sealed class ProjectIntegrityService
{
    private readonly ProjectIntegrityValidator _validator;
    private readonly ProjectRepairService _repair = new();
    private readonly Action<string>? _log;
    private readonly Action<string,Exception>? _error;
    public ProjectIntegrityService(AudioProbeBatchService probes,Action<string>? log=null,Action<string,Exception>? error=null)
    { _validator=new(probes);_log=log;_error=error; }
    public Task<ProjectIntegrityResult> InspectAsync(string folder,ProjectIntegrityOptions options,
        IProgress<IntegrityProgress>? progress=null,CancellationToken token=default) => InspectAsync(folder,options,progress,token,null);
    internal async Task<ProjectIntegrityResult> InspectAsync(string folder,ProjectIntegrityOptions options,
        IProgress<IntegrityProgress>? progress,CancellationToken token,IntegrityProject? previous)
    {
        token.ThrowIfCancellationRequested();
        ProjectUpgradeResult upgrade;
        using(options.Performance?.Phase("upgrade"))upgrade=new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder,options.Performance);
        IntegrityProject? project=null;
        var repairs=upgrade.Repairs.ToArray();
        var persisting=false;
        try
        {
            if(!upgrade.IsCurrent && (upgrade.ProjectFormatVersion!=1||upgrade.SafetyBlocked))
                return PersistFailed(folder,upgrade);
            project=await _validator.ValidateAsync(folder,options,progress,token,previous).ConfigureAwait(false);
            if(upgrade.Outcome==ProjectUpgradeOutcome.RebuildRequired)
                Add(project,IntegrityIssueCode.ManifestConflict,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,upgrade.Detail??"Project reconciliation is incomplete.");
            progress?.Report(new("Integrity.Phase.Repair",folder));
            using(options.Performance?.Phase("repair"))repairs=repairs.Concat(await _repair.RepairAsync(project,token,options.Performance).ConfigureAwait(false)).ToArray();
            options.Performance?.Add("Repairs",repairs.Length);
            if(repairs.Length>0)
                using(options.Performance?.Phase("revalidate"))project=await _validator.ValidateAsync(folder,options,progress,token,project).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var findings=project.Findings.ToList();
            // Potential loss of user edits must remain visible even after a functional rebuild.
            findings.AddRange(repairs.Where(f=>f.RepairAction == RepairActionCode.RebuildProjectMetadata ||
                f.RepairAction == RepairActionCode.RebuildTrackList && project.Sources.Count > 0)
                .Select(f=>f with { HealthImpact=ProjectHealthStatus.Warning,WasRepaired=true }));
            var health=findings.Any(f=>f.HealthImpact==ProjectHealthStatus.Damaged)?ProjectHealthStatus.Damaged:
                findings.Any(f=>f.HealthImpact==ProjectHealthStatus.RebuildRequired)?ProjectHealthStatus.RebuildRequired:
                findings.Count>0?ProjectHealthStatus.Warning:ProjectHealthStatus.OK;
            findings.AddRange(repairs.Where(f=>f.IssueCode==IntegrityIssueCode.StaleManagedPath));
            var result=new ProjectIntegrityResult(folder,health,upgrade.Outcome==ProjectUpgradeOutcome.Upgraded,repairs.Length>0,findings);
            persisting=true; using(options.Performance?.Phase("persist"))Persist(project,result,repairs);return result;
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(FileNotFoundException ex) when(ex.FileName==options.FfprobePath){throw;}
        catch(Exception ex) when(!persisting && (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or InvalidOperationException))
        {
            var result=new ProjectIntegrityResult(folder,ProjectHealthStatus.RebuildRequired,upgrade.Outcome==ProjectUpgradeOutcome.Upgraded,repairs.Length>0,
                [new(IntegrityIssueCode.ManifestCorrupt,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,ex.Message)]);
            if(project is not null&&project.Safe)Persist(project,result,repairs);
            _error?.Invoke("PROJECT INTEGRITY",ex);return result;
        }
    }
    internal static ProjectIntegrityResult? ReadPersistedResult(string root)
    {
        try
        {
            var path = Path.Combine(ProjectFolderLayout.GetSettingsFolder(root), "project-integrity.json");
            if (!File.Exists(path)) return null;
            var json = Read(path);
            if (json["Health"] is null || json["Findings"] is not JsonArray) return null;
            var result = System.Text.Json.JsonSerializer.Deserialize<ProjectIntegrityResult>(json.ToJsonString(), JsonOptions);
            if (result is null || !Enum.IsDefined(result.Health) || result.Findings is null ||
                result.Findings.Any(f => f is null || !Enum.IsDefined(f.IssueCode) || !Enum.IsDefined(f.HealthImpact))) return null;
            if (json["SafetyBlocked"] is JsonValue safety && safety.TryGetValue<bool>(out var blocked) && blocked && !result.SafetyBlocked)
                result = result with { Findings = result.Findings.Append(new IntegrityFinding(IntegrityIssueCode.UnsafeReparsePoint,
                    result.Health, RepairActionCode.None, "Persisted integrity safety block.")).ToArray() };
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or FormatException)
        { return null; }
    }

    internal void PersistResult(string root,ProjectIntegrityResult result)
    {
        var path=new[]{ProjectFolderLayout.GetFolderManifestPath(root),ProjectFolderLayout.GetMp3DiscManifestPath(root),ProjectFolderLayout.GetAudioDiscManifestPath(root)}.Single(File.Exists);
        Persist(new IntegrityProject {Root=root,MainPath=path,Main=Read(path)},result,[]);
    }
    private ProjectIntegrityResult PersistFailed(string root,ProjectUpgradeResult upgrade)
    {
        var result=new ProjectIntegrityResult(root,ProjectHealthStatus.RebuildRequired,false,false,
            [new(upgrade.SafetyBlocked?IntegrityIssueCode.UnsafeReparsePoint:IntegrityIssueCode.ManifestConflict,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,upgrade.Detail??"Project format upgrade failed.")]);
        try
        {
            SafeFileSystemEnumeration.EnsureSafeTree(root);
            var paths=new[]{ProjectFolderLayout.GetFolderManifestPath(root),ProjectFolderLayout.GetMp3DiscManifestPath(root),ProjectFolderLayout.GetAudioDiscManifestPath(root)}.Where(File.Exists).ToArray();
            new AtomicJsonStore().Save(Path.Combine(ProjectFolderLayout.GetSettingsFolder(root),"project-integrity.json"),result,JsonOptions);
            if(paths.Length==1) Persist(new IntegrityProject { Root=root, MainPath=paths[0], Main=Read(paths[0]) },result,[]);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        { _error?.Invoke("PROJECT INTEGRITY HEALTH",ex); }
        _log?.Invoke(upgrade.Detail??"Project upgrade failed.");
        return result;
    }
    private void Persist(IntegrityProject p,ProjectIntegrityResult result,IReadOnlyList<IntegrityFinding> repairs)
    {
        if(!p.Safe)return;
        var previous=Text(p.Main["Health"],"LastIntegrityStatus");
        p.Main["Health"]=System.Text.Json.JsonSerializer.SerializeToNode(new ProjectHealth {
            LastIntegrityCheckUtc=DateTime.UtcNow,LastIntegrityCheckProjectFormatVersion=ProjectFormat.CurrentProjectFormatVersion,LastIntegrityContractVersion=ProjectIntegrityContract.CurrentVersion,
            LastIntegrityStatus=result.Health.ToString(),Findings=result.Findings.ToList() });
        var store=new AtomicJsonStore();store.Save(p.MainPath,p.Main,JsonOptions);
        store.Save(Path.Combine(ProjectFolderLayout.GetSettingsFolder(p.Root),"project-integrity.json"),result,JsonOptions);
        if(result.Health!=ProjectHealthStatus.OK||repairs.Count>0||previous is "Warning" or "RebuildRequired" or "Damaged")
        {
            var logPath=Path.Combine(ProjectFolderLayout.GetSettingsFolder(p.Root),"project-integrity.log");
            var entry=new StringBuilder().AppendLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC]")
                .AppendLine("Result: "+result.Health.ToString().ToUpperInvariant())
                .AppendLine("ProjectFormatVersion: "+ProjectFormat.CurrentProjectFormatVersion)
                .AppendLine($"WasUpgraded: {result.WasUpgraded}; WasRepaired: {result.WasRepaired}");
            foreach(var backup in p.RecoveryBackups)entry.AppendLine("Recovery backup: "+backup);
            foreach(var f in repairs)entry.AppendLine($"Repair: {f.RepairAction}; {f.IssueCode}; {f.Path}");
            foreach(var f in result.Findings)entry.AppendLine($"{f.HealthImpact}: {f.IssueCode}; {f.TechnicalDetail}; {f.Path}");
            if(result.Findings.Count==0)entry.AppendLine("Previous issues are resolved. All inspected contracts are valid.");
            try
            {
            var text=entry.AppendLine().ToString()+(File.Exists(logPath)?File.ReadAllText(logPath,Encoding.UTF8):"");
            var tmp=logPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(tmp,text,new UTF8Encoding(false));File.Move(tmp,logPath,true);}
            finally{try{if(File.Exists(tmp))File.Delete(tmp);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}}
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            { _error?.Invoke("PROJECT INTEGRITY LOG",ex); }
        }
        _log?.Invoke($"{p.Root}: {result.Health}; upgraded={result.WasUpgraded}; repaired={result.WasRepaired}");
        if(result.Findings.Count>0)_log?.Invoke(string.Join(Environment.NewLine,result.Findings.Select(f=>$"{p.Root}: {f.IssueCode}; {f.RepairAction}; {f.TechnicalDetail}; {f.Path}; SourceId={f.SourceId}; repaired={f.WasRepaired}")));
    }
}
