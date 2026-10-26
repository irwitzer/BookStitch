namespace BookStitch.Services;

/// <summary>Aggregates display notices only; never changes media decisions or device actions.</summary>
internal static class DiscDriveRoundWaitPresentation
{
    public static string ActivityProgress(long frame) =>
        LocalizationText.Get("Disc.DriveRound.Activity.Progress") + " " + new string('.', (int)(frame % 5) + 1);

    public static DiscPollingResult? KeepActionable(DiscPollingResult? previous, DiscPollingResult current, bool actionable) =>
        actionable ? current : previous;

    public static string WaitingProgress(string mediaName, int discNumber, int totalDiscs) =>
        LocalizationText.Format("Disc.DriveRound.Waiting.Progress", mediaName, discNumber, totalDiscs);

    public static DiscPollingResult Complete(string mediaName, int discNumber, int totalDiscs, DiscPollingResult? notice) =>
        (notice ?? new DiscPollingResult(false,
            LocalizationText.Format("Disc.DriveRound.Waiting.Dialog", mediaName, discNumber, totalDiscs),
            LocalizationText.Format("Disc.DriveRound.Waiting.Status", mediaName, discNumber), "")) with
        { ProgressText = WaitingProgress(mediaName, discNumber, totalDiscs) };
}
