using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectFormatWiringTests
{
    [Fact]
    public void SweepDiscovery_MissingRootFailsInsteadOfPublishingAnEmptySuccess()
    {
        using var folder = new TemporaryFolder();
        Assert.Throws<DirectoryNotFoundException>(() => ProjectIndexService.DiscoverProjectFolders(Path.Combine(folder.Path, "missing")));
    }

    [Fact]
    public void IndexOfLegacyFolder_DoesNotCreateMetadataOrMoveManifest()
    {
        using var folder = new TemporaryFolder();
        var project = Directory.CreateDirectory(Path.Combine(folder.Path, "Book")).FullName;
        var path = Path.Combine(project, "project.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new ExportWorkManifest { ProjectWorkFolder = project,
            Metadata = new() { Title = "Legacy book", Author = "Author" } }));
        var bytes = File.ReadAllBytes(path);

        var item = Assert.Single(new ProjectIndexService().ScanProjects(folder.Path));
        Assert.True(item.NeedsUpgrade);
        Assert.Equal(0, item.ProjectFormatVersion);
        Assert.Equal("Legacy book", item.Title);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetSettingsFolder(project)));
    }

}
