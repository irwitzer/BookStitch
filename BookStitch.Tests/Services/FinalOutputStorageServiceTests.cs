using Xunit;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;

namespace BookStitch.Tests.Services;

public sealed class FinalOutputStorageServiceTests
{
    [Fact]
    public void MoveToOutput_accepts_the_maximum_valid_final_path_length()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "final.part");
        var nameLength = WindowsOutputPathService.MaximumPathLength - folder.Path.Length - 1 - ".m4b".Length;
        var destination = Path.Combine(folder.Path, new string('a', nameLength) + ".m4b");
        File.WriteAllText(source, "complete audio");
        Assert.True(WindowsOutputPathService.IsValid(destination));
        new FinalOutputStorageService().MoveToOutput(source, destination, false);
        Assert.Equal("complete audio", File.ReadAllText(destination));
        Assert.False(File.Exists(source));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.bookstitch-part*"));
    }

    [Fact]
    public void MoveToOutput_committed_destination_survives_working_copy_cleanup_failure()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "final.part");
        var destination = Path.Combine(folder.Path, "book.m4b");
        File.WriteAllText(source, "complete audio");
        var cleanupCalls = 0;
        var service = new FinalOutputStorageService(path =>
        { cleanupCalls++; Assert.Equal(source, path); throw new IOException("Cleanup denied"); });
        service.MoveToOutput(source, destination, false);
        Assert.Equal(1, cleanupCalls);
        Assert.Equal("complete audio", File.ReadAllText(destination));
        Assert.Equal("complete audio", File.ReadAllText(source));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.bookstitch-part*"));
    }

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

    [Fact]
    public void MoveToOutput_WhenOverwriteIsFalseAndDestinationExists_KeepsExistingOutputAndSource()
    {
        using var folder = new TemporaryFolder();
        var service = new FinalOutputStorageService();
        var sourcePath = Path.Combine(folder.Path, "final.part");
        var destinationPath = Path.Combine(folder.Path, "Autor - Titel.m4b");
        File.WriteAllText(sourcePath, "new audio");
        File.WriteAllText(destinationPath, "existing audio");

        Assert.Throws<IOException>(() => service.MoveToOutput(sourcePath, destinationPath, overwrite: false));

        Assert.Equal("existing audio", File.ReadAllText(destinationPath));
        Assert.Equal("new audio", File.ReadAllText(sourcePath));
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.bookstitch-part*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void MoveToOutput_WhenOverwriteIsTrue_ReplacesOnlyAfterDestinationSideTempCommit()
    {
        using var folder = new TemporaryFolder();
        var service = new FinalOutputStorageService();
        var sourcePath = Path.Combine(folder.Path, "final.part");
        var destinationPath = Path.Combine(folder.Path, "Autor - Titel.m4b");
        File.WriteAllText(sourcePath, "new audio");
        File.WriteAllText(destinationPath, "existing audio");

        service.MoveToOutput(sourcePath, destinationPath, overwrite: true);

        Assert.False(File.Exists(sourcePath));
        Assert.Equal("new audio", File.ReadAllText(destinationPath));
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.bookstitch-part*", SearchOption.TopDirectoryOnly));
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
