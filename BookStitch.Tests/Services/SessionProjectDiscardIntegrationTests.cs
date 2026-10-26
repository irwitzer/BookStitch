using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

// ProjectIndexService's current composition also requires settings/environment services.
// Keep the real deletion integration in Windows; validation uses the shared neutral suite.
public sealed partial class SessionProjectLifecycleTests
{
    [Fact]
    public void Silent_draft_cleanup_preserves_source_and_other_projects()
    {
        using var fixture = new Draft();
        var original = fixture.Root.CreateFile("source/original.wav");
        var older = fixture.Root.CreateFile("audio/older/project-settings/keep.json");
        fixture.Validate();
        var result = new ProjectIndexService().DeleteProject(fixture.Root.Path, fixture.Folder);
        Assert.True(result.Deleted, result.ErrorMessage);
        Assert.False(Directory.Exists(fixture.Folder));
        Assert.True(File.Exists(original));
        Assert.True(File.Exists(older));
    }

    [Fact]
    public void Deletion_failure_preserves_draft_and_authority_for_retry()
    {
        using var fixture = new Draft();
        fixture.Validate();
        var index = new ProjectIndexService { BeforeManualDeletion = () => throw new IOException("injected") };
        Assert.False(index.DeleteProject(fixture.Root.Path, fixture.Folder).Deleted);
        fixture.Validate();
        Assert.True(Directory.Exists(fixture.Folder));
    }
}
