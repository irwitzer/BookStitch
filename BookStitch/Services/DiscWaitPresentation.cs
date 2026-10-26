namespace BookStitch.Services;

internal static class DiscWaitPresentation
{
    public static string MediaName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? LocalizationText.Get("Disc.Wait.GenericMedia") : name.Trim();

    public static string InsertTitle(string mediaName, int discNumber, int totalDiscs) =>
        LocalizationText.Format("Disc.Wait.InsertTitle", mediaName, discNumber, totalDiscs);

    public static string Checking(string mediaName, int discNumber, int totalDiscs) =>
        LocalizationText.Format("Disc.Wait.Checking", mediaName, discNumber, totalDiscs);

    public static string CheckingProgress(string mediaName, int discNumber) =>
        LocalizationText.Format("Disc.Wait.CheckingProgress", mediaName, discNumber);

    public static string Waiting(string mediaName, int discNumber, int totalDiscs) =>
        LocalizationText.Format("Disc.Wait.Waiting", mediaName, discNumber, totalDiscs);

    public static string DriveNotReady(string mediaName, int discNumber) =>
        LocalizationText.Format("Disc.Wait.DriveNotReady", mediaName, discNumber);

    public static string ReadFailure(string mediaName, int discNumber, int totalDiscs, string buttonText, string error) =>
        LocalizationText.Format("Disc.Wait.ReadFailure", mediaName, discNumber, totalDiscs, buttonText, error);
}
