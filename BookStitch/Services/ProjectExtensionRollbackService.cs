using System.IO;
using System.Text;

namespace BookStitch.Services;

public sealed record ProjectExtensionRollbackSnapshot(
    string ProjectFolder,
    IReadOnlySet<string> ExistingFiles,
    IReadOnlySet<string> ExistingDirectories,
    IReadOnlyDictionary<string, byte[]> JsonFiles);

public sealed record ProjectExtensionRollbackResult(
    int DeletedFiles,
    int DeletedDirectories,
    int RestoredJsonFiles);

public sealed class ProjectExtensionRollbackService
{
    private const int CleanupRetryCount = 20;
    private static readonly TimeSpan CleanupRetryDelay = TimeSpan.FromMilliseconds(100);

    public ProjectExtensionRollbackSnapshot Capture(string projectFolder)
    {
        var fullProjectFolder = ValidateProjectFolder(projectFolder);
        Directory.CreateDirectory(fullProjectFolder);

        var files = Directory
            .EnumerateFiles(fullProjectFolder, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directories = Directory
            .EnumerateDirectories(fullProjectFolder, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        directories.Add(fullProjectFolder);

        var jsonFiles = files
            .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                path => path,
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);

        return new ProjectExtensionRollbackSnapshot(
            fullProjectFolder,
            files,
            directories,
            jsonFiles);
    }

    public ProjectExtensionRollbackResult Rollback(
        ProjectExtensionRollbackSnapshot snapshot,
        IEnumerable<string>? generatedFiles = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var projectFolder = ValidateProjectFolder(snapshot.ProjectFolder);
        if (!Directory.Exists(projectFolder))
            return new ProjectExtensionRollbackResult(0, 0, 0);

        var explicitGeneratedFiles = generatedFiles?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        var deletedFiles = 0;
        var stablePasses = 0;

        for (var attempt = 0; attempt < CleanupRetryCount && stablePasses < 2; attempt++)
        {
            var deletedThisPass = 0;

            foreach (var generatedFile in explicitGeneratedFiles)
            {
                deletedThisPass += TryDeleteGeneratedFile(projectFolder, generatedFile);
                deletedThisPass += TryDeleteGeneratedFile(projectFolder, generatedFile + ".part");
            }

            foreach (var file in Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories).ToArray())
            {
                var fullPath = Path.GetFullPath(file);
                if (snapshot.ExistingFiles.Contains(fullPath))
                    continue;

                deletedThisPass += TryDeleteGeneratedFile(projectFolder, fullPath);
            }

            deletedFiles += deletedThisPass;
            stablePasses = deletedThisPass == 0 ? stablePasses + 1 : 0;

            if (stablePasses < 2)
                Thread.Sleep(CleanupRetryDelay);
        }

        var restoredJsonFiles = 0;
        foreach (var entry in snapshot.JsonFiles)
        {
            EnsureInsideProject(projectFolder, entry.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(entry.Key)!);
            File.WriteAllBytes(entry.Key, entry.Value);
            restoredJsonFiles++;
        }

        var deletedDirectories = 0;
        foreach (var directory in Directory
                     .EnumerateDirectories(projectFolder, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            var fullPath = Path.GetFullPath(directory);
            if (snapshot.ExistingDirectories.Contains(fullPath) || Directory.EnumerateFileSystemEntries(fullPath).Any())
                continue;

            EnsureInsideProject(projectFolder, fullPath);
            Directory.Delete(fullPath, recursive: false);
            deletedDirectories++;
        }

        return new ProjectExtensionRollbackResult(
            deletedFiles,
            deletedDirectories,
            restoredJsonFiles);
    }

    private static int TryDeleteGeneratedFile(string projectFolder, string path)
    {
        EnsureInsideProject(projectFolder, path);
        if (!File.Exists(path))
            return 0;

        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return 1;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string ValidateProjectFolder(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            throw new ArgumentException("Der Projektordner darf nicht leer sein.", nameof(projectFolder));

        return Path.GetFullPath(projectFolder);
    }

    private static void EnsureInsideProject(string projectFolder, string path)
    {
        var root = projectFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ein Erweiterungs-Rollback darf nur innerhalb des Projektordners arbeiten.");
    }
}
