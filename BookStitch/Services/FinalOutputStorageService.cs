using System.IO;

namespace BookStitch.Services;

public interface IFinalOutputStorageService
{
    void MoveToOutput(string sourcePath, string destinationPath, bool overwrite);

    string CreateDesktopOutputPath(string requestedOutputPath);

    string CreateAvailableOutputPath(string preferredPath);
}

public sealed class FinalOutputStorageService : IFinalOutputStorageService
{
    private readonly Action<string> _deleteCommittedSource;

    public FinalOutputStorageService() : this(File.Delete) { }

    internal FinalOutputStorageService(Action<string> deleteCommittedSource) =>
        _deleteCommittedSource = deleteCommittedSource ?? throw new ArgumentNullException(nameof(deleteCommittedSource));

    public void MoveToOutput(string sourcePath, string destinationPath, bool overwrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!WindowsOutputPathService.IsValid(destinationPath))
            throw new IOException(LocalizationText.Format("Export.InvalidOutputPath", destinationPath));
        if (TrackPathService.PathEquals(sourcePath, destinationPath))
            throw new IOException(LocalizationText.Format("Export.InvalidOutputPath", destinationPath));

        var destinationFolder = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new DirectoryNotFoundException(LocalizationText.Get("Output.TargetFolderMissing"));

        Directory.CreateDirectory(destinationFolder);
        var tempPath = CreateDestinationTempPath(destinationFolder, destinationPath);

        try
        {
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(temp);
                temp.Flush(flushToDisk: true);
            }

            File.Move(tempPath, destinationPath, overwrite);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }

        // The destination rename commits the export. Removing the working copy is cleanup.
        try { _deleteCommittedSource(sourcePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Export committed; working-file cleanup failed: {0}", ex);
        }
    }

    public string CreateDesktopOutputPath(string requestedOutputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedOutputPath);

        var desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopFolder))
            throw new DirectoryNotFoundException(LocalizationText.Get("Output.DesktopFolderMissing"));

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
            var candidate = WindowsOutputPathService.CreateConflictCandidate(folder, "", fileName, extension, $" ({suffix})");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new IOException(LocalizationText.Get("Output.NoAvailableFilename"));
    }

    public static bool IsRecoverableDestinationError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException;
    }

    private static string CreateDestinationTempPath(string destinationFolder, string destinationPath)
    {
        var extension = Path.GetExtension(destinationPath);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = Path.Combine(
                destinationFolder,
                $".{Guid.NewGuid():N}.bookstitch-part{extension}");

            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new IOException(LocalizationText.Get("Output.NoTemporaryFilename"));
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Ein liegengebliebener Temp-Commit darf die ursprüngliche Exportursache nicht verdecken.
        }
    }
}
