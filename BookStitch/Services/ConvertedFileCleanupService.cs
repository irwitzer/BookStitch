using System.IO;

namespace BookStitch.Services;

public sealed class ConvertedFileCleanupService
{
    public void DeleteUnusedConvertedFiles(string convertedFolder, IEnumerable<string> expectedConvertedPaths)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(convertedFolder) || !Directory.Exists(convertedFolder))
                return;

            var expected = expectedConvertedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(GetComparableFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(convertedFolder, "*.m4a", SearchOption.TopDirectoryOnly))
            {
                if (expected.Contains(GetComparableFullPath(file)))
                    continue;

                TryDeleteFile(file);
            }
        }
        catch
        {
            // Alte Cache-Dateien dürfen den erfolgreichen Export nicht verhindern.
        }
    }

    public void DeletePartFiles(string folderPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            var incompleteFiles = Directory
                .EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                .Where(IsIncompleteFilePath)
                .ToList();

            foreach (var file in incompleteFiles)
                TryDeleteFile(file);
        }
        catch
        {
            // Aufräumen darf den eigentlichen Fehler nicht überdecken.
        }
    }

    public void TryDeleteFile(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            var attributes = File.GetAttributes(filePath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);

            File.Delete(filePath);
        }
        catch
        {
            // Aufräumen darf den eigentlichen Fehler nicht überdecken.
        }
    }

    public static bool IsPartFilePath(string filePath)
    {
        return filePath.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
               filePath.Contains(".part.", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIncompleteFilePath(string filePath)
    {
        return IsPartFilePath(filePath) ||
               filePath.EndsWith(".copying", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetComparableFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
