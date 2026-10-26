using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectFolderNameServiceTests
{
    [Fact]
    public void CreateUniqueProjectFolder_UsesReadableTimestampWithMilliseconds()
    {
        using var folder = new TemporaryFolder();
        var service = new ProjectFolderNameService();

        var result = service.CreateUniqueProjectFolder(
            folder.Path,
            "Kummer aller Art",
            "Projekt",
            new DateTime(2026, 8, 8, 22, 5, 39, 428));

        Assert.Equal(
            Path.Combine(folder.Path, "Kummer_aller_Art_2026-08-08_22-05-39_428"),
            result);
    }

    [Fact]
    public void CreateUniqueProjectFolder_AppendsCounterWhenFolderAlreadyExists()
    {
        using var folder = new TemporaryFolder();
        var service = new ProjectFolderNameService();
        var timestamp = new DateTime(2026, 8, 8, 22, 5, 39, 428);
        Directory.CreateDirectory(Path.Combine(folder.Path, "Book_2026-08-08_22-05-39_428"));
        Directory.CreateDirectory(Path.Combine(folder.Path, "Book_2026-08-08_22-05-39_428_2"));

        var result = service.CreateUniqueProjectFolder(folder.Path, "Book", "Projekt", timestamp);

        Assert.Equal(
            Path.Combine(folder.Path, "Book_2026-08-08_22-05-39_428_3"),
            result);
    }
}
