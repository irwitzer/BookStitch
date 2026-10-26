namespace BookStitch.Models;

/// <summary>
/// Stable append-only raw event persisted as one JSON object per line.
/// Additional optional fields can be added without changing existing readers.
/// </summary>
public sealed record PerformanceJournalEvent
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Event { get; init; } = string.Empty;

    public Guid SessionId { get; init; }

    public Guid? OperationId { get; init; }

    public Guid? PhaseId { get; init; }

    public DateTimeOffset Utc { get; init; }

    public string? Name { get; init; }

    public PerformanceOutcome? Outcome { get; init; }

    public double? ElapsedMilliseconds { get; init; }

    public string? Tool { get; init; }

    public string? Purpose { get; init; }

    public int? ExitCode { get; init; }

    public double? Value { get; init; }

    public string? Unit { get; init; }

    public Guid? DriveId { get; init; }

    public string? DriveReference { get; init; }

    public string? ProbeType { get; init; }

    public DateTimeOffset? StartedUtc { get; init; }

    public bool? Success { get; init; }

    public bool? TimedOut { get; init; }

    public bool? Cancelled { get; init; }

    public bool? IsReady { get; init; }

    public bool? MediumPresent { get; init; }

    public bool? FirstTouchInSession { get; init; }

    public string? Result { get; init; }
}