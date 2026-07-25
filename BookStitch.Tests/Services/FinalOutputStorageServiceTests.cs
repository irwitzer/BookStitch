using Xunit;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;

namespace BookStitch.Tests.Services;

public sealed class FinalOutputStorageServiceTests
{
    [Fact]
    public void CreateAvailableOutputPath_ReturnsPreferredPath_WhenItDoesNotExist()
    {
        using var folder = new TemporaryFolder();
        var service = new FinalOutputStorageService();
        var preferredPath = Path.Combine(folder.Path, "Autor - Titel.m4b");

        var result = service.CreateAvailableOutputPath(preferredPath);

        Assert.Equal(preferredPath, result);
    }

    [Fact]
    public void CreateAvailableOutputPath_AppendsNextFreeNumber()
    {
        using var folder = new TemporaryFolder();
        var service = new FinalOutputStorageService();
        var preferredPath = Path.Combine(folder.Path, "Autor - Titel.m4b");
        File.WriteAllText(preferredPath, "existing");
        File.WriteAllText(Path.Combine(folder.Path, "Autor - Titel (2).m4b"), "existing");

        var result = service.CreateAvailableOutputPath(preferredPath);

        Assert.Equal(Path.Combine(folder.Path, "Autor - Titel (3).m4b"), result);
    }

    [Fact]
    public void MoveToOutput_CreatesDestinationFolderAndMovesFile()
    {
        using var folder = new TemporaryFolder();
        var service = new FinalOutputStorageService();
        var sourcePath = Path.Combine(folder.Path, "final.part");
        var destinationPath = Path.Combine(folder.Path, "new", "Autor - Titel.m4b");
        File.WriteAllText(sourcePath, "audio");

        service.MoveToOutput(sourcePath, destinationPath, overwrite: false);

        Assert.False(File.Exists(sourcePath));
        Assert.Equal("audio", File.ReadAllText(destinationPath));
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    [InlineData(typeof(DriveNotFoundException))]
    [InlineData(typeof(PathTooLongException))]
    [InlineData(typeof(NotSupportedException))]
    public void IsRecoverableDestinationError_ReturnsTrue_ForExpectedFileErrors(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.True(FinalOutputStorageService.IsRecoverableDestinationError(exception));
    }

    [Fact]
    public void IsRecoverableDestinationError_ReturnsFalse_ForUnexpectedErrors()
    {
        Assert.False(FinalOutputStorageService.IsRecoverableDestinationError(new InvalidOperationException()));
    }
}
