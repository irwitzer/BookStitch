namespace BookStitch.Models;

public sealed record AudioDiscPipelineTimingSnapshot(
    DateTimeOffset? StartedUtc,
    TimeSpan? RipDuration,
    TimeSpan? TotalDuration,
    bool IsRunning);
