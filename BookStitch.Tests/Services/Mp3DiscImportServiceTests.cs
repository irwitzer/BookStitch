using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscImportServiceTests
{

    [Fact]
    public void CreateDiscProjectFolder_UsesProvidedMp3DiscProjectsFolder()
    {
        using var source = new TemporaryFolder();
        using var projects = new TemporaryFolder();
        var mp3ProjectsFolder = Path.Combine(projects.Path, "MP3DiscProjects");
        var service = new Mp3DiscImportService();

        var projectFolder = service.CreateDiscProjectFolder(source.Path, mp3ProjectsFolder);

        Assert.Equal(Path.GetFullPath(mp3ProjectsFolder), Directory.GetParent(projectFolder)!.FullName);
        Assert.True(Directory.Exists(mp3ProjectsFolder));
        Assert.False(projectFolder.Contains($"{Path.DirectorySeparatorChar}DiscProjects{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckQuickIdentity_WhenSourceIsUnchanged_ReturnsMatch()
    {
        using var folder = new TemporaryFolder();
        CreateFile(folder.Path, "001.mp3", "audio one");
        CreateFile(folder.Path, "002.mp3", "audio two");
        CreateFile(folder.Path, "003.mp3", "audio three");
        CreateFile(folder.Path, "004.mp3", "audio four");
        CreateFile(folder.Path, "005.mp3", "audio five");
        var service = new Mp3DiscImportService();
        var analysis = service.AnalyzeSource(folder.Path);
        var identity = service.CreateQuickIdentity(folder.Path, analysis);

        var result = service.CheckQuickIdentity(folder.Path, identity);

        Assert.Equal(DiscSourceQuickCheckStatus.Match, result.Status);
        Assert.True(result.IsMatch);
        Assert.Equal(3, identity.Samples.Count);
    }

    [Fact]
    public void CheckQuickIdentity_WhenReferenceFileIsMissing_ReturnsMismatch()
    {
        using var folder = new TemporaryFolder();
        CreateFile(folder.Path, "001.mp3", "audio one");
        CreateFile(folder.Path, "002.mp3", "audio two");
        CreateFile(folder.Path, "003.mp3", "audio three");
        var service = new Mp3DiscImportService();
        var analysis = service.AnalyzeSource(folder.Path);
        var identity = service.CreateQuickIdentity(folder.Path, analysis);
        var missingSample = identity.Samples[1];
        File.Delete(Path.Combine(folder.Path, missingSample.RelativePath));

        var result = service.CheckQuickIdentity(folder.Path, identity);

        Assert.Equal(DiscSourceQuickCheckStatus.Mismatch, result.Status);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void CheckQuickIdentity_WhenReferenceFileSizeChanged_ReturnsMismatch()
    {
        using var folder = new TemporaryFolder();
        CreateFile(folder.Path, "001.mp3", "audio one");
        CreateFile(folder.Path, "002.mp3", "audio two");
        var service = new Mp3DiscImportService();
        var analysis = service.AnalyzeSource(folder.Path);
        var identity = service.CreateQuickIdentity(folder.Path, analysis);
        var changedSample = identity.Samples[0];
        File.AppendAllText(Path.Combine(folder.Path, changedSample.RelativePath), " changed");

        var result = service.CheckQuickIdentity(folder.Path, identity);

        Assert.Equal(DiscSourceQuickCheckStatus.Mismatch, result.Status);
    }

    [Fact]
    public void CheckQuickIdentity_WhenSourceDoesNotExist_ReturnsUnavailable()
    {
        using var folder = new TemporaryFolder();
        CreateFile(folder.Path, "001.mp3", "audio one");
        var service = new Mp3DiscImportService();
        var analysis = service.AnalyzeSource(folder.Path);
        var identity = service.CreateQuickIdentity(folder.Path, analysis);
        var missingSource = Path.Combine(folder.Path, "missing");

        var result = service.CheckQuickIdentity(missingSource, identity);

        Assert.Equal(DiscSourceQuickCheckStatus.Unavailable, result.Status);
        Assert.False(result.IsMatch);
    }


    [Fact]
    public void CreateDiscStructureSignature_SameRelativeFilesInDifferentFolders_Matches()
    {
        using var first = new TemporaryFolder();
        using var second = new TemporaryFolder();
        CreateFile(first.Path, Path.Combine("CD", "001.mp3"), "first content");
        CreateFile(first.Path, Path.Combine("CD", "002.mp3"), "second content");
        CreateFile(second.Path, Path.Combine("CD", "001.mp3"), "different bytes");
        CreateFile(second.Path, Path.Combine("CD", "002.mp3"), "other bytes");
        var service = new Mp3DiscImportService();

        var firstSignature = service.CreateDiscStructureSignature(first.Path, service.AnalyzeSource(first.Path));
        var secondSignature = service.CreateDiscStructureSignature(second.Path, service.AnalyzeSource(second.Path));

        Assert.Equal(firstSignature, secondSignature);
    }

    [Fact]
    public void CreateDiscStructureSignature_WhenRelativeFileListChanges_Differs()
    {
        using var first = new TemporaryFolder();
        using var second = new TemporaryFolder();
        CreateFile(first.Path, "001.mp3", "audio");
        CreateFile(second.Path, "002.mp3", "audio");
        var service = new Mp3DiscImportService();

        var firstSignature = service.CreateDiscStructureSignature(first.Path, service.AnalyzeSource(first.Path));
        var secondSignature = service.CreateDiscStructureSignature(second.Path, service.AnalyzeSource(second.Path));

        Assert.NotEqual(firstSignature, secondSignature);
    }

    [Fact]
    public async Task CopyDiscAsync_WhenMatchingFilesAlreadyExist_PreservesAndReusesThem()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateFile(source.Path, "001.mp3", "audio one");
        CreateFile(source.Path, "002.mp3", "audio two");

        var discFolder = ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 1);
        Directory.CreateDirectory(discFolder);
        var existingPath = Path.Combine(discFolder, "001.mp3");
        File.Copy(Path.Combine(source.Path, "001.mp3"), existingPath);
        var originalWriteTime = DateTime.UtcNow.AddMinutes(-10);
        File.SetLastWriteTimeUtc(existingPath, originalWriteTime);

        var service = new Mp3DiscImportService();
        var newlyCopiedFiles = new List<DiscCopiedFile>();

        var result = await service.CopyDiscAsync(
            source.Path,
            project.Path,
            discNumber: 1,
            totalDiscs: 1,
            progress: null,
            copiedFileProgress: new CollectingProgress<DiscCopiedFile>(newlyCopiedFiles.Add),
            CancellationToken.None);

        Assert.False(result.WasCanceled);
        Assert.Equal(2, result.CopiedFiles);
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(existingPath));
        Assert.True(File.Exists(Path.Combine(discFolder, "002.mp3")));
        var copiedFile = Assert.Single(newlyCopiedFiles);
        Assert.Equal("002.mp3", Path.GetFileName(copiedFile.ImportedFile));
    }

    private sealed class CollectingProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private static void CreateFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
