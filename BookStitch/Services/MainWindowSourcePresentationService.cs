using System.IO;

namespace BookStitch.Services;

internal sealed record MainWindowSourcePresentation(
    bool HasProjectTracks,
    string LaunchButtonStyleResourceKey,
    string DisplayText,
    string OpenPath);

internal static class MainWindowSourcePresentationService
{
    public static string GetLaunchButtonStyleResourceKey(bool hasProjectTracks) =>
        hasProjectTracks ? "SecondaryButtonStyle" : "PrimaryButtonStyle";

    public static MainWindowSourcePresentation Create(
        int trackCount,
        string? selectedFolder,
        string? displayOverride,
        string? openPathOverride,
        bool selectedFolderIsCdDrive)
    {
        var hasProjectTracks = trackCount > 0;
        if (!hasProjectTracks)
        {
            return new MainWindowSourcePresentation(
                false,
                "PrimaryButtonStyle",
                string.Empty,
                string.Empty);
        }

        var folder = selectedFolder ?? string.Empty;
        var displayText = !string.IsNullOrWhiteSpace(displayOverride)
            ? displayOverride
            : BuildDisplayText(folder, selectedFolderIsCdDrive);
        var openPath = !string.IsNullOrWhiteSpace(openPathOverride)
            ? openPathOverride
            : folder;

        return new MainWindowSourcePresentation(
            true,
            GetLaunchButtonStyleResourceKey(hasProjectTracks),
            displayText,
            openPath);
    }

    private static string BuildDisplayText(string path, bool isCdDrive)
    {
        if (string.IsNullOrWhiteSpace(path))
            return LocalizationText.Get("MainWindow.Source.NoFolderSelected");

        if (isCdDrive)
        {
            var root = Path.GetPathRoot(path) ?? path;
            var letter = root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':');
            return string.IsNullOrWhiteSpace(letter)
                ? LocalizationText.Get("MainWindow.Source.CdDrive")
                : LocalizationText.Format("MainWindow.Source.CdDriveLetter", letter);
        }

        try
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(folderName) ? path : folderName;
        }
        catch
        {
            return path;
        }
    }
}
