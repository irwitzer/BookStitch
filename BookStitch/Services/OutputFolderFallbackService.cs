using System.IO;

namespace BookStitch.Services;

public static class OutputFolderFallbackService
{
    public static string Resolve(string? selectedFolder) =>
        Resolve(selectedFolder, GetDesktopFallbackFolder());

    internal static string Resolve(string? selectedFolder, string fallbackFolder)
    {
        var selected = selectedFolder?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selected) && Directory.Exists(selected))
            return selected;

        return fallbackFolder?.Trim() ?? string.Empty;
    }

    public static string GetDesktopFallbackFolder()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop))
            return desktop;

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}