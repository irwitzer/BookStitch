using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectCatalogIndexIntegrationTests
{
    [Fact]
    public async Task Missing_manifest_damage_uses_the_index_discovery_and_deletion_invalidates_catalog()
    {
        using var root = new TemporaryFolder();
        var project = Path.Combine(WorkFolderStructure.FromRoot(root.Path).LocalProjectsFolder, "Broken");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "source.mp3"), "fixture");
        var index = new ProjectIndexService();
        var snapshot = await index.Catalog.RefreshAsync(root.Path, 45);
        var complete = await index.Catalog.WaitForDamageAsync(snapshot.Generation, CancellationToken.None);
        Assert.Equal(project, Assert.Single(complete!.DamageInformation).ProjectFolder);
        Assert.True(index.DeleteProject(root.Path, project).Deleted);
        Assert.True(index.Catalog.Snapshot!.IsDirty);
        var refreshed = await index.Catalog.RefreshAsync(root.Path, 45);
        Assert.Empty((await index.Catalog.WaitForDamageAsync(refreshed.Generation, CancellationToken.None))!.DamageInformation);
    }
}
