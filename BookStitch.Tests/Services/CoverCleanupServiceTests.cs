using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class CoverCleanupServiceTests
{
    [Fact]
    public void Cancellation_before_cover_reference_scan_preserves_all_files()
    {
        using var root = new TemporaryFolder();
        var covers = Directory.CreateDirectory(Path.Combine(root.Path, "covers")).FullName;
        var path = Path.Combine(covers, "old.png");
        File.WriteAllText(path, "old cover");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-100));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new CoverCleanupService().DeleteOldUnreferencedCoverFiles(
            root.Path, covers, DateTime.UtcNow, 1, cancellation.Token));
        Assert.Equal("old cover", File.ReadAllText(path));
    }

    [Fact]
    public void Cover_folder_outside_managed_root_is_never_cleaned()
    {
        using var root = new TemporaryFolder();
        using var outside = new TemporaryFolder();
        var cover = outside.CreateFile("old.jpg");
        File.WriteAllText(cover, "external cover");
        Assert.NotEmpty(new CoverCleanupService().DeleteOldUnreferencedCoverFiles(
            root.Path, outside.Path, DateTime.UtcNow.AddDays(1), 0));
        Assert.Equal("external cover", File.ReadAllText(cover));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Corrupt_or_unreadable_manifest_aborts_cleanup_before_any_deletion(bool locked)
    {
        using var folder = new TemporaryFolder();
        var covers = Directory.CreateDirectory(Path.Combine(folder.Path, "covers")).FullName;
        var cover = Path.Combine(covers, "old.jpg");
        File.WriteAllText(cover, "cover");
        File.SetLastWriteTimeUtc(cover, DateTime.UtcNow.AddDays(-10));
        var manifest = folder.CreateFile("project.json");
        File.WriteAllText(manifest, locked ? "{}" : "{broken");
        using var handle = locked ? new FileStream(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;

        var result = new CoverCleanupService().DeleteOldUnreferencedCoverFiles(folder.Path, covers, DateTime.UtcNow, 1);

        Assert.NotEmpty(result);
        Assert.Equal("cover", File.ReadAllText(cover));
    }

    [Fact]
    public void Complete_reference_scan_preserves_referenced_and_recent_covers_only()
    {
        using var folder = new TemporaryFolder();
        var covers = Directory.CreateDirectory(Path.Combine(folder.Path, "covers")).FullName;
        var referenced = Path.Combine(covers, "used.jpg");
        var unused = Path.Combine(covers, "unused.jpg");
        var recent = Path.Combine(covers, "recent.jpg");
        foreach (var path in new[] { referenced, unused, recent }) File.WriteAllText(path, "cover");
        File.SetLastWriteTimeUtc(referenced, DateTime.UtcNow.AddDays(-10));
        File.SetLastWriteTimeUtc(unused, DateTime.UtcNow.AddDays(-10));
        File.WriteAllText(folder.CreateFile("project.json"), JsonSerializer.Serialize(new { Cover = referenced }));

        Assert.Empty(new CoverCleanupService().DeleteOldUnreferencedCoverFiles(folder.Path, covers, DateTime.UtcNow, 1));
        Assert.True(File.Exists(referenced));
        Assert.True(File.Exists(recent));
        Assert.False(File.Exists(unused));
    }
}
