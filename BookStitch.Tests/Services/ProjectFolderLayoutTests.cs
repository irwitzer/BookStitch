using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectFolderLayoutTests
{
    [Fact]
    public void Export_manifest_probe_can_read_legacy_state_while_a_shared_writer_is_open()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var path = ProjectFolderLayout.GetWorkManifestPath(folder.Path);
        const string json = "{\"Tracks\":[],\"State\":{},\"Export\":{}}";
        File.WriteAllText(path, json);
        using (var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete))
            Assert.Equal(path, ProjectFolderLayout.ResolveExportManifestPath(folder.Path));
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void EnsureProjectFolders_CreatesUnifiedTopLevelStructure()
    {
        using var folder = new TemporaryFolder();

        ProjectFolderLayout.EnsureProjectFolders(folder.Path);

        Assert.True(Directory.Exists(ProjectFolderLayout.GetOriginalsFolder(folder.Path)));
        Assert.True(Directory.Exists(ProjectFolderLayout.GetConvertedFolder(folder.Path)));
        Assert.True(Directory.Exists(ProjectFolderLayout.GetMergeFolder(folder.Path)));
        Assert.True(Directory.Exists(ProjectFolderLayout.GetSettingsFolder(folder.Path)));
    }

    [Fact]
    public void GetDiscOriginalsFolder_UsesOriginalsAndDiscSubfolder()
    {
        using var folder = new TemporaryFolder();

        var result = ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 2);

        Assert.Equal(Path.Combine(folder.Path, "originals", "CD 02"), result);
    }

    [Fact]
    public void GetProjectFolderFromManifestPath_ReturnsParentOfSettingsFolder()
    {
        using var folder = new TemporaryFolder();
        var manifestPath = ProjectFolderLayout.GetWorkManifestPath(folder.Path);

        var result = ProjectFolderLayout.GetProjectFolderFromManifestPath(manifestPath);

        Assert.Equal(folder.Path, result);
    }

    [Fact]
    public void EnsureProjectFolders_WhenInsideProjectRoot_DoesNotWriteDiagnosticEvent()
    {
        using var folder = new TemporaryFolder();
        var projectRoot = Directory.CreateDirectory(Path.Combine(folder.Path, "BookStitchProjects")).FullName;
        var projectFolder = Path.Combine(projectRoot, "Book A");
        var events = new List<(string EventName, string Message)>();

        using (ProjectFolderLayoutDiagnostics.BeginScope(
                   (eventName, message) => events.Add((eventName, message)),
                   new ProjectFolderLayoutDiagnosticContext("Development", projectRoot)))
        {
            ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        }

        Assert.Empty(events);
    }

    [Fact]
    public void EnsureProjectFolders_WithoutProjectRoot_DoesNotWriteDiagnosticEvent()
    {
        using var folder = new TemporaryFolder();
        var events = new List<(string EventName, string Message)>();

        using (ProjectFolderLayoutDiagnostics.BeginScope(
                   (eventName, message) => events.Add((eventName, message)),
                   new ProjectFolderLayoutDiagnosticContext("Development", "")))
        {
            ProjectFolderLayout.EnsureProjectFolders(Path.Combine(folder.Path, "Book A"));
        }

        Assert.Empty(events);
    }

    [Fact]
    public void EnsureProjectFolders_WhenDiagnosticsConfigured_RecordsOutsideProjectRoot()
    {
        using var folder = new TemporaryFolder();
        var projectRoot = Directory.CreateDirectory(Path.Combine(folder.Path, "BookStitchProjects")).FullName;
        var outsideFolder = Path.Combine(folder.Path, "Desktop", "Unexpected");
        var events = new List<(string EventName, string Message)>();

        using (ProjectFolderLayoutDiagnostics.BeginScope(
                   (eventName, message) => events.Add((eventName, message)),
                   new ProjectFolderLayoutDiagnosticContext("Development", projectRoot)))
        {
            ProjectFolderLayout.EnsureProjectFolders(outsideFolder);
        }

        var entry = Assert.Single(
            events,
            entry => entry.Message.Contains(Path.GetFullPath(outsideFolder), StringComparison.Ordinal));
        Assert.Equal("PROJECT LAYOUT OUTSIDE ROOT", entry.EventName);
        Assert.Contains(Path.GetFullPath(outsideFolder), entry.Message, StringComparison.Ordinal);
        Assert.Contains("Environment=Development", entry.Message, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(projectRoot), entry.Message, StringComparison.Ordinal);
        Assert.Contains("InsideProjectRoot=False", entry.Message, StringComparison.Ordinal);
    }
}
