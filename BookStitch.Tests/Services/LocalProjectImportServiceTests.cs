using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LocalProjectImportServiceTests
{
    private readonly LocalProjectImportService _service = new();

    [Fact]
    public void CreateProjectFolder_UsesSourceNameAndStableTimestamp()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "Kummer aller Art");
        var projectsFolder = Path.Combine(folder.Path, "Projects", "LocalProjects");
        Directory.CreateDirectory(sourceFolder);

        var result = _service.CreateProjectFolder(
            projectsFolder,
            sourceFolder,
            new DateTime(2026, 7, 17, 14, 30, 45));

        Assert.Equal(
            Path.Combine(projectsFolder, "Kummer_aller_Art_20260717_143045"),
            result);
    }

    [Fact]
    public async Task CopySourcesAsync_PreservesRelativeFoldersAndReportsFinalizedFiles()
    {
        using var source = new TemporaryFolder();
        using var projects = new TemporaryFolder();
        var firstFile = CreateFile(source.Path, "Track 01.mp3", "audio 1");
        var secondFile = CreateFile(source.Path, Path.Combine("CD 02", "Track 02.flac"), "audio 2");
        var projectFolder = Path.Combine(projects.Path, "Project");
        var progressItems = new List<LocalProjectCopyProgress>();
        var copiedItems = new List<LocalProjectCopiedFile>();

        var result = await _service.CopySourcesAsync(
            source.Path,
            [secondFile, firstFile],
            projectFolder,
            new CollectingProgress<LocalProjectCopyProgress>(progressItems.Add),
            new CollectingProgress<LocalProjectCopiedFile>(copiedItems.Add),
            CancellationToken.None);

        var originalsFolder = Path.Combine(projectFolder, LocalProjectImportService.OriginalsFolderName);
        Assert.False(result.WasCanceled);
        Assert.Equal(2, result.TotalFiles);
        Assert.Equal(2, result.CompletedFiles);
        Assert.Equal(originalsFolder, result.OriginalsFolder);
        Assert.True(File.Exists(Path.Combine(originalsFolder, "Track 01.mp3")));
        Assert.True(File.Exists(Path.Combine(originalsFolder, "CD 02", "Track 02.flac")));
        Assert.Empty(Directory.EnumerateFiles(projectFolder, "*.copying", SearchOption.AllDirectories));
        Assert.Equal(2, progressItems.Count);
        Assert.Equal(2, copiedItems.Count);
        Assert.All(copiedItems, item => Assert.False(item.WasReused));
        Assert.All(copiedItems, item => Assert.True(File.Exists(item.TargetFile)));
    }

    [Fact]
    public async Task CopySourcesAsync_ReusesCompleteExistingCopy()
    {
        using var source = new TemporaryFolder();
        using var projects = new TemporaryFolder();
        var sourceFile = CreateFile(source.Path, "Track 01.mp3", "same audio bytes");
        var projectFolder = Path.Combine(projects.Path, "Project");
        var existingTarget = CreateFile(
            Path.Combine(projectFolder, LocalProjectImportService.OriginalsFolderName),
            "Track 01.mp3",
            "same audio bytes");
        var originalWriteTime = File.GetLastWriteTimeUtc(existingTarget);
        var copiedItems = new List<LocalProjectCopiedFile>();

        var result = await _service.CopySourcesAsync(
            source.Path,
            [sourceFile],
            projectFolder,
            progress: null,
            new CollectingProgress<LocalProjectCopiedFile>(copiedItems.Add),
            CancellationToken.None);

        Assert.False(result.WasCanceled);
        Assert.Single(copiedItems);
        Assert.True(copiedItems[0].WasReused);
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(existingTarget));
    }

    [Fact]
    public async Task CopySourcesAsync_WhenCanceledBeforeStart_LeavesNoTemporaryFile()
    {
        using var source = new TemporaryFolder();
        using var projects = new TemporaryFolder();
        var sourceFile = CreateFile(source.Path, "Track 01.mp3", "audio");
        var projectFolder = Path.Combine(projects.Path, "Project");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await _service.CopySourcesAsync(
            source.Path,
            [sourceFile],
            projectFolder,
            progress: null,
            copiedFileProgress: null,
            cancellation.Token);

        Assert.True(result.WasCanceled);
        Assert.Equal(0, result.CompletedFiles);
        Assert.Empty(Directory.EnumerateFiles(projectFolder, "*.copying", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CopySourcesAsync_RejectsFileOutsideSourceFolder()
    {
        using var source = new TemporaryFolder();
        using var outside = new TemporaryFolder();
        using var projects = new TemporaryFolder();
        var outsideFile = CreateFile(outside.Path, "Track 01.mp3", "audio");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CopySourcesAsync(
                source.Path,
                [outsideFile],
                Path.Combine(projects.Path, "Project"),
                progress: null,
                copiedFileProgress: null,
                CancellationToken.None));

        Assert.Contains("außerhalb", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateFile(string folder, string relativePath, string content)
    {
        var filePath = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private sealed class CollectingProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
