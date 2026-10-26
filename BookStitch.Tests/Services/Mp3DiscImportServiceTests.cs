using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscImportServiceTests
{

    [Fact]
    public void AnalyzeSource_Does_not_accept_audio_and_data_track_media_as_an_mp3_disc()
    {
        using var source = new TemporaryFolder();
        CreateFile(source.Path, "song.mp3", "audio");
        CreateFile(source.Path, "track.cda", "track");
        var result = new Mp3DiscImportService().AnalyzeSource(source.Path);
        Assert.False(result.IsSupportedDataDisc);
        Assert.True(result.IsProbablyAudioCd);
    }

    [Fact]
    public async Task CopyDiscAsync_Repairs_a_changed_target_of_the_same_size()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        var file = Path.Combine(source.Path, "001.mp3");
        File.WriteAllBytes(file, [1, 2, 3]);
        var service = new Mp3DiscImportService();
        await service.CopyDiscAsync(source.Path, project.Path, 1, 1, null, CancellationToken.None);
        var target = Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 1), "001.mp3");
        File.WriteAllBytes(target, [9, 8, 7]);

        var result = await service.CopyDiscAsync(source.Path, project.Path, 1, 1, null, CancellationToken.None);

        Assert.False(result.WasCanceled);
        Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(target));
        Assert.Empty(Directory.EnumerateFiles(project.Path, "*.copying", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CopyDiscAsync_Canceled_before_analysis_creates_no_partial_copy()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        File.WriteAllBytes(Path.Combine(source.Path, "001.mp3"), [1, 2, 3]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new Mp3DiscImportService().CopyDiscAsync(source.Path, project.Path,
            1, 1, null, cancellation.Token);
        Assert.True(result.WasCanceled);
        Assert.Equal(0, result.CopiedFiles);
        Assert.Empty(Directory.EnumerateFiles(project.Path, "*.copying", SearchOption.AllDirectories));
    }

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
    public void AnalyzeSource_UsesCatalogDataDiscFormatSet()
    {
        using var folder = new TemporaryFolder();
        foreach (var fileName in new[] { "001.mp3", "002.m4a", "003.m4b", "004.aac", "005.wav", "006.flac", "007.wma", "008.ogg", "009.oga", "010.opus" })
            CreateFile(folder.Path, fileName, "audio");
        CreateFile(folder.Path, "ignored.webm", "audio");

        var analysis = new Mp3DiscImportService().AnalyzeSource(folder.Path);

        Assert.Equal(
            ["001.mp3", "002.m4a", "003.m4b", "004.aac", "005.wav", "006.flac", "007.wma", "008.ogg", "009.oga", "010.opus"],
            analysis.SupportedAudioFiles.Select(path => Path.GetFileName(path) ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task CopyDiscAsync_WhenMatchingFilesAlreadyExist_PreservesAndReusesThem()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateFile(source.Path, "001.mp3", "audio one");
        CreateFile(source.Path, "002.mp3", "audio two");

        var service = new Mp3DiscImportService();
        await service.CopyDiscAsync(
            source.Path,
            project.Path,
            discNumber: 1,
            totalDiscs: 1,
            progress: null,
            copiedFileProgress: null,
            CancellationToken.None);

        var discFolder = ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 1);
        var existingPath = Path.Combine(discFolder, "001.mp3");
        var originalWriteTime = DateTime.UtcNow.AddMinutes(-10);
        File.SetLastWriteTimeUtc(existingPath, originalWriteTime);

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
        Assert.Empty(newlyCopiedFiles);
        Assert.Equal(2, result.ImportedFiles?.Count);
    }

    [Fact]
    public async Task CopyDiscAsync_DoesNotReuseEqualSizedFileWhenSourceIdentityDiffers()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateFile(source.Path, "001.mp3", "audio one");
        var service = new Mp3DiscImportService();

        await service.CopyDiscAsync(
            source.Path,
            project.Path,
            discNumber: 1,
            totalDiscs: 1,
            progress: null,
            copiedFileProgress: null,
            CancellationToken.None);

        File.SetLastWriteTimeUtc(
            Path.Combine(source.Path, "001.mp3"),
            File.GetLastWriteTimeUtc(Path.Combine(source.Path, "001.mp3")).AddMinutes(5));
        var newlyCopiedFiles = new List<DiscCopiedFile>();

        var result = await service.CopyDiscAsync(
            source.Path,
            project.Path,
            discNumber: 1,
            totalDiscs: 1,
            progress: null,
            copiedFileProgress: new CollectingProgress<DiscCopiedFile>(newlyCopiedFiles.Add),
            CancellationToken.None);

        var copiedFile = Assert.Single(newlyCopiedFiles);
        Assert.Equal("001.mp3", Path.GetFileName(copiedFile.ImportedFile));
        Assert.NotNull(copiedFile.SourceIdentity);
        Assert.Equal(copiedFile.SourceIdentity!.SourceLastWriteUtcTicks, result.ImportedFiles!.Single().SourceLastWriteUtcTicks);
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
