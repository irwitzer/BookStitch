using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectFolderLayoutTests
{
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
}
