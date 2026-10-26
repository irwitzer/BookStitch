using BookStitch.Models;
namespace BookStitch.Services;
public static class ProjectIntegrityPresentation
{
    public static string FormatCompletion(ProjectFormatSweepSummary run)
    {
        var lines=new List<string> {LocalizationText.Get(run.RunStatus==ProjectFormatSweepRunStatus.Completed ? "Integrity.Success" : RunStatusKey(run.RunStatus)), ""};
        void Count(string key,int count,bool always=false)
        {if(always||count>0)lines.Add("• "+LocalizationText.Format(key,count));}
        Count("Integrity.Count.Checked",run.ProcessedProjects,true);
        Count("Integrity.Count.OK",run.CountOK);
        Count("Integrity.Count.Upgraded",run.CountUpgraded);
        Count("Integrity.Count.Repaired",run.CountRepaired);
        Count("Integrity.Count.Warning",run.CountWarning);
        Count("Integrity.Count.Damaged",run.CountDamaged);
        Count("Integrity.Count.Rebuild",run.CountRebuildRequired);
        return string.Join("\n",lines);
    }

    public static string FormatRebuild(ProjectIntegrityResult result,bool succeeded)
    {
        var lines=new List<string> {LocalizationText.Get(succeeded ? "Rebuild.Success" : result.SafetyBlocked ? "Integrity.SafetyBlocked" : result.Health==ProjectHealthStatus.Damaged ? "Integrity.Damaged" : "Integrity.RebuildRequired")};
        if(succeeded)lines.Add(LocalizationText.Get("Rebuild.SuccessDetails"));
        var actions=new HashSet<string>();
        foreach(var finding in result.Findings)
        {
            var key=finding.IssueCode switch
            {
                IntegrityIssueCode.RebuildTrackStateLost=>"Integrity.Issue.RebuildTrackStateLost",
                IntegrityIssueCode.RebuildMetadataLost=>"Integrity.Issue.RebuildMetadataLost",
                IntegrityIssueCode.CoverMissing=>"Rebuild.CheckCover",
                _=>finding.WasRepaired?finding.RepairAction switch
                {
                    RepairActionCode.RebuildProjectMetadata=>"Integrity.Issue.RebuildMetadataLost",
                    RepairActionCode.RebuildTrackList=>"Integrity.Issue.RebuildTrackStateLost",
                    _=>null
                }:null
            };
            if(key is not null&&actions.Add(key))lines.Add("• "+LocalizationText.Get(key));
        }
        return string.Join("\n",lines);
    }

    public static string Format(ProjectFormatSweepSummary? run) => run is null
        ? LocalizationText.Get("Integrity.NotChecked")
        : LocalizationText.Format("Integrity.SummaryCompact",ProjectFormat.CurrentProjectFormatVersion,
            FormatRunRange(run.RunStartedUtc,run.RunEndedUtc),
            run.RunEndedUtc is { } ended?FormatDuration(ended-run.RunStartedUtc):LocalizationText.Get("Integrity.Run.Running"),
            run.ProcessedProjects,run.TotalProjects,LocalizationText.Get(RunStatusKey(run.RunStatus)),
            run.CountUpgraded,run.CountRepaired,run.CountOK,run.CountWarning,run.CountDamaged,run.CountRebuildRequired);

    public static string FormatDuration(TimeSpan duration)
    {
        var seconds=(long)Math.Round(Math.Max(0,duration.TotalSeconds),MidpointRounding.AwayFromZero);
        return seconds<60?LocalizationText.Format("Integrity.DurationSeconds",seconds):
            LocalizationText.Format("Integrity.DurationMinutes",$"{seconds/60:00}:{seconds%60:00}");
    }
    public static string FormatRunRange(DateTime startedUtc,DateTime? endedUtc)
    {
        var started=startedUtc.ToLocalTime();var culture=System.Globalization.CultureInfo.CurrentCulture;
        if(endedUtc is not { } value)return started.ToString("g",culture);
        var ended=value.ToLocalTime();
        return started.ToString("g",culture)+" – "+ended.ToString(started.Date==ended.Date?"t":"g",culture);
    }
    private static string RunStatusKey(ProjectFormatSweepRunStatus status) => status switch
    {
        ProjectFormatSweepRunStatus.Running => "Integrity.Run.Running",
        ProjectFormatSweepRunStatus.Completed => "Integrity.Run.Completed",
        ProjectFormatSweepRunStatus.Canceled => "Integrity.Run.Canceled",
        _ => "Integrity.Run.Failed"
    };
    public static string ShortProjectPath(string folder, string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return folder;
        try
        {
            var relative = System.IO.Path.GetRelativePath(System.IO.Path.GetFullPath(root), System.IO.Path.GetFullPath(folder));
            if (relative == ".." || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar) || System.IO.Path.IsPathRooted(relative)) return folder;
            return System.IO.Path.Combine(System.IO.Path.GetFileName(root.TrimEnd(System.IO.Path.DirectorySeparatorChar)), relative);
        }
        catch (ArgumentException) { return folder; }
    }
}
