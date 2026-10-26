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

            if (HasReparseAncestor(convertedFolder) || HasOriginalsAncestor(convertedFolder)) return;
            var expected = expectedConvertedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(GetComparableFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in SafeFileSystemEnumeration.FilesInDirectory(convertedFolder, searchPattern: "*.m4a"))
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

            // User source filenames are never evidence of temporary output.
            if (HasReparseAncestor(folderPath) || HasOriginalsAncestor(folderPath)) return;
            DeleteManagedPartFiles(ProjectFolderLayout.GetConvertedFolder(folderPath));
            DeleteManagedPartFiles(ProjectFolderLayout.GetMergeFolder(folderPath));
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Project temporary-file cleanup deferred: {0}", error);
        }
    }

    public void DeleteConvertedPartFiles(string convertedFolder)
    {
        var directory = new DirectoryInfo(convertedFolder);
        if (!directory.Name.Equals(ProjectFolderLayout.ConvertedFolderName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(directory.Parent?.Name, ProjectFolderLayout.ConvertedFolderName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A managed converted folder is required.", nameof(convertedFolder));
        DeleteManagedPartFiles(convertedFolder);
    }

    private void DeleteManagedPartFiles(string folder)
    {
        try
        {
            if (!Directory.Exists(folder) || HasReparseAncestor(folder) || HasOriginalsAncestor(folder)) return;
            foreach (var file in SafeFileSystemEnumeration.EnumerateFiles(folder).Where(IsIncompleteFilePath))
                TryDeleteFile(file);
        }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Cache cleanup failed: {0}", ex); }
    }

    private static bool HasOriginalsAncestor(string folder)
    {
        for (var directory = new DirectoryInfo(folder); directory is not null; directory = directory.Parent)
            if (directory.Name.Equals(ProjectFolderLayout.OriginalsFolderName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public void TryDeleteFile(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            if (HasReparseAncestor(Path.GetDirectoryName(Path.GetFullPath(filePath))!)) return;
            var attributes = File.GetAttributes(filePath);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return;
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);

            File.Delete(filePath);
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Project temporary-file cleanup deferred for {0}: {1}", filePath, error);
        }
    }

    private static bool HasReparseAncestor(string folder)
    {
        try { SafeFileSystemEnumeration.EnsureNoReparseAncestors(folder); return false; }
        catch (Exception ex) when (SafeFileSystemEnumeration.IsFileSystemError(ex))
        {
            System.Diagnostics.Trace.TraceWarning("Cache cleanup rejected an unsafe path: {0}", ex.Message);
            return true;
        }
    }

    // These names are temporary output only when the caller has already established
    // a managed cache/merge path. Never use this predicate to filter imported sources.
    public static bool IsPartFilePath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".part.m4a", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".part.m4b", StringComparison.OrdinalIgnoreCase);
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
