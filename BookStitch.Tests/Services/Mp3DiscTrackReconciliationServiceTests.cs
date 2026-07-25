using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscTrackReconciliationServiceTests
{
    private readonly Mp3DiscTrackReconciliationService _service = new();

    [Fact]
    public void AppendMissingPreviewTracks_DoesNotDuplicateExistingImportedTracks()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        var projectFolder = Path.Combine(folder.Path, "project");
        Directory.CreateDirectory(Path.Combine(sourceFolder, "sub"));
        Directory.CreateDirectory(Path.Combine(projectFolder, "CD 01", "sub"));

        var existing = new List<TrackInfo>
        {
            new()
            {
                DiscNumber = 1,
                FilePath = Path.Combine(projectFolder, "CD 01", "sub", "001.mp3"),
                FileName = "001.mp3"
            }
        };

        var preview = new[]
        {
            new TrackInfo
            {
                FilePath = Path.Combine(sourceFolder, "sub", "001.mp3"),
                FileName = "001.mp3"
            }
        };

        var added = _service.AppendMissingPreviewTracks(
            existing,
            preview,
            sourceFolder,
            discNumber: 1,
            clearExistingTracks: false);

        Assert.Equal(0, added);
        Assert.Single(existing);
    }

    [Fact]
    public void AppendMissingPreviewTracks_AddsOnlyTracksNotAlreadyKnownForDisc()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        Directory.CreateDirectory(sourceFolder);

        var existing = new List<TrackInfo>
        {
            new()
            {
                DiscNumber = 1,
                FilePath = Path.Combine(sourceFolder, "001.mp3"),
                FileName = "001.mp3"
            }
        };

        var preview = new[]
        {
            new TrackInfo { FilePath = Path.Combine(sourceFolder, "001.mp3"), FileName = "001.mp3" },
            new TrackInfo { FilePath = Path.Combine(sourceFolder, "002.mp3"), FileName = "002.mp3" }
        };

        var added = _service.AppendMissingPreviewTracks(
            existing,
            preview,
            sourceFolder,
            discNumber: 1,
            clearExistingTracks: false);

        Assert.Equal(1, added);
        Assert.Equal(2, existing.Count);
        Assert.Equal("002.mp3", existing[1].FileName);
    }

    [Fact]
    public void ReconcileImportedTrackPaths_UsesCopiedProjectFileAndKeepsUncopiedPreviewTrack()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        var projectFolder = Path.Combine(folder.Path, "project");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(Path.Combine(projectFolder, "CD 01"));

        var copiedSourcePath = Path.Combine(sourceFolder, "001.mp3");
        var uncopiedSourcePath = Path.Combine(sourceFolder, "002.mp3");
        var importedPath = Path.Combine(projectFolder, "CD 01", "001.mp3");
        File.WriteAllText(importedPath, "copied");

        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, FilePath = copiedSourcePath, FileName = "001.mp3" },
            new() { DiscNumber = 1, FilePath = uncopiedSourcePath, FileName = "002.mp3" }
        };

        var changed = _service.ReconcileImportedTrackPaths(
            tracks,
            sourceFolder,
            projectFolder,
            discNumber: 1);

        Assert.Equal(1, changed);
        Assert.Equal(importedPath, tracks[0].FilePath);
        Assert.Equal(uncopiedSourcePath, tracks[1].FilePath);
    }

    [Fact]
    public void ReconcileImportedTrackPathsForExistingDiscs_MapsPreviouslyCopiedTracksAcrossDiscs()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        var projectFolder = Path.Combine(folder.Path, "project");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(Path.Combine(projectFolder, "CD 01"));
        Directory.CreateDirectory(Path.Combine(projectFolder, "CD 02"));

        var previewPath = Path.Combine(sourceFolder, "001.mp3");
        var firstImportedPath = Path.Combine(projectFolder, "CD 01", "001.mp3");
        var secondImportedPath = Path.Combine(projectFolder, "CD 02", "001.mp3");
        File.WriteAllText(firstImportedPath, "disc one");
        File.WriteAllText(secondImportedPath, "disc two");

        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, FilePath = previewPath, FileName = "001.mp3" },
            new() { DiscNumber = 2, FilePath = previewPath, FileName = "001.mp3" }
        };

        var changed = _service.ReconcileImportedTrackPathsForExistingDiscs(
            tracks,
            sourceFolder,
            projectFolder,
            totalDiscs: 2);

        Assert.Equal(2, changed);
        Assert.Equal(firstImportedPath, tracks[0].FilePath);
        Assert.Equal(secondImportedPath, tracks[1].FilePath);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
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
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
