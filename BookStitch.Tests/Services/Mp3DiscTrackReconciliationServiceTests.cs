using BookStitch.Models;
using BookStitch.Models.TrackOrdering;
using BookStitch.Services;
using BookStitch.Services.TrackOrdering;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscTrackReconciliationServiceTests
{
    private readonly Mp3DiscTrackReconciliationService _service = new();

    [Fact]
    public void Accepted_preview_identity_checkpoint_preserves_edits_when_imported_paths_replace_source_paths()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        var projectFolder = Path.Combine(folder.Path, "project");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(projectFolder);
        var files = Enumerable.Range(1, 3).Select(number =>
        {
            var path = Path.Combine(sourceFolder, $"{number:000}.mp3");
            File.WriteAllText(path, "synthetic source");
            return path;
        }).ToArray();
        var previewIds = Mp3DiscSourceIdentityPlanner.Create(sourceFolder, "structure-signature", files);
        var acceptedIds = Mp3DiscSourceIdentityPlanner.Create(sourceFolder, "full-signature", files);
        var preview = new[] { 2, 0, 1 }.Select(index => new TrackInfo
        {
            FilePath = files[index], FileName = Path.GetFileName(files[index]), DiscNumber = 1,
            SourceId = previewIds[index].SourceId, AutomaticOrderPosition = index + 1,
            IsExcluded = index == 0, ExcludedChapterTitle = index == 0 ? "User chapter" : ""
        }).ToList();
        var state = new TrackListStateService();
        state.Save(projectFolder, preview);

        Assert.Equal(3, _service.ApplyPlannedSourceIds(preview, sourceFolder, 1, acceptedIds));
        Assert.All(preview, track => Assert.DoesNotContain(previewIds, item => item.SourceId == track.SourceId));
        state.Save(projectFolder, preview);

        var originals = ProjectFolderLayout.GetOriginalsFolder(projectFolder);
        var imported = files.Select(path => new TrackInfo
        {
            FilePath = Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 1), Path.GetFileName(path)),
            FileName = Path.GetFileName(path)
        }).ToList();
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = projectFolder, TotalDiscs = 1,
            ImportedDiscs = [new Mp3DiscProjectManifestDisc
            {
                DiscNumber = 1, Status = Mp3DiscImportStatus.Completed,
                SourceFiles = imported.Select((track, index) =>
                    SourceFile(acceptedIds[index].SourceId, originals, track.FilePath)).ToList()
            }]
        };
        _service.ApplyManifestDiscOrder(imported, projectFolder, manifest);
        var restored = state.Apply(projectFolder, imported, TrackOrderProjectType.Mp3Disc,
            TrackOrderConfirmedContextFactory.CreateMp3DiscContexts(imported));

        Assert.Equal(new[] { "003.mp3", "001.mp3", "002.mp3" }, restored.Select(track => track.FileName));
        Assert.Equal(new int?[] { 3, 1, 2 }, restored.Select(track => track.AutomaticOrderPosition));
        Assert.True(restored[1].IsExcluded);
        Assert.Equal("User chapter", restored[1].ExcludedChapterTitle);
    }

    [Fact]
    public void Disc_refresh_with_new_sources_restores_manual_order_exclusion_and_chapter_by_manifest_identity()
    {
        using var folder = new TemporaryFolder();
        var originals = ProjectFolderLayout.GetOriginalsFolder(folder.Path);
        var first = Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1), "001.mp3");
        var second = Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 2), "001.mp3");
        var third = Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 3), "001.mp3");
        Directory.CreateDirectory(originals);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var saved = new[]
        {
            new TrackInfo { FilePath = second, SourceId = ids[1], IsExcluded = true, ExcludedChapterTitle = "User chapter", AutomaticOrderPosition = 2 },
            new TrackInfo { FilePath = first, SourceId = ids[0], AutomaticOrderPosition = 1 }
        };
        var state = new TrackListStateService();
        state.Save(folder.Path, saved);
        var paths = new[] { first, second, third };
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = folder.Path, TotalDiscs = 3,
            ImportedDiscs = paths.Select((path, index) => new Mp3DiscProjectManifestDisc
            {
                DiscNumber = index + 1, Status = Mp3DiscImportStatus.Completed,
                SourceFiles = [SourceFile(ids[index], originals, path)]
            }).ToList()
        };
        var scanned = paths.Select(path => new TrackInfo { FilePath = path, FileName = "001.mp3" }).ToList();
        _service.ApplyManifestDiscOrder(scanned, folder.Path, manifest);
        var restored = state.Apply(folder.Path, scanned, TrackOrderProjectType.Mp3Disc,
            TrackOrderConfirmedContextFactory.CreateMp3DiscContexts(scanned));
        Assert.Equal(new[] { ids[1], ids[0], ids[2] }, restored.Select(track => track.SourceId));
        Assert.True(restored[0].IsExcluded);
        Assert.Equal("User chapter", restored[0].ExcludedChapterTitle);
        Assert.False(restored[2].IsExcluded);
        Assert.Equal(new int?[] { 2, 1, 3 }, restored.Select(track => track.AutomaticOrderPosition));
        var reopened = state.Apply(folder.Path,
            paths.Select((path, index) => new TrackInfo { FilePath = path, SourceId = ids[index] }),
            TrackOrderProjectType.Mp3Disc);
        Assert.Equal(restored.Select(track => track.SourceId), reopened.Select(track => track.SourceId));
        Assert.True(reopened[0].IsExcluded);
    }

    [Fact]
    public void NormalizeProjectDiscNumber_UsesLogicalProjectDiscWithoutChangingSourceEvidence()
    {
        var track = new TrackInfo
        {
            DiscNumber = 2,
            SourceDiscNumber = 2
        };

        _service.NormalizeProjectDiscNumber([track], discNumber: 1);

        Assert.Equal(1, track.DiscNumber);
        Assert.Equal(2, track.SourceDiscNumber);
    }

    [Fact]
    public void ApplyPlannedSourceIds_AfterProjectDiscNormalization_AssignsStableSourceId()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        Directory.CreateDirectory(sourceFolder);
        var filePath = Path.Combine(sourceFolder, "113.mp3");
        File.WriteAllText(filePath, "audio");

        var planned = Mp3DiscSourceIdentityPlanner.Create(
            sourceFolder,
            "disc-signature",
            [filePath]);
        var track = new TrackInfo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            DiscNumber = 2,
            SourceDiscNumber = 2
        };

        _service.NormalizeProjectDiscNumber([track], discNumber: 1);
        var changed = _service.ApplyPlannedSourceIds(
            [track],
            sourceFolder,
            discNumber: 1,
            planned);

        Assert.Equal(1, changed);
        Assert.Equal(1, track.DiscNumber);
        Assert.Equal(2, track.SourceDiscNumber);
        Assert.Equal(Assert.Single(planned).SourceId, track.SourceId);
    }

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
        Assert.Equal(string.Empty, existing[1].DisplayFolder);
    }

    [Fact]
    public void CreatePreviewTracksFromKnownFiles_KeepsTechnicalFolderButHidesDisplayFolder()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        Directory.CreateDirectory(Path.Combine(sourceFolder, "CD 02"));
        var filePath = Path.Combine(sourceFolder, "CD 02", "001.mp3");

        var tracks = _service.CreatePreviewTracksFromKnownFiles(sourceFolder, [filePath]);

        var track = Assert.Single(tracks);
        Assert.Equal(filePath, track.FilePath);
        Assert.Equal("001.mp3", track.FileName);
        Assert.Equal("CD 02", track.RelativeFolder);
        Assert.Equal(string.Empty, track.DisplayFolder);
    }

    [Fact]
    public void CreatePreviewTracksFromKnownFiles_UsesDeterministicRelativePathOrder()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        Directory.CreateDirectory(sourceFolder);

        var tracks = _service.CreatePreviewTracksFromKnownFiles(
            sourceFolder,
            [
                Path.Combine(sourceFolder, "003.mp3"),
                Path.Combine(sourceFolder, "001.mp3"),
                Path.Combine(sourceFolder, "002.mp3")
            ]);

        Assert.Equal(new[] { "001.mp3", "002.mp3", "003.mp3" }, tracks.Select(track => track.FileName));
        Assert.Equal(new[] { 1, 2, 3 }, tracks.Select(track => track.Index));
    }

    [Fact]
    public void AppendMissingPreviewTracks_AllowsImmediateAutoOrderRebuildAcrossKnownDiscs()
    {
        using var folder = new TemporaryFolder();
        var sourceOne = Path.Combine(folder.Path, "disc-one");
        var sourceTwo = Path.Combine(folder.Path, "disc-two");
        var sourceThree = Path.Combine(folder.Path, "disc-three");
        Directory.CreateDirectory(sourceOne);
        Directory.CreateDirectory(sourceTwo);
        Directory.CreateDirectory(sourceThree);

        var tracks = new List<TrackInfo>();

        AppendDiscPreviewAndRebuild(tracks, sourceOne, 1, ["001.mp3", "002.mp3"]);
        Assert.Equal(new int?[] { 1, 2 }, tracks.Select(track => track.AutomaticOrderPosition).ToArray());

        AppendDiscPreviewAndRebuild(tracks, sourceTwo, 2, ["001.mp3", "002.mp3", "003.mp3"]);
        Assert.Equal(5, tracks.Count);
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5 }, tracks.Select(track => track.AutomaticOrderPosition).ToArray());

        AppendDiscPreviewAndRebuild(tracks, sourceThree, 3, ["001.mp3"]);

        Assert.Equal(6, tracks.Count);
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5, 6 }, tracks.Select(track => track.AutomaticOrderPosition).ToArray());
        Assert.Equal(new int?[] { 1, 1, 2, 2, 2, 3 }, tracks.Select(track => track.DiscNumber).ToArray());
        Assert.Equal(6, tracks.Select(track => track.AutomaticOrderPosition).Distinct().Count());
        Assert.All(tracks, track => Assert.Equal(string.Empty, track.DisplayFolder));
    }

    [Fact]
    public void AppendMissingPreviewTracks_KeepsGlobalAutoOrderWhenNextDiscPreviewHasNoTrackMetadata()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        var discTwoSource = Path.Combine(folder.Path, "drive-cd2");
        Directory.CreateDirectory(Path.Combine(projectFolder, "CD 01"));
        Directory.CreateDirectory(discTwoSource);

        var tracks = Enumerable.Range(1, 25)
            .Select(number => new TrackInfo
            {
                Index = number,
                DiscNumber = 1,
                TrackNumber = number,
                FilePath = Path.Combine(projectFolder, "CD 01", $"{number:000}.mp3"),
                FileName = $"{number:000}.mp3",
                RelativeFolder = "CD 01",
                HideFolderDisplay = true
            })
            .ToList();

        new TrackAutomaticOrderApplicationService().CalculateAndApply(
            tracks,
            TrackOrderProjectType.Mp3Disc,
            TrackOrderConfirmedContextFactory.CreateMp3DiscContexts(tracks));
        Assert.Equal(Enumerable.Range(1, 25).Select(number => (int?)number), tracks.Select(track => track.AutomaticOrderPosition));

        var previewFiles = Enumerable.Range(26, 19)
            .Select(number => Path.Combine(discTwoSource, $"{number:000}.mp3"))
            .ToArray();
        var previewTracks = _service.CreatePreviewTracksFromKnownFiles(discTwoSource, previewFiles);

        Assert.All(previewTracks, track => Assert.Null(track.TrackNumber));

        _service.AppendMissingPreviewTracks(
            tracks,
            previewTracks,
            discTwoSource,
            discNumber: 2,
            clearExistingTracks: false);

        new TrackAutomaticOrderApplicationService().CalculateAndApply(
            tracks,
            TrackOrderProjectType.Mp3Disc,
            TrackOrderConfirmedContextFactory.CreateMp3DiscContexts(tracks));

        Assert.Equal(44, tracks.Count);
        Assert.Equal(1, tracks.Single(track => track.FileName == "001.mp3").AutomaticOrderPosition);
        Assert.Equal(25, tracks.Single(track => track.FileName == "025.mp3").AutomaticOrderPosition);
        Assert.Equal(26, tracks.Single(track => track.FileName == "026.mp3").AutomaticOrderPosition);
        Assert.Equal(44, tracks.Single(track => track.FileName == "044.mp3").AutomaticOrderPosition);
    }

    [Fact]
    public void ApplyManifestDiscOrder_KeepsDiscImportSequenceAcrossConflictingFilenameEvidence()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        var originalsFolder = ProjectFolderLayout.GetOriginalsFolder(projectFolder);
        var discOneFolder = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 1);
        var discTwoFolder = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 2);
        Directory.CreateDirectory(discOneFolder);
        Directory.CreateDirectory(discTwoFolder);

        var discOneSourceOne = Guid.NewGuid();
        var discOneSourceTwo = Guid.NewGuid();
        var discTwoSourceOne = Guid.NewGuid();
        var discTwoSourceTwo = Guid.NewGuid();
        var discOneFileOne = Path.Combine(discOneFolder, "0101John Grisham Bestechung.mp3");
        var discOneFileTwo = Path.Combine(discOneFolder, "0102John Grisham Bestechung.mp3");
        var discTwoFileOne = Path.Combine(discTwoFolder, "026 - Rafik Schami Die Wüste.mp3");
        var discTwoFileTwo = Path.Combine(discTwoFolder, "027 - Rafik Schami Die Wüste.mp3");

        var tracks = new List<TrackInfo>
        {
            new() { FilePath = discTwoFileOne, FileName = Path.GetFileName(discTwoFileOne), DiscNumber = 2, TrackNumber = 26 },
            new() { FilePath = discTwoFileTwo, FileName = Path.GetFileName(discTwoFileTwo), DiscNumber = 2, TrackNumber = 27 },
            new() { FilePath = discOneFileOne, FileName = Path.GetFileName(discOneFileOne), DiscNumber = 1, TrackNumber = null },
            new() { FilePath = discOneFileTwo, FileName = Path.GetFileName(discOneFileTwo), DiscNumber = 1, TrackNumber = null }
        };
        new TrackAutomaticOrderApplicationService().CalculateAndApply(
            tracks,
            TrackOrderProjectType.LocalFolder);

        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = projectFolder,
            TotalDiscs = 2,
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = 2,
                    Status = Mp3DiscImportStatus.Completed,
                    SourceFiles =
                    [
                        SourceFile(discTwoSourceOne, originalsFolder, discTwoFileOne),
                        SourceFile(discTwoSourceTwo, originalsFolder, discTwoFileTwo)
                    ]
                },
                new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = Mp3DiscImportStatus.Completed,
                    SourceFiles =
                    [
                        SourceFile(discOneSourceOne, originalsFolder, discOneFileOne),
                        SourceFile(discOneSourceTwo, originalsFolder, discOneFileTwo)
                    ]
                }
            ]
        };

        _service.ApplyManifestDiscOrder(tracks, projectFolder, manifest);

        Assert.Equal(
            new[]
            {
                "0101John Grisham Bestechung.mp3",
                "0102John Grisham Bestechung.mp3",
                "026 - Rafik Schami Die Wüste.mp3",
                "027 - Rafik Schami Die Wüste.mp3"
            },
            tracks.Select(track => track.FileName));
        Assert.Equal(new int?[] { 1, 1, 2, 2 }, tracks.Select(track => track.DiscNumber));
        Assert.Equal(new[] { 1, 2, 3, 4 }, tracks.Select(track => track.Index));
        Assert.Equal(new int?[] { 1, 2, 3, 4 }, tracks.Select(track => track.AutomaticOrderPosition));
        Assert.Equal(
            new[] { discOneSourceOne, discOneSourceTwo, discTwoSourceOne, discTwoSourceTwo },
            tracks.Select(track => track.SourceId));
    }

    [Fact]
    public void ApplyManifestDiscOrder_RejectsScannedTracksWithoutStableManifestIdentity()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "project");
        var discOneFolder = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 1);
        Directory.CreateDirectory(discOneFolder);

        var knownFile = Path.Combine(discOneFolder, "001.mp3");
        var unknownFile = Path.Combine(discOneFolder, "002.mp3");
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = projectFolder,
            TotalDiscs = 1,
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = Mp3DiscImportStatus.Completed,
                    SourceFiles =
                    [
                        SourceFile(
                            Guid.NewGuid(),
                            ProjectFolderLayout.GetOriginalsFolder(projectFolder),
                            knownFile)
                    ]
                }
            ]
        };
        var tracks = new List<TrackInfo>
        {
            new() { FilePath = knownFile, FileName = "001.mp3" },
            new() { FilePath = unknownFile, FileName = "002.mp3" }
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _service.ApplyManifestDiscOrder(tracks, projectFolder, manifest));

        Assert.Contains("1/2", exception.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { knownFile, unknownFile }, tracks.Select(track => track.FilePath));
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
        Assert.Equal(string.Empty, tracks[0].DisplayFolder);
        Assert.Equal(string.Empty, tracks[1].DisplayFolder);
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

    private static Mp3DiscProjectManifestSourceFile SourceFile(
        Guid sourceId,
        string originalsFolder,
        string fullPath) => new()
    {
        SourceId = sourceId,
        RelativePath = Path.GetRelativePath(originalsFolder, fullPath),
        SourceFileName = Path.GetFileName(fullPath)
    };

    private void AppendDiscPreviewAndRebuild(
        IList<TrackInfo> tracks,
        string sourceFolder,
        int discNumber,
        IEnumerable<string> fileNames)
    {
        var previewTracks = fileNames.Select((fileName, index) => new TrackInfo
        {
            Index = index + 1,
            TrackNumber = index + 1,
            FilePath = Path.Combine(sourceFolder, fileName),
            FileName = fileName,
            RelativeFolder = Path.GetFileName(sourceFolder)
        });

        _service.AppendMissingPreviewTracks(
            tracks,
            previewTracks,
            sourceFolder,
            discNumber,
            clearExistingTracks: false);

        new TrackAutomaticOrderApplicationService().CalculateAndApply(
            tracks.ToList(),
            TrackOrderProjectType.Mp3Disc,
            TrackOrderConfirmedContextFactory.CreateMp3DiscContexts(tracks));
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
