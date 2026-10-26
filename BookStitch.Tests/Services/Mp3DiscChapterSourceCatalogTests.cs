using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscChapterSourceCatalogTests
{
    [Fact]
    public void Planner_UsesDiscSignatureAndRelativePath_NotDriveRoot()
    {
        using var first = new TemporaryFolder();
        using var second = new TemporaryFolder();
        var firstFile = CreateFile(first.Path, Path.Combine("Audio", "Track 01.mp3"), "audio");
        var secondFile = CreateFile(second.Path, Path.Combine("Audio", "Track 01.mp3"), "audio");

        var firstPlan = Mp3DiscSourceIdentityPlanner.Create(first.Path, "SIGNATURE", [firstFile]);
        var secondPlan = Mp3DiscSourceIdentityPlanner.Create(second.Path, "SIGNATURE", [secondFile]);

        Assert.Equal(Assert.Single(firstPlan).SourceId, Assert.Single(secondPlan).SourceId);
        Assert.Equal("Audio" + Path.DirectorySeparatorChar + "Track 01.mp3", firstPlan[0].RelativePath);
    }

    [Fact]
    public void Catalog_StagesRuntimeOnlyAcceptedDiscsAndCanRemoveThem()
    {
        var source = new Mp3DiscPlannedSourceIdentity(
            Guid.NewGuid(),
            "Track 01.mp3",
            "Track 01.mp3",
            123,
            456);
        var catalog = new Mp3DiscChapterSourceCatalog();

        catalog.Stage(2, "disc-2", [source]);

        var staged = Assert.Single(catalog.Snapshot());
        Assert.Equal(2, staged.DiscNumber);
        Assert.Equal("disc-2", staged.Signature);
        Assert.Equal(source.SourceId, Assert.Single(staged.Sources).SourceId);

        catalog.Remove(2);
        Assert.Empty(catalog.Snapshot());
    }

    private static string CreateFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
