namespace BookStitch.Models;

public sealed class ExportWorkflowCallbacks
{
    public Action<string>? SetStatusText { get; init; }
    public Action<string>? SetProgressText { get; init; }
    public Action<double>? SetProgressPercent { get; init; }
    public Action<ProjectPipelineState>? SetPipelineState { get; init; }
    public Action<int, int, long, long, IEnumerable<int>>? ReportConversionProgress { get; init; }
    public Action<int, int, double>? ReportMergeProgress { get; init; }
    public Action? NotifyWritingMetadata { get; init; }
    public Action<ConvertedTrackResumeState>? ReportConversionResumeState { get; init; }
    public Func<bool>? ShouldPauseBeforeMerge { get; init; }
    public Func<string, string, FinalOutputConflictAction>? ResolveFinalOutputConflict { get; init; }
    public Func<string, string, Exception, FinalOutputFailureAction>? ResolveFinalOutputFailure { get; init; }
    public Action? NotifyManualReviewStateChanged { get; init; }
}
