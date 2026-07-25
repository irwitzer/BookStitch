using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscImportWorkflowServiceTests
{
    [Fact]
    public void CountCompletedCopiedFiles_SumsOnlyCompletedDiscs()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed, CopiedFiles = 12 },
                new Mp3DiscProjectManifestDisc { DiscNumber = 2, Status = "Canceled", CopiedFiles = 7 },
                new Mp3DiscProjectManifestDisc { DiscNumber = 3, Status = Mp3DiscImportStatus.Completed, CopiedFiles = 8 }
            ]
        };

        var service = CreateService();

        Assert.Equal(20, service.CountCompletedCopiedFiles(manifest));
    }

    [Fact]
    public void BuildCompletedDiscSignatureSet_ReturnsOnlyCompletedNonEmptySignatures()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed, Signature = "ABC" },
                new Mp3DiscProjectManifestDisc { DiscNumber = 2, Status = "Canceled", Signature = "DEF" },
                new Mp3DiscProjectManifestDisc { DiscNumber = 3, Status = Mp3DiscImportStatus.Completed, Signature = "" },
                new Mp3DiscProjectManifestDisc { DiscNumber = 4, Status = Mp3DiscImportStatus.Completed, Signature = "abc" }
            ]
        };

        var service = CreateService();
        var signatures = service.BuildCompletedDiscSignatureSet(manifest);

        Assert.Single(signatures);
        Assert.Contains("ABC", signatures);
        Assert.Contains("abc", signatures);
        Assert.DoesNotContain("DEF", signatures);
    }

    [Fact]
    public void AnalyzeDiscForImport_DetectsAlreadyImportedSignature()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = folder.Path;
        CreateNonEmptyFile(sourceFolder, "Track 01.mp3", "audio 1");
        CreateNonEmptyFile(sourceFolder, "Track 02.mp3", "audio 2");
        var service = CreateService();
        var first = service.AnalyzeDiscForImport(sourceFolder, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var importedSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first.Signature.ToLowerInvariant() };

        var second = service.AnalyzeDiscForImport(sourceFolder, importedSignatures);

        Assert.True(first.Analysis.IsSupportedDataDisc);
        Assert.Equal(2, first.TotalFiles);
        Assert.False(first.IsAlreadyImported);
        Assert.True(second.IsAlreadyImported);
        Assert.Equal(first.Signature, second.Signature);
    }

    [Fact]
    public async Task CopyDiscAsync_CopiesFilesAndReportsProgress()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "Track 01.mp3", "audio 1");
        CreateNonEmptyFile(source.Path, Path.Combine("Sub", "Track 02.m4a"), "audio 2");
        var service = CreateService();
        var progressItems = new List<DiscCopyProgress>();
        var copiedItems = new List<DiscCopiedFile>();

        var result = await service.CopyDiscAsync(
            source.Path,
            project.Path,
            discNumber: 2,
            totalDiscs: 4,
            new CollectingProgress<DiscCopyProgress>(progressItems.Add),
            new CollectingProgress<DiscCopiedFile>(copiedItems.Add),
            CancellationToken.None);

        Assert.False(result.WasCanceled);
        Assert.Equal(2, result.CopiedFiles);
        Assert.True(File.Exists(Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 2), "Track 01.mp3")));
        Assert.True(File.Exists(Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 2), "Sub", "Track 02.m4a")));
        Assert.Equal(2, progressItems.Count);
        Assert.Equal(2, copiedItems.Count);
        Assert.All(copiedItems, item => Assert.Equal(2, item.DiscNumber));
    }


    [Fact]
    public async Task ImportDiscAsync_CopiesDiscMarksCompletedAndAddsSignature()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "Track 01.mp3", "audio 1");
        CreateNonEmptyFile(source.Path, "Track 02.mp3", "audio 2");
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = project.Path,
            ImportedDiscs = []
        };
        var importedSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var service = CreateService();
        var readyToCopyCalled = false;

        var result = await service.ImportDiscAsync(
            source.Path,
            project.Path,
            manifest,
            discNumber: 1,
            totalDiscs: 2,
            importedSignatures,
            progress: null,
            copiedFileProgress: null,
            onReadyToCopy: precheck =>
            {
                readyToCopyCalled = true;
                Assert.Equal(2, precheck.TotalFiles);
            },
            CancellationToken.None);

        Assert.True(readyToCopyCalled);
        Assert.False(result.IsAlreadyImported);
        Assert.False(result.WasCanceled);
        Assert.Equal(2, result.CopiedFiles);
        Assert.Single(importedSignatures);
        Assert.Single(manifest.ImportedDiscs);
        Assert.Equal(Mp3DiscImportStatus.Completed, manifest.ImportedDiscs[0].Status);
        Assert.Equal(2, manifest.ImportedDiscs[0].CopiedFiles);
    }

    [Fact]
    public async Task ImportDiscAsync_WhenSignatureAlreadyImported_DoesNotCopyOrMarkCompleted()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        CreateNonEmptyFile(source.Path, "Track 01.mp3", "audio 1");
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = project.Path,
            ImportedDiscs = []
        };
        var service = CreateService();
        var signature = service.AnalyzeDiscForImport(
            source.Path,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Signature;
        var importedSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { signature };
        var readyToCopyCalled = false;

        var result = await service.ImportDiscAsync(
            source.Path,
            project.Path,
            manifest,
            discNumber: 1,
            totalDiscs: 2,
            importedSignatures,
            progress: null,
            copiedFileProgress: null,
            onReadyToCopy: _ => readyToCopyCalled = true,
            CancellationToken.None);

        Assert.True(result.IsAlreadyImported);
        Assert.Null(result.ImportResult);
        Assert.False(readyToCopyCalled);
        Assert.Empty(manifest.ImportedDiscs);
        Assert.False(Directory.Exists(ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 1)));
    }

    [Fact]
    public void MarkDiscCompleted_UpdatesManifestThroughProjectService()
    {
        using var source = new TemporaryFolder();
        using var project = new TemporaryFolder();
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = project.Path,
            ImportedDiscs = []
        };
        var importResult = new DiscImportResult(
            source.Path,
            ProjectFolderLayout.GetDiscOriginalsFolder(project.Path, 1),
            project.Path,
            DiscNumber: 1,
            TotalDiscs: 3,
            CopiedFiles: 5);
        var service = CreateService();

        service.MarkDiscCompleted(
            manifest,
            discNumber: 1,
            signature: "SIGNATURE",
            source.Path,
            importResult,
            fileCount: 5);

        var disc = Assert.Single(manifest.ImportedDiscs);
        Assert.Equal(1, disc.DiscNumber);
        Assert.Equal(Mp3DiscImportStatus.Completed, disc.Status);
        Assert.Equal("SIGNATURE", disc.Signature);
        Assert.Equal(importResult.ImportedFolder, disc.LocalFolder);
        Assert.Equal(5, disc.FileCount);
        Assert.Equal(5, disc.CopiedFiles);
    }

    private static Mp3DiscImportWorkflowService CreateService()
    {
        return new Mp3DiscImportWorkflowService(
            new Mp3DiscImportService(),
            new Mp3DiscProjectService());
    }

    private static string CreateNonEmptyFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private sealed class CollectingProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
