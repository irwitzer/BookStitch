using System.IO;

namespace CoverImageDrop.Pipeline;

public sealed class CoverImageFilePickerDirectoryState
{
    private string? _preferredDirectory;

    public CoverImageFilePickerDirectoryState(
        string? restoredLastDirectory = null)
    {
        LastUsedDirectory = NormalizeOptionalDirectory(
            restoredLastDirectory);
    }

    public string? PreferredDirectory => _preferredDirectory;

    public string? LastUsedDirectory { get; private set; }

    public void SetPreferredFilePickerDirectory(string? directory)
    {
        _preferredDirectory = NormalizeOptionalDirectory(directory);
    }

    public string? GetFilePickerDirectoryForNextOpen()
    {
        if (IsExistingDirectory(_preferredDirectory))
        {
            return _preferredDirectory;
        }

        if (IsExistingDirectory(LastUsedDirectory))
        {
            return LastUsedDirectory;
        }

        var desktop = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory);

        return string.IsNullOrWhiteSpace(desktop)
            ? null
            : desktop;
    }

    public void RecordFilePickerSelection(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "The selected file path does not contain a directory.",
                nameof(filePath));
        }

        LastUsedDirectory = directory;
        _preferredDirectory = null;
    }

    private static string? NormalizeOptionalDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        return Path.GetFullPath(directory);
    }

    private static bool IsExistingDirectory(string? directory)
    {
        return !string.IsNullOrWhiteSpace(directory) &&
               Directory.Exists(directory);
    }
}
