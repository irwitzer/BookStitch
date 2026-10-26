using BookStitch.Services;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConvertedFileCleanupServiceTests
{
    [Fact]
    public void Windows_locked_part_is_deferred_and_the_next_cleanup_removes_it()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var folder = new TemporaryDirectory();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var part = Path.Combine(ProjectFolderLayout.GetConvertedFolder(folder.Path), "track.m4a.part");
        File.WriteAllText(part, "incomplete");
        var cleanup = new ConvertedFileCleanupService();
        using (var held = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            cleanup.DeletePartFiles(folder.Path);
            Assert.True(File.Exists(part));
        }
        cleanup.DeletePartFiles(folder.Path);
        Assert.False(File.Exists(part));
    }

    [Theory]
    [InlineData("track.mp3")]
    [InlineData("track.m4a")]
    [InlineData("project.json")]
    public void Temporary_marker_in_parent_directory_does_not_mark_complete_file(string name)
    {
        var path = Path.Combine("Story.part.01", "originals", name);
        Assert.False(ConvertedFileCleanupService.IsPartFilePath(path));
        Assert.False(ConvertedFileCleanupService.IsIncompleteFilePath(path));
    }

    [Fact]
    public void Cleanup_preserves_source_files_with_temporary_looking_names()
    {
        using var temp = new TemporaryDirectory();
        var project = Path.Combine(temp.Path, "converted", "project");
        var originals = ProjectFolderLayout.GetOriginalsFolder(project);
        Directory.CreateDirectory(originals);
        foreach (var name in new[] { "Book.part.01.mp3", "Book.part", "Book.mp3.copying", "Book.part.01.m4a" })
            File.WriteAllText(Path.Combine(originals, name), "original source");
        new ConvertedFileCleanupService().DeletePartFiles(project);
        new ConvertedFileCleanupService().DeletePartFiles(originals);
        new ConvertedFileCleanupService().DeleteUnusedConvertedFiles(originals, []);
        Assert.Equal(4, Directory.GetFiles(originals).Length);
        Assert.All(Directory.GetFiles(originals), path => Assert.Equal("original source", File.ReadAllText(path)));
    }

    [Fact]
    public void Cleanup_does_not_follow_directory_links_in_managed_cache()
    {
        using var project = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var cache = ProjectFolderLayout.GetConvertedFolder(project.Path);
        Directory.CreateDirectory(cache);
        var link = Path.Combine(cache, "linked");
        var source = Path.Combine(outside.Path, "source.part.mp3");
        File.WriteAllText(source, "source");
        if (OperatingSystem.IsWindows())
        {
            var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
            start.ArgumentList.Add(link); start.ArgumentList.Add(outside.Path);
            using var process = System.Diagnostics.Process.Start(start)!;
            Assert.True(process.WaitForExit(10000)); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, outside.Path);
        try
        {
            var cleanup = new ConvertedFileCleanupService();
            cleanup.DeletePartFiles(project.Path);
            cleanup.TryDeleteFile(Path.Combine(link, "source.part.mp3"));
            cleanup.DeleteUnusedConvertedFiles(link, []);
            Assert.Equal("source", File.ReadAllText(source));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void Cleanup_preserves_complete_files_below_directory_with_part_marker()
    {
        using var temp = new TemporaryDirectory();
        var folder = Path.Combine(temp.Path, "Story.part.01", "originals");
        Directory.CreateDirectory(folder);
        var complete = Path.Combine(folder, "source.mp3");
        var metadata = Path.Combine(folder, "project.json");
        var cache = ProjectFolderLayout.GetConvertedFolder(temp.Path);
        Directory.CreateDirectory(cache);
        var partial = Path.Combine(cache, "converted.part.m4a");
        File.WriteAllText(complete, "complete source fixture");
        File.WriteAllText(metadata, "complete metadata fixture");
        File.WriteAllText(partial, "incomplete fixture");

        new ConvertedFileCleanupService().DeletePartFiles(temp.Path);

        Assert.True(File.Exists(complete));
        Assert.Equal("complete source fixture", File.ReadAllText(complete));
        Assert.True(File.Exists(metadata));
        Assert.Equal("complete metadata fixture", File.ReadAllText(metadata));
        Assert.False(File.Exists(partial));
    }

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
        var nestedFolder = Path.Combine(temp.Path, "converted", "nested");
        Directory.CreateDirectory(nestedFolder);

        var partFile = Path.Combine(nestedFolder, "first.part.m4a");
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
