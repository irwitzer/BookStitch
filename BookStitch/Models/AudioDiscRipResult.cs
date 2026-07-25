namespace BookStitch.Models;

public sealed record AudioDiscRipProgress(
    int CompletedTracks,
    int TotalTracks,
    int CurrentTrackNumber,
    double CurrentTrackFraction,
    TimeSpan Elapsed);

public sealed record AudioDiscRipResult(
    bool Succeeded,
    bool WasCanceled,
    string ErrorMessage,
    int CompletedTracks)
{
    public static AudioDiscRipResult Success(int completedTracks) =>
        new(true, false, string.Empty, completedTracks);

    public static AudioDiscRipResult Canceled(int completedTracks) =>
        new(false, true, string.Empty, completedTracks);

    public static AudioDiscRipResult Failed(string message, int completedTracks) =>
        new(false, false, message, completedTracks);
}
