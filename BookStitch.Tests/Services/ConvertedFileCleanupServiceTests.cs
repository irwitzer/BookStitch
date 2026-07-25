using BookStitch.Services;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConvertedFileCleanupServiceTests
{
    [Theory]
    [InlineData("track.part")]
    [InlineData("track.part.m4a")]
    [InlineData("track.m4a.copying")]
    [InlineData("TRACK.M4A.COPYING")]
    public void IsIncompleteFilePath_RecognizesTemporaryOutputFiles(string fileName)
    {
        Assert.True(ConvertedFileCleanupService.IsIncompleteFilePath(fileName));
    }

    [Theory]
    [InlineData("track.m4a")]
    [InlineData("track.flac")]
    [InlineData("copying-track.m4a")]
    public void IsIncompleteFilePath_DoesNotMatchCompleteFiles(string fileName)
    {
        Assert.False(ConvertedFileCleanupService.IsIncompleteFilePath(fileName));
    }

    [Fact]
    public void DeletePartFiles_RemovesPartAndCopyingFilesRecursively()
    {
        using var temp = new TemporaryDirectory();
        var nestedFolder = Path.Combine(temp.Path, "nested");
        Directory.CreateDirectory(nestedFolder);

        var partFile = Path.Combine(temp.Path, "first.part.m4a");
        var copyingFile = Path.Combine(nestedFolder, "second.m4a.copying");
        var completeFile = Path.Combine(nestedFolder, "third.m4a");

        File.WriteAllText(partFile, "partial");
        File.WriteAllText(copyingFile, "partial");
        File.WriteAllText(completeFile, "complete");

        new ConvertedFileCleanupService().DeletePartFiles(temp.Path);

        Assert.False(File.Exists(partFile));
        Assert.False(File.Exists(copyingFile));
        Assert.True(File.Exists(completeFile));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "BookStitch.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Test cleanup must not hide the assertion result.
            }
        }
    }
}
