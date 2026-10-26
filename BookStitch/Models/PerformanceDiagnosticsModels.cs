namespace BookStitch.Models;

public enum PerformanceOutcome
{
    Success,
    Failed,
    Cancelled,
    Incomplete,
    Paused
}

public enum PerformanceSessionStartReason
{
    ApplicationStartup,
    UserEnabled
}

public enum PerformanceProjectType
{
    Unknown,
    LocalFolder,
    Mp3Disc,
    AudioCd,
    Resume
}

[Flags]
public enum PerformanceDriveRole
{
    None = 0,
    Source = 1,
    ProjectWorking = 2,
    Output = 4,
    OpticalSource = 8
}

public sealed record PerformanceMetric
{
    public string Name { get; init; } = string.Empty;

    public double Value { get; init; }

    public string? Unit { get; init; }
}

public sealed record PerformanceWorkloadSnapshot
{
    public PerformanceProjectType ProjectType { get; init; } = PerformanceProjectType.Unknown;

    public int? TrackCount { get; init; }

    public int? FileCount { get; init; }

    public int? DiscCount { get; init; }

    public int? SelectedTrackCount { get; init; }

    public long? InputBytes { get; init; }

    public long? OutputBytes { get; init; }

    public double? InputAudioDurationMilliseconds { get; init; }

    public double? OutputAudioDurationMilliseconds { get; init; }

    public int? PreparedReuseCount { get; init; }

    public int? ConvertedTrackCount { get; init; }

    public int? SkippedTrackCount { get; init; }

    public int? WarningCount { get; init; }

    public int? RetryCount { get; init; }
}

public sealed record PerformanceEnvironmentSnapshot
{
    public string? OperatingSystem { get; init; }

    public string? OperatingSystemVersion { get; init; }

    public string? OperatingSystemArchitecture { get; init; }

    public string? ProcessArchitecture { get; init; }

    public string? DotNetRuntime { get; init; }

    public int? LogicalProcessorCount { get; init; }

    public int? PhysicalCoreCount { get; init; }

    public string? CpuModel { get; init; }

    public long? TotalPhysicalMemoryBytes { get; init; }

    public long? AvailablePhysicalMemoryBytes { get; init; }

    public long? ProcessWorkingSetBytes { get; init; }
}

public sealed record PerformanceToolSnapshot
{
    public bool Available { get; init; }

    public string? Version { get; init; }

    public string? FileVersion { get; init; }

    public string? Architecture { get; init; }

    public string? PathHint { get; init; }
}

public sealed record PerformanceDriveSnapshot
{
    public Guid DriveId { get; init; }

    public string DriveRoot { get; init; } = string.Empty;

    public PerformanceDriveRole Roles { get; init; }

    public string? DriveType { get; init; }

    public string? VolumeLabel { get; init; }

    public string? FileSystem { get; init; }

    public long? TotalSizeBytes { get; init; }

    public long? AvailableFreeSpaceBytes { get; init; }

    public string? DeviceModel { get; init; }

    public string? BusType { get; init; }

    public string? MediaType { get; init; }

    public string? OpticalDriveDisplayName { get; init; }

    public string? UserNickname { get; init; }
}

public sealed record PerformanceDriveRoleReference
{
    public Guid DriveId { get; init; }

    public PerformanceDriveRole Roles { get; init; }
}

public sealed record PerformanceExternalProcessAggregate
{
    public string Tool { get; init; } = string.Empty;

    public string? Purpose { get; init; }

    public int CallCount { get; init; }

    public int SuccessCount { get; init; }

    public int FailureCount { get; init; }

    public int CancelledCount { get; init; }

    public double SumElapsedMilliseconds { get; init; }

    public double? MinElapsedMilliseconds { get; init; }

    public double? MaxElapsedMilliseconds { get; init; }

    public double? MeanElapsedMilliseconds { get; init; }

    public int MaxConcurrency { get; init; }
}

public sealed record PerformanceOperationStatistics
{
    public double? WallClockMilliseconds { get; init; }
    public double SumChildOperationElapsedMilliseconds { get; init; }
    public double SumPhaseElapsedMilliseconds { get; init; }
    public int MaxExternalProcessConcurrency { get; init; }
    public double? InputMegabytesPerSecond { get; init; }
    public double? OutputMegabytesPerSecond { get; init; }
    public double? FilesPerSecond { get; init; }
    public double? TracksPerSecond { get; init; }
    public double? RealTimeFactor { get; init; }
}

public sealed record PerformancePhaseReport
{
    public Guid PhaseId { get; init; }

    public Guid OperationId { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset? CompletedUtc { get; init; }

    public double? ElapsedMilliseconds { get; init; }

    public PerformanceOutcome Outcome { get; init; } = PerformanceOutcome.Incomplete;

    public List<PerformanceMetric> Metrics { get; init; } = [];

    public List<PerformanceExternalProcessAggregate> ExternalProcessAggregates { get; init; } = [];

    public string? FailureType { get; init; }

    public string? FailureMessage { get; init; }
}

public sealed record PerformanceOperationReport
{
    public Guid OperationId { get; init; }

    public Guid SessionId { get; init; }

    public Guid? ParentOperationId { get; init; }

    public string Name { get; init; } = string.Empty;

    public PerformanceProjectType ProjectType { get; init; } = PerformanceProjectType.Unknown;

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset? CompletedUtc { get; init; }

    public double? ElapsedMilliseconds { get; init; }

    public PerformanceOutcome Outcome { get; init; } = PerformanceOutcome.Incomplete;

    public PerformanceWorkloadSnapshot Workload { get; init; } = new();

    public List<PerformanceDriveRoleReference> DriveRoleReferences { get; init; } = [];

    public PerformanceOperationStatistics? Statistics { get; init; }

    public List<PerformanceMetric> Metrics { get; init; } = [];

    public List<PerformanceExternalProcessAggregate> ExternalProcessAggregates { get; init; } = [];

    public string? FailureType { get; init; }

    public string? FailureMessage { get; init; }

    public List<PerformancePhaseReport> Phases { get; init; } = [];

    public List<Guid> ChildOperationIds { get; init; } = [];
}

public sealed record PerformanceSessionReport
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public Guid SessionId { get; init; }

    public PerformanceSessionStartReason StartReason { get; init; } = PerformanceSessionStartReason.ApplicationStartup;

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset? CompletedUtc { get; init; }

    public double? ElapsedMilliseconds { get; init; }

    public PerformanceOutcome Outcome { get; init; } = PerformanceOutcome.Incomplete;

    public string ApplicationVersion { get; init; } = string.Empty;

    public string? BuildIdentifier { get; init; }

    public string? SourceRevision { get; init; }

    public string? BuildConfiguration { get; init; }

    public string? SelectedParallelJobs { get; init; }

    public PerformanceEnvironmentSnapshot? Environment { get; init; }

    public PerformanceToolSnapshot? FfmpegContext { get; init; }

    public PerformanceToolSnapshot? FfprobeContext { get; init; }

    public List<PerformanceDriveSnapshot> DriveSnapshots { get; init; } = [];

    public List<PerformanceOperationReport> Operations { get; init; } = [];
}
