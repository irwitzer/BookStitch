namespace BookStitch.Services;

public static class SettingsDriveTextService
{
    public static string BuildCircleHint(int activeConnected, int inactiveConnected, int disconnected, int maximumActiveDrives)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeConnected);
        ArgumentOutOfRangeException.ThrowIfNegative(inactiveConnected);
        ArgumentOutOfRangeException.ThrowIfNegative(disconnected);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumActiveDrives);

        var parts = new List<string>
        {
            LocalizationText.Format(activeConnected == 1 ? "Settings.Drive.ActiveCount.One" : "Settings.Drive.ActiveCount.Many", activeConnected),
            LocalizationText.Format(inactiveConnected == 1 ? "Settings.Drive.InactiveConnectedCount.One" : "Settings.Drive.InactiveConnectedCount.Many", inactiveConnected),
            LocalizationText.Format(disconnected == 1 ? "Settings.Drive.DisconnectedCount.One" : "Settings.Drive.DisconnectedCount.Many", disconnected)
        };
        if (activeConnected > maximumActiveDrives)
            parts[0] += " " + LocalizationText.Format("Settings.Drive.ActiveLimit", maximumActiveDrives);
        return string.Join(Environment.NewLine, parts);
    }
}
