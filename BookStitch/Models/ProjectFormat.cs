namespace BookStitch.Models;

public static class ProjectFormat
{
    public const int CurrentProjectFormatVersion = 1;
}

public sealed class ProjectHealth
{
    public DateTime? LastIntegrityCheckUtc { get; set; }
    public int? LastIntegrityCheckProjectFormatVersion { get; set; }
    public int LastIntegrityContractVersion { get; set; }
    public string LastIntegrityStatus { get; set; } = "NotChecked";
    public List<IntegrityFinding> Findings { get; set; } = [];
}

public sealed class FolderProjectManifest
{
    public int ProjectFormatVersion { get; set; } = ProjectFormat.CurrentProjectFormatVersion;
    [System.Text.Json.Serialization.JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;
    public string FormatVersion { get; set; } = "1";
    public string ProjectId { get; set; } = "";
    public string ProjectType { get; set; } = ProjectManifestTypes.FolderProject;
    public string ProjectFolder { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public string PipelineState { get; set; } = ProjectPipelineStateNames.Preparing;
    // Missing in legacy projects: retain the historical 44.1 kHz contract.
    public int TargetSampleRateHz { get; set; } = 44100;
    public bool AllowAacReuse { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public ProjectHealth Health { get; set; } = new();
}

public enum ProjectUpgradeOutcome { OK, Upgraded, Repaired, Warning, RebuildRequired, Damaged }

public sealed record ProjectUpgradeResult(string ProjectFolder, ProjectUpgradeOutcome Outcome,
    int ProjectFormatVersion, string? Detail = null, bool SafetyBlocked = false)
{
    public bool IsCurrent => Outcome is ProjectUpgradeOutcome.OK or ProjectUpgradeOutcome.Upgraded;
    public IReadOnlyList<IntegrityFinding> Repairs { get; init; } = [];
}
