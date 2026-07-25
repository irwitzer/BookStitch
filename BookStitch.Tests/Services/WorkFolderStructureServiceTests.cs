using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class WorkFolderStructureServiceTests
{
    [Fact]
    public void FromRoot_ReturnsStableProjectFolderLayout()
    {
        using var folder = new TemporaryFolder();
        var root = Path.Combine(folder.Path, "BookStitchProjects");

        var structure = WorkFolderStructure.FromRoot(root);

        Assert.Equal(Path.GetFullPath(root), structure.ProjectRootFolder);
        Assert.Equal(Path.Combine(root, "Projects"), structure.ProjectsFolder);
        Assert.Equal(Path.Combine(root, "Projects", "LocalProjects"), structure.LocalProjectsFolder);
        Assert.Equal(Path.Combine(root, "Projects", "MP3DiscProjects"), structure.Mp3DiscProjectsFolder);
        Assert.Equal(Path.Combine(root, "Projects", "AudioDiscProjects"), structure.AudioDiscProjectsFolder);
        Assert.Equal(Path.Combine(root, "Covers"), structure.CoversFolder);
        Assert.Equal(Path.Combine(root, "Covers", "BrowserDrops"), structure.BrowserDropsFolder);
        Assert.Equal(Path.Combine(root, "Covers", "DropImports"), structure.DropImportsFolder);
        Assert.Equal(Path.Combine(root, "software-settings"), structure.SoftwareSettingsFolder);
        Assert.Equal(Path.Combine(root, "software-settings", "logs"), structure.LogsFolder);
    }

    [Fact]
    public void Ensure_CreatesCompleteProjectFolderLayout()
    {
        using var folder = new TemporaryFolder();
        var root = Path.Combine(folder.Path, "BookStitchProjects");
        var service = new WorkFolderStructureService();

        var structure = service.Ensure(root);

        Assert.True(Directory.Exists(structure.ProjectRootFolder));
        Assert.True(Directory.Exists(structure.ProjectsFolder));
        Assert.True(Directory.Exists(structure.LocalProjectsFolder));
        Assert.True(Directory.Exists(structure.Mp3DiscProjectsFolder));
        Assert.True(Directory.Exists(structure.AudioDiscProjectsFolder));
        Assert.True(Directory.Exists(structure.CoversFolder));
        Assert.True(Directory.Exists(structure.BrowserDropsFolder));
        Assert.True(Directory.Exists(structure.DropImportsFolder));
        Assert.True(Directory.Exists(structure.SoftwareSettingsFolder));
        Assert.True(Directory.Exists(structure.LogsFolder));
    }

    [Fact]
    public void Ensure_OnWindows_HidesOnlyProjectRoot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var folder = new TemporaryFolder();
        var root = Path.Combine(folder.Path, "BookStitchProjects");
        var service = new WorkFolderStructureService();

        var structure = service.Ensure(root);

        Assert.True(File.GetAttributes(structure.ProjectRootFolder).HasFlag(FileAttributes.Hidden));
        Assert.False(File.GetAttributes(structure.ProjectsFolder).HasFlag(FileAttributes.Hidden));
        Assert.False(File.GetAttributes(structure.LocalProjectsFolder).HasFlag(FileAttributes.Hidden));
        Assert.False(File.GetAttributes(structure.Mp3DiscProjectsFolder).HasFlag(FileAttributes.Hidden));
        Assert.False(File.GetAttributes(structure.AudioDiscProjectsFolder).HasFlag(FileAttributes.Hidden));
        Assert.False(File.GetAttributes(structure.CoversFolder).HasFlag(FileAttributes.Hidden));
        Assert.False(File.GetAttributes(structure.SoftwareSettingsFolder).HasFlag(FileAttributes.Hidden));
        Assert.False(File.GetAttributes(structure.LogsFolder).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void Ensure_MigratesLegacyRootLogsFolder()
    {
        using var folder = new TemporaryFolder();
        var root = Path.Combine(folder.Path, "BookStitchProjects");
        var legacyLogs = Path.Combine(root, "Logs");
        Directory.CreateDirectory(legacyLogs);
        File.WriteAllText(Path.Combine(legacyLogs, "BookStitch.log"), "legacy");

        var service = new WorkFolderStructureService();
        var structure = service.Ensure(root);

        Assert.True(File.Exists(Path.Combine(structure.LogsFolder, "BookStitch.log")));
        Assert.False(Directory.Exists(legacyLogs));
    }
}
