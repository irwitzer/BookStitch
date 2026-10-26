namespace BookStitch.Models;

public enum ProjectFormatSweepRunStatus { Running, Completed, Canceled, Failed }

public sealed class ProjectFormatSweepSummary
{
    public int TargetProjectFormatVersion { get; set; } = ProjectFormat.CurrentProjectFormatVersion;
    public DateTime RunStartedUtc { get; set; }
    public DateTime? RunEndedUtc { get; set; }
    public ProjectFormatSweepRunStatus RunStatus { get; set; }
    public int TotalProjects { get; set; }
    public int ProcessedProjects { get; set; }
    public int CountOK { get; set; }
    public int CountUpgraded { get; set; }
    public int CountRepaired { get; set; }
    public int CountWarning { get; set; }
    public int CountRebuildRequired { get; set; }
    public int CountDamaged { get; set; }
}

/// <summary>One atomic document contains both progress and the successful sweep marker.</summary>
public sealed class ProjectFormatSweepState
{
    public int SchemaVersion { get; set; } = 1;
    public string WorkingRoot { get; set; } = "";
    public int LastProjectFormatSweepVersion { get; set; }
    public int LastIntegritySweepVersion { get; set; }
    public int IntegrityContractVersion { get; set; }
    public bool IsIntegrityRun { get; set; }
    public bool FullContentHashes { get; set; }
    public bool DiscoveryCompleted { get; set; }
    public ProjectFormatSweepSummary LastRun { get; set; } = new();
    public List<string> PendingProjects { get; set; } = [];
    public Dictionary<string, ProjectUpgradeOutcome> FinishedProjects { get; set; } = new();
}
