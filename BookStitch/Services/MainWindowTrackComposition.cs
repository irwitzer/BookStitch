namespace BookStitch.Services;

public sealed class MainWindowTrackComposition
{
    private MainWindowTrackComposition()
    {
        TrackListActionService = new TrackListActionService();
        TrackListWarningService = new TrackListWarningService();
        TrackWorkspaceFilterService = new TrackWorkspaceFilterService();
        TrackIssueSummaryService = new TrackIssueSummaryService();
    }

    public TrackListActionService TrackListActionService { get; }
    public TrackListWarningService TrackListWarningService { get; }
    public TrackWorkspaceFilterService TrackWorkspaceFilterService { get; }
    public TrackIssueSummaryService TrackIssueSummaryService { get; }

    public static MainWindowTrackComposition Create() => new();
}