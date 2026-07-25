using System.IO;

namespace BookStitch.Services;

public sealed class FinalOutputStorageService
{
    public void MoveToOutput(string sourcePath, string destinationPath, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var destinationFolder = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new DirectoryNotFoundException("Der Zielordner konnte nicht bestimmt werden.");

        Directory.CreateDirectory(destinationFolder);
        File.Move(sourcePath, destinationPath, overwrite);
    }

    public string CreateDesktopOutputPath(string requestedOutputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedOutputPath);

        var desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopFolder))
            throw new DirectoryNotFoundException("Der Desktop-Ordner konnte nicht bestimmt werden.");

        return CreateAvailableOutputPath(Path.Combine(desktopFolder, Path.GetFileName(requestedOutputPath)));
    }

    public string CreateAvailableOutputPath(string preferredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferredPath);

        if (!File.Exists(preferredPath) && !Directory.Exists(preferredPath))
            return preferredPath;

        var folder = Path.GetDirectoryName(preferredPath) ?? string.Empty;
        var extension = Path.GetExtension(preferredPath);
        var fileName = Path.GetFileNameWithoutExtension(preferredPath);

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = Path.Combine(folder, $"{fileName} ({suffix}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new IOException("Es konnte kein freier Dateiname erzeugt werden.");
    }

    public static bool IsRecoverableDestinationError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException;
    }
}
