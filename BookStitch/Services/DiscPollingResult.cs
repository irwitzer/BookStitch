namespace BookStitch.Services;

public enum DiscPollingDisplayState
{
    Waiting,
    Unsupported,
    Duplicate,
    Ready
}

public static class DiscPollingDisplayStateRules
{
    public static bool ShouldKeepNoticeVisible(
        DiscPollingDisplayState displayedState,
        DiscPollingDisplayState nextState) =>
        (displayedState is DiscPollingDisplayState.Unsupported or DiscPollingDisplayState.Duplicate) &&
        nextState == DiscPollingDisplayState.Waiting;
}

public sealed record DiscPollingResult(
    bool CanImport,
    string DialogText,
    string StatusText,
    string ProgressText,
    DiscPollingDisplayState DisplayState = DiscPollingDisplayState.Waiting);
