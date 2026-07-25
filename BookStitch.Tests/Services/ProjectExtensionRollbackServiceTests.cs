using BookStitch.Services;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectExtensionRollbackServiceTests
{
    [Fact]
    public void Rollback_RemovesOnlyFilesAndDirectoriesCreatedAfterCapture()
    {
        using var temp = new TemporaryDirectory();
        var existingFolder = Directory.CreateDirectory(Path.Combine(temp.Path, "originals", "CD 01")).FullName;
        var existingFile = Path.Combine(existingFolder, "001.mp3");
        File.WriteAllText(existingFile, "existing");

        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path);

        var newFolder = Directory.CreateDirectory(Path.Combine(temp.Path, "originals", "CD 02")).FullName;
        var newFile = Path.Combine(newFolder, "001.mp3");
        File.WriteAllText(newFile, "new");

        var result = service.Rollback(snapshot);

        Assert.True(File.Exists(existingFile));
        Assert.False(File.Exists(newFile));
        Assert.False(Directory.Exists(newFolder));
        Assert.Equal(1, result.DeletedFiles);
    }

    [Fact]
    public void Rollback_RestoresChangedJsonManifest()
    {
        using var temp = new TemporaryDirectory();
        var manifest = Path.Combine(temp.Path, "project.json");
        File.WriteAllText(manifest, "{\"totalDiscs\":1}");

        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path);
        File.WriteAllText(manifest, "{\"totalDiscs\":2}");

        service.Rollback(snapshot);

        Assert.Equal("{\"totalDiscs\":1}", File.ReadAllText(manifest));
    }

    [Fact]
    public void Rollback_RemovesExplicitGeneratedConvertedFileAndPartFile()
    {
        using var temp = new TemporaryDirectory();
        var convertedFolder = Directory.CreateDirectory(Path.Combine(temp.Path, "converted", "aac_stereo_128k")).FullName;
        var existingFile = Path.Combine(convertedFolder, "001_existing.m4a");
        File.WriteAllText(existingFile, "existing");

        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path);

        var generatedFile = Path.Combine(convertedFolder, "144_added.m4a");
        var generatedPartFile = generatedFile + ".part";
        File.WriteAllText(generatedFile, "new");
        File.WriteAllText(generatedPartFile, "partial");

        service.Rollback(snapshot, new[] { generatedFile });

        Assert.True(File.Exists(existingFile));
        Assert.False(File.Exists(generatedFile));
        Assert.False(File.Exists(generatedPartFile));
    }


    [Fact]
    public async Task Rollback_RemovesFileThatAppearsShortlyAfterCleanupStarts()
    {
        using var temp = new TemporaryDirectory();
        var convertedFolder = Directory.CreateDirectory(Path.Combine(temp.Path, "converted", "aac_stereo_128k")).FullName;

        var service = new ProjectExtensionRollbackService();
        var snapshot = service.Capture(temp.Path);
        var lateFile = Path.Combine(convertedFolder, "144_added.m4a.part");

        var writer = Task.Run(async () =>
        {
            await Task.Delay(75);
            File.WriteAllText(lateFile, "late");
        });

        service.Rollback(snapshot);
        await writer;

        Assert.False(File.Exists(lateFile));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BookStitch.Tests", Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (!Directory.Exists(Path))
                return;

            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
    }
}
