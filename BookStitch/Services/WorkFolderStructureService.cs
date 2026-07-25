using System.IO;

namespace BookStitch.Services;

public sealed class WorkFolderStructureService
{
    public WorkFolderStructure Ensure(string projectRootFolder)
    {
        if (string.IsNullOrWhiteSpace(projectRootFolder))
            throw new ArgumentException("Es wurde kein Projektordner übergeben.", nameof(projectRootFolder));

        var structure = WorkFolderStructure.FromRoot(projectRootFolder);

        Directory.CreateDirectory(structure.ProjectRootFolder);
        TryHideProjectRoot(structure.ProjectRootFolder);
        Directory.CreateDirectory(structure.ProjectsFolder);
        Directory.CreateDirectory(structure.LocalProjectsFolder);
        Directory.CreateDirectory(structure.Mp3DiscProjectsFolder);
        Directory.CreateDirectory(structure.AudioDiscProjectsFolder);
        Directory.CreateDirectory(structure.CoversFolder);
        Directory.CreateDirectory(structure.BrowserDropsFolder);
        Directory.CreateDirectory(structure.DropImportsFolder);
        Directory.CreateDirectory(structure.SoftwareSettingsFolder);
        Directory.CreateDirectory(structure.LogsFolder);
        MigrateLegacyLogs(structure);

        return structure;
    }

    private static void MigrateLegacyLogs(WorkFolderStructure structure)
    {
        var legacyLogFolder = Path.Combine(structure.ProjectRootFolder, "Logs");
        if (!Directory.Exists(legacyLogFolder))
            return;

        try
        {
            foreach (var sourcePath in Directory.EnumerateFiles(legacyLogFolder, "*", SearchOption.TopDirectoryOnly))
            {
                var targetPath = Path.Combine(structure.LogsFolder, Path.GetFileName(sourcePath));
                if (!File.Exists(targetPath))
                    File.Move(sourcePath, targetPath);
            }

            if (!Directory.EnumerateFileSystemEntries(legacyLogFolder).Any())
                Directory.Delete(legacyLogFolder);
        }
        catch (IOException)
        {
            // Bestehende Logs bleiben erhalten, wenn Windows sie gerade verwendet.
        }
        catch (UnauthorizedAccessException)
        {
            // Die neue Ordnerstruktur bleibt dennoch nutzbar.
        }
    }

    private static void TryHideProjectRoot(string projectRootFolder)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var attributes = File.GetAttributes(projectRootFolder);
            if ((attributes & FileAttributes.Hidden) == 0)
                File.SetAttributes(projectRootFolder, attributes | FileAttributes.Hidden);
        }
        catch (IOException)
        {
            // Die Ordnerstruktur bleibt nutzbar, auch wenn Windows das Hidden-Attribut nicht setzen kann.
        }
        catch (UnauthorizedAccessException)
        {
            // Die Ordnerstruktur bleibt nutzbar, auch wenn Windows das Hidden-Attribut nicht setzen kann.
        }
    }
}

public sealed record WorkFolderStructure(
    string ProjectRootFolder,
    string ProjectsFolder,
    string LocalProjectsFolder,
    string Mp3DiscProjectsFolder,
    string AudioDiscProjectsFolder,
    string CoversFolder,
    string BrowserDropsFolder,
    string DropImportsFolder,
    string SoftwareSettingsFolder,
    string LogsFolder)
{
    public static WorkFolderStructure FromRoot(string projectRootFolder)
    {
        if (string.IsNullOrWhiteSpace(projectRootFolder))
            throw new ArgumentException("Es wurde kein Projektordner übergeben.", nameof(projectRootFolder));

        var root = Path.GetFullPath(projectRootFolder);
        var projects = Path.Combine(root, "Projects");
        var covers = Path.Combine(root, "Covers");

        return new WorkFolderStructure(
            ProjectRootFolder: root,
            ProjectsFolder: projects,
            LocalProjectsFolder: Path.Combine(projects, "LocalProjects"),
            Mp3DiscProjectsFolder: Path.Combine(projects, "MP3DiscProjects"),
            AudioDiscProjectsFolder: Path.Combine(projects, "AudioDiscProjects"),
            CoversFolder: covers,
            BrowserDropsFolder: Path.Combine(covers, "BrowserDrops"),
            DropImportsFolder: Path.Combine(covers, "DropImports"),
            SoftwareSettingsFolder: SettingsService.GetSettingsFolder(root),
            LogsFolder: Path.Combine(SettingsService.GetSettingsFolder(root), "logs"));
    }
}
