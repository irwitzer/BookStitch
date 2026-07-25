namespace BookStitch.Models;

public enum ConvertedTrackPreparationPlanStatus
{
    FullyReusable,
    PartiallyReusable,
    RequiresPreparation
}

public sealed record ConvertedTrackResumeState(
    ConvertedTrackPreparationPlanStatus Status,
    int TotalCount,
    int ReusableCount,
    int PendingCount,
    long ReusableDurationTicks)
{
    public bool IsPartialResume => Status == ConvertedTrackPreparationPlanStatus.PartiallyReusable;
    public bool IsFullyReusable => Status == ConvertedTrackPreparationPlanStatus.FullyReusable;
    public bool RequiresPreparation => PendingCount > 0;
}
