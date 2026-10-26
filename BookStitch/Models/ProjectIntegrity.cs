using System.Text.Json.Serialization;
namespace BookStitch.Models;
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProjectHealthStatus { NotChecked, OK, Warning, RebuildRequired, Damaged }
public enum ProjectOperationOutcome { Completed, Canceled, Failed }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IntegrityIssueCode
{
    SourceMissing, SourceZeroLength, SourceSizeMismatch, SourceHashMismatch, SourceUnreadable,
    SourceMetadataMismatch, PreparedMissing, PreparedUnreadable, PreparedMetadataMismatch, PreparedTimingMismatch,
    SourceMetadataMissing, SourceMetadataCorrupt, ProjectMetadataMissing, ProjectMetadataCorrupt,
    TrackListStateCorrupt, UnknownTrackSourceId, ManifestCorrupt, ManifestConflict,
    ProjectIdConflict, ProjectTypeConflict, UnknownProjectFile, UnsafeReparsePoint,
    ManagedTemporaryFileRemoved, PresetUnavailable, StandardDirectoryMissing,
    SourceCopyIdentityMissing, SourceCopyIdentityCorrupt, CoverMissing, UnsupportedSchema, StaleManagedPath, RebuildTrackStateLost, RebuildMetadataLost
}
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RepairActionCode { None, CreateDirectory, RebuildSourceMetadata, RebuildProjectMetadata,
    RebuildTrackList, CorrectSourceReference, RebuildCopyIdentity, UpdateSourceEvidence,
    InvalidatePrepared, ResetTiming, RemoveManagedTemporaryFile, RebaseManagedPath }
public sealed record IntegrityFinding(IntegrityIssueCode IssueCode, ProjectHealthStatus HealthImpact,
    RepairActionCode RepairAction, string TechnicalDetail, string? Path = null, Guid? SourceId = null)
{
    public bool WasRepaired { get; init; }
    public string UserMessageKey => WasRepaired ? "Integrity.Repaired." + RepairAction : "Integrity.Issue." + IssueCode;
    public string UserActionKey => "Integrity.Action." + RepairAction;
}
public static class ProjectIntegrityContract { public const int CurrentVersion = 3; }

public sealed record ProjectIntegrityResult(string ProjectFolder, ProjectHealthStatus Health,
    bool WasUpgraded, bool WasRepaired, IReadOnlyList<IntegrityFinding> Findings)
{ public bool SafetyBlocked => Findings.Any(f=>f.IssueCode==IntegrityIssueCode.UnsafeReparsePoint); }
public sealed record IntegrityProgress(string PhaseKey, string ProjectFolder, int CompletedFiles = 0, int TotalFiles = 0,
    int ProcessedProjects = 0, int TotalProjects = 0);
public sealed record ProjectIntegrityOptions(string FfprobePath, string? ParallelJobs = "Auto", bool FullContentHashes = false)
{ public BookStitch.Services.ProjectIntegrityPerformance? Performance { get; init; } }
