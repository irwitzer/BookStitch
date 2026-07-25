using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public class WorkManifestServiceTests
{
    private readonly WorkManifestService _service = new();

    [Fact]
    public void LoadOrCreate_WhenManifestIsMissing_CreatesNewFolderProjectManifest()
    {
        using var folder = new TemporaryFolder();

        var manifestPath = System.IO.Path.Combine(folder.Path, "project.json");

        var manifest = _service.LoadOrCreate(
            manifestPath,
            folder.Path,
            @"C:\Source",
            "AAC Stereo 192 kbps");

        Assert.Equal(ExportWorkManifestVersions.Current, manifest.FormatVersion);
        Assert.Equal(ProjectManifestTypes.FolderProject, manifest.ProjectType);
        Assert.False(string.IsNullOrWhiteSpace(manifest.ProjectId));
        Assert.Equal(folder.Path, manifest.ProjectWorkFolder);
        Assert.Equal(@"C:\Source", manifest.SourceFolder);
        Assert.Equal("AAC Stereo 192 kbps", manifest.SelectedPreset);
        Assert.Equal("AAC Stereo 192 kbps", manifest.Export.SelectedPreset);
        Assert.Equal(ProjectManifestStatuses.Preparing, manifest.State.Status);
        Assert.False(manifest.Resume.CanResume);
        Assert.Empty(manifest.Tracks);
    }

    [Fact]
    public void LoadOrCreate_WithExplicitProjectType_CreatesMp3DiscManifest()
    {
        using var folder = new TemporaryFolder();

        var manifest = _service.LoadOrCreate(
            System.IO.Path.Combine(folder.Path, "project.json"),
            ProjectManifestTypes.Mp3DiscProject,
            folder.Path,
            @"D:\",
            "AAC Mono 64 kbps");

        Assert.Equal(ProjectManifestTypes.Mp3DiscProject, manifest.ProjectType);
    }

    [Fact]
    public void LoadOrCreate_WhenManifestIsCorrupt_CreatesNewManifest()
    {
        using var folder = new TemporaryFolder();

        var manifestPath = System.IO.Path.Combine(folder.Path, "project.json");
        System.IO.File.WriteAllText(manifestPath, "{ kaputt");

        var manifest = _service.LoadOrCreate(
            manifestPath,
            folder.Path,
            @"C:\Source",
            "AAC Stereo 192 kbps");

        Assert.Equal(folder.Path, manifest.ProjectWorkFolder);
        Assert.Equal(@"C:\Source", manifest.SourceFolder);
        Assert.Equal("AAC Stereo 192 kbps", manifest.SelectedPreset);
        Assert.Empty(manifest.Tracks);
    }

    [Fact]
    public void Save_WritesManifestFileAndReloadsIt()
    {
        using var folder = new TemporaryFolder();

        var manifestPath = System.IO.Path.Combine(folder.Path, "nested", "project.json");
        var manifest = new ExportWorkManifest
        {
            ProjectWorkFolder = folder.Path,
            SourceFolder = @"C:\Source",
            SelectedPreset = "AAC Stereo 192 kbps",
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = @"C:\Source\Track 01.mp3",
                    ConvertedPath = @"C:\Work\Track 01.m4a",
                    ConvertedSizeBytes = 123,
                    Status = ProjectManifestTrackStatuses.Converted
                }
            ]
        };

        _service.Save(manifestPath, manifest);

        Assert.True(System.IO.File.Exists(manifestPath));

        var loaded = _service.LoadOrCreate(
            manifestPath,
            folder.Path,
            @"C:\Source2",
            "AAC Mono 64 kbps");

        Assert.Equal(folder.Path, loaded.ProjectWorkFolder);
        Assert.Equal(@"C:\Source2", loaded.SourceFolder);
        Assert.Equal("AAC Mono 64 kbps", loaded.SelectedPreset);
        Assert.Single(loaded.Tracks);
    }

    [Fact]
    public void UpdateExportSettings_StoresExportSettings()
    {
        var manifest = new ExportWorkManifest();

        _service.UpdateExportSettings(
            manifest,
            "AAC Stereo 192 kbps",
            @"C:\Output",
            "Book.m4b",
            ".m4b",
            "Auto");

        Assert.Equal("AAC Stereo 192 kbps", manifest.SelectedPreset);
        Assert.Equal("AAC Stereo 192 kbps", manifest.Export.SelectedPreset);
        Assert.Equal(@"C:\Output", manifest.Export.OutputFolder);
        Assert.Equal("Book.m4b", manifest.Export.OutputFileName);
        Assert.Equal(".m4b", manifest.Export.OutputExtension);
        Assert.Equal("Auto", manifest.Export.ParallelJobs);
    }

    [Fact]
    public void UpdateBookMetadata_StoresMetadata()
    {
        var manifest = new ExportWorkManifest();

        _service.UpdateBookMetadata(
            manifest,
            "Titel",
            "Autor",
            "Album",
            "Sprecher",
            "Genre",
            @"C:\Cover.jpg",
            @"C:\Work\cover.jpg");

        Assert.Equal("Titel", manifest.Metadata.Title);
        Assert.Equal("Autor", manifest.Metadata.Author);
        Assert.Equal("Album", manifest.Metadata.Album);
        Assert.Equal("Sprecher", manifest.Metadata.Narrator);
        Assert.Equal("Genre", manifest.Metadata.Genre);
        Assert.Equal(@"C:\Cover.jpg", manifest.Metadata.CoverSourcePath);
        Assert.Equal(@"C:\Work\cover.jpg", manifest.Metadata.ProcessedCoverPath);
    }

    [Fact]
    public void MarkConversionPreparationStarted_WhenAlreadyRunning_DoesNotAddDuplicateHistory()
    {
        var manifest = new ExportWorkManifest();

        _service.MarkConversionPreparationStarted(manifest);
        _service.MarkConversionPreparationStarted(manifest);

        Assert.Equal(ProjectManifestStatuses.Converting, manifest.State.Status);
        Assert.Equal("AAC-Vorbereitung läuft.", manifest.Resume.Reason);
        Assert.Single(manifest.History, entry => entry.Event == "ConversionPreparationStarted");
        Assert.DoesNotContain(manifest.History, entry => entry.Event == "ConversionPreparationResumed");
    }

    [Fact]
    public void MarkConversionPreparationStarted_AfterCancellation_RecordsResumeInsteadOfSecondStart()
    {
        var manifest = new ExportWorkManifest();
        _service.MarkConversionPreparationStarted(manifest);
        _service.MarkExportCanceled(manifest, "Abbruch");

        _service.MarkConversionPreparationStarted(manifest);

        Assert.Equal(ProjectManifestStatuses.Converting, manifest.State.Status);
        Assert.True(manifest.Resume.CanResume);
        Assert.Equal("AAC-Vorbereitung wurde fortgesetzt.", manifest.Resume.Reason);
        Assert.Single(manifest.History, entry => entry.Event == "ConversionPreparationStarted");
        Assert.Single(manifest.History, entry => entry.Event == "ConversionPreparationResumed");
    }

    [Fact]
    public void MarkExportStateMethods_UpdateStateResumeAndHistory()
    {
        var manifest = new ExportWorkManifest();

        _service.MarkExportStarted(manifest);

        Assert.Equal(ProjectManifestStatuses.Converting, manifest.State.Status);
        Assert.True(manifest.Resume.CanResume);
        Assert.Contains(manifest.History, entry => entry.Event == "ExportStarted");

        _service.MarkExportCanceled(manifest, "Abbruch");

        Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, manifest.State.Status);
        Assert.True(manifest.Resume.CanResume);
        Assert.Equal("Abbruch", manifest.Resume.Reason);
        Assert.NotNull(manifest.State.CancelRequestedUtc);

        _service.MarkExportFailed(manifest, "Fehler");

        Assert.Equal(ProjectManifestStatuses.ReviewBeforeMerge, manifest.State.Status);
        Assert.Equal("Fehler", manifest.State.LastErrorSummary);
        Assert.NotNull(manifest.State.LastErrorUtc);

        _service.MarkExportCompleted(manifest);

        Assert.Equal(ProjectManifestStatuses.Completed, manifest.State.Status);
        Assert.False(manifest.Resume.CanResume);
        Assert.Equal("ExportCompleted", manifest.State.LastSuccessfulStep);
    }

    [Fact]
    public void PruneInvalidEntries_RemovesMissingEmptyAndBlankConvertedFiles()
    {
        using var folder = new TemporaryFolder();

        var validConverted = WriteFile(folder, "valid.m4a", "audio");
        var emptyConverted = folder.CreateFile("empty.m4a");
        var missingConverted = System.IO.Path.Combine(folder.Path, "missing.m4a");

        var manifest = new ExportWorkManifest
        {
            Tracks =
            [
                new ExportWorkManifestTrack { TrackIndex = 1, ConvertedPath = validConverted },
                new ExportWorkManifestTrack { TrackIndex = 2, ConvertedPath = emptyConverted },
                new ExportWorkManifestTrack { TrackIndex = 3, ConvertedPath = missingConverted },
                new ExportWorkManifestTrack { TrackIndex = 4, ConvertedPath = "" }
            ]
        };

        _service.PruneInvalidEntries(manifest);

        var remaining = Assert.Single(manifest.Tracks);
        Assert.Equal(1, remaining.TrackIndex);
    }

    [Fact]
    public void Save_SortsTracksAndDiscsForReadableProjectJson()
    {
        using var folder = new TemporaryFolder();

        var manifestPath = System.IO.Path.Combine(folder.Path, "project.json");
        var manifest = new ExportWorkManifest
        {
            Tracks =
            [
                new ExportWorkManifestTrack { TrackIndex = 3, ConvertedPath = "c", ConvertedSizeBytes = 1 },
                new ExportWorkManifestTrack { TrackIndex = 1, ConvertedPath = "a", ConvertedSizeBytes = 1 },
                new ExportWorkManifestTrack { TrackIndex = 2, ConvertedPath = "b", ConvertedSizeBytes = 1 }
            ],
            Discs =
            [
                new ExportWorkManifestDisc { DiscIndex = 2 },
                new ExportWorkManifestDisc { DiscIndex = 1 }
            ]
        };

        _service.Save(manifestPath, manifest);

        Assert.Equal([1, 2, 3], manifest.Tracks.Select(track => track.TrackIndex).ToArray());
        Assert.Equal([1, 2], manifest.Discs.Select(disc => disc.DiscIndex).ToArray());
    }

    [Fact]
    public void UpdateTrack_NormalizesSourceExtensionWithLeadingDotAndLowercase()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.MP3", "source");
        var converted = WriteFile(folder, "Track 01.m4a", "converted");
        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(
            manifest,
            index: 0,
            new TrackInfo
            {
                FileName = "Track 01.MP3",
                Codec = "MP3",
                Extension = "MP3",
                ProcessingAction = "Konvertieren"
            },
            source,
            converted,
            ExportPreset.Parse("AAC Stereo 192 kbps"));

        var entry = Assert.Single(manifest.Tracks);
        Assert.Equal(".mp3", entry.SourceExtension);
    }


    [Fact]
    public void UpdateTrack_ReplacesExistingEntryForSameSourcePresetAndAction()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var oldConverted = WriteFile(folder, "old_Track 01.m4a", "converted");
        var converted = WriteFile(folder, "Track 01.m4a", "converted");
        var preset = ExportPreset.Parse("AAC Stereo 192 kbps");
        var manifest = new ExportWorkManifest
        {
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = System.IO.Path.GetFullPath(source),
                    ConvertedPath = System.IO.Path.GetFullPath(oldConverted),
                    Action = "Konvertieren",
                    Preset = preset.DisplayName,
                    Status = ProjectManifestTrackStatuses.Converted
                }
            ]
        };

        _service.UpdateTrack(
            manifest,
            index: 0,
            new TrackInfo
            {
                FileName = "Track 01.mp3",
                Codec = "MP3",
                Extension = ".mp3",
                Duration = "00:01",
                ChapterTitle = "Kapitel 1",
                ProcessingAction = "Konvertieren"
            },
            source,
            converted,
            preset);

        var entry = Assert.Single(manifest.Tracks);
        Assert.Equal(1, entry.TrackIndex);
        Assert.Equal(System.IO.Path.GetFullPath(source), entry.SourcePath);
        Assert.Equal(System.IO.Path.GetFullPath(converted), entry.ConvertedPath);
        Assert.Equal("MP3", entry.SourceCodec);
        Assert.Equal(".mp3", entry.SourceExtension);
        Assert.Equal("00:01", entry.Duration);
        Assert.Equal("Kapitel 1", entry.ChapterTitle);
        Assert.Equal("Konvertieren", entry.Action);
        Assert.Equal("AAC Stereo 192 kbps", entry.Preset);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, entry.Status);
        Assert.Equal(1, manifest.State.LastCompletedTrackIndex);
        Assert.Equal("TrackConverted", manifest.State.LastSuccessfulStep);
        Assert.True(entry.SourceSizeBytes > 0);
        Assert.True(entry.ConvertedSizeBytes > 0);
    }

    [Fact]
    public void UpdateTrack_WhenTracksCompleteOutOfOrder_KeepsHighestCompletedTrackIndex()
    {
        using var folder = new TemporaryFolder();

        var service = new WorkManifestService();
        var manifestPath = Path.Combine(folder.Path, "project.json");
        var manifest = service.LoadOrCreate(
            manifestPath,
            folder.Path,
            folder.Path,
            "AAC Mono 64 kbps");
        var preset = ExportPreset.Parse("AAC Mono 64 kbps");

        for (var index = 0; index < 3; index++)
        {
            WriteFile(folder, $"Track {index + 1:00}.flac", $"source-{index}");
            WriteFile(folder, $"Track {index + 1:00}.m4a", $"converted-{index}");
        }

        service.UpdateTrack(
            manifest,
            2,
            new TrackInfo { FileName = "Track 03.flac", Extension = ".flac", Codec = "FLAC" },
            Path.Combine(folder.Path, "Track 03.flac"),
            Path.Combine(folder.Path, "Track 03.m4a"),
            preset);

        service.UpdateTrack(
            manifest,
            0,
            new TrackInfo { FileName = "Track 01.flac", Extension = ".flac", Codec = "FLAC" },
            Path.Combine(folder.Path, "Track 01.flac"),
            Path.Combine(folder.Path, "Track 01.m4a"),
            preset);

        Assert.Equal(3, manifest.State.LastCompletedTrackIndex);
    }

    [Fact]
    public void LoadOrCreate_NormalizesStaleLastCompletedTrackIndexFromConvertedTracks()
    {
        using var folder = new TemporaryFolder();

        var manifestPath = Path.Combine(folder.Path, "project.json");
        File.WriteAllText(manifestPath, """
        {
          "ProjectType": "AudioCdProject",
          "State": {
            "LastCompletedTrackIndex": 2
          },
          "Tracks": [
            { "TrackIndex": 1, "Status": "Converted" },
            { "TrackIndex": 4, "Status": "Converted" },
            { "TrackIndex": 5, "Status": "Pending" }
          ]
        }
        """);

        var service = new WorkManifestService();
        var manifest = service.LoadOrCreate(
            manifestPath,
            ProjectManifestTypes.AudioCdProject,
            folder.Path,
            folder.Path,
            "AAC Mono 64 kbps");

        Assert.Equal(4, manifest.State.LastCompletedTrackIndex);
    }

    [Fact]
    public void UpdateTrack_KeepsExistingEntryForSameSourceWhenPresetDiffers()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var converted64 = WriteFile(folder, "Track 01_64.m4a", "converted64");
        var converted96 = WriteFile(folder, "Track 01_96.m4a", "converted96");
        var preset64 = ExportPreset.Parse("AAC Mono 64 kbps");
        var preset96 = ExportPreset.Parse("AAC Mono 96 kbps");
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            Codec = "MP3",
            Extension = ".mp3",
            ProcessingAction = "Konvertieren"
        };

        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(manifest, 0, track, source, converted64, preset64);
        _service.UpdateTrack(manifest, 0, track, source, converted96, preset96);

        Assert.Equal(2, manifest.Tracks.Count);
        Assert.Contains(manifest.Tracks, entry =>
            entry.Preset == preset64.DisplayName &&
            System.IO.Path.GetFullPath(converted64) == entry.ConvertedPath);
        Assert.Contains(manifest.Tracks, entry =>
            entry.Preset == preset96.DisplayName &&
            System.IO.Path.GetFullPath(converted96) == entry.ConvertedPath);
    }

    [Fact]
    public void MarkTrackStarted_WithPresetIdentity_DoesNotOverwriteOtherPresetEntry()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var converted64 = WriteFile(folder, "Track 01_64.m4a", "converted64");
        var converted96 = System.IO.Path.Combine(folder.Path, "Track 01_96.m4a");
        var preset64 = ExportPreset.Parse("AAC Mono 64 kbps");
        var preset96 = ExportPreset.Parse("AAC Mono 96 kbps");
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            Codec = "MP3",
            Extension = ".mp3",
            ProcessingAction = "Konvertieren"
        };

        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(manifest, 0, track, source, converted64, preset64);
        _service.MarkTrackStarted(
            manifest,
            0,
            track,
            source,
            converted96,
            preset96,
            ProjectManifestTrackStatuses.Converting);

        Assert.Equal(2, manifest.Tracks.Count);
        Assert.Contains(manifest.Tracks, entry =>
            entry.Preset == preset64.DisplayName &&
            entry.Status == ProjectManifestTrackStatuses.Converted);
        Assert.Contains(manifest.Tracks, entry =>
            entry.Preset == preset96.DisplayName &&
            entry.Status == ProjectManifestTrackStatuses.Converting);
    }

    [Fact]
    public void MarkTrackStarted_UpdatesExistingTrackAndProjectState()
    {
        var manifest = new ExportWorkManifest
        {
            Tracks =
            [
                new ExportWorkManifestTrack { TrackIndex = 2 }
            ]
        };

        _service.MarkTrackStarted(manifest, index: 1, ProjectManifestTrackStatuses.Converting);

        var entry = Assert.Single(manifest.Tracks);
        Assert.Equal(ProjectManifestTrackStatuses.Converting, entry.Status);
        Assert.NotNull(entry.StartedUtc);
        Assert.Equal(2, manifest.State.LastStartedTrackIndex);
    }

    [Fact]
    public void MarkTrackFailed_UpdatesExistingTrackAndErrorState()
    {
        var manifest = new ExportWorkManifest
        {
            Tracks =
            [
                new ExportWorkManifestTrack { TrackIndex = 2 }
            ]
        };

        _service.MarkTrackFailed(manifest, index: 1, "Lesefehler");

        var entry = Assert.Single(manifest.Tracks);
        Assert.Equal(ProjectManifestTrackStatuses.Failed, entry.Status);
        Assert.Equal("Lesefehler", entry.LastError);
        Assert.Equal("Lesefehler", manifest.State.LastErrorSummary);
        Assert.NotNull(manifest.State.LastErrorUtc);
    }

    [Fact]
    public void CountReusableConvertedTracks_CountsOnlyValidatedEntriesForPreset()
    {
        using var folder = new TemporaryFolder();

        var source1 = WriteFile(folder, "Track 01.mp3", "source-1");
        var converted1 = WriteFile(folder, "Track 01.m4a", "converted-1");
        var source2 = WriteFile(folder, "Track 02.mp3", "source-2");
        var converted2 = WriteFile(folder, "Track 02.m4a", "converted-2");
        var preset = ExportPreset.Parse("AAC Stereo 192 kbps");
        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(manifest, 0, new TrackInfo
        {
            FileName = "Track 01.mp3",
            ProcessingAction = "Konvertieren"
        }, source1, converted1, preset);
        _service.UpdateTrack(manifest, 1, new TrackInfo
        {
            FileName = "Track 02.mp3",
            ProcessingAction = "Konvertieren"
        }, source2, converted2, preset);

        manifest.Tracks[1].ConvertedSizeBytes++;

        Assert.Equal(1, _service.CountReusableConvertedTracks(manifest, preset));
        Assert.Equal(0, _service.CountReusableConvertedTracks(
            manifest,
            ExportPreset.Parse("AAC Mono 64 kbps")));
    }

    [Fact]
    public void CountReusableConvertedTracks_RejectsChangedSourceFile()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var converted = WriteFile(folder, "Track 01.m4a", "converted");
        var preset = ExportPreset.Parse("AAC Stereo 192 kbps");
        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(manifest, 0, new TrackInfo
        {
            FileName = "Track 01.mp3",
            ProcessingAction = "Konvertieren"
        }, source, converted, preset);

        File.AppendAllText(source, "changed");

        Assert.Equal(0, _service.CountReusableConvertedTracks(manifest, preset));
    }

    [Fact]
    public void CanReuseConvertedTrack_ReturnsTrueForMatchingConvertedManifestEntry()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var converted = WriteFile(folder, "Track 01.m4a", "converted");
        var preset = ExportPreset.Parse("AAC Stereo 192 kbps");
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            Codec = "MP3",
            Extension = ".mp3",
            ProcessingAction = "Konvertieren"
        };

        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(manifest, 0, track, source, converted, preset);

        Assert.True(_service.CanReuseConvertedTrack(manifest, 0, track, source, converted, preset));
    }

    [Fact]
    public void CanReuseConvertedTrack_ReturnsFalseWhenStatusIsNotConverted()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var converted = WriteFile(folder, "Track 01.m4a", "converted");
        var preset = ExportPreset.Parse("AAC Stereo 192 kbps");
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            Codec = "MP3",
            Extension = ".mp3",
            ProcessingAction = "Konvertieren"
        };

        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(manifest, 0, track, source, converted, preset);
        manifest.Tracks[0].Status = ProjectManifestTrackStatuses.Failed;

        Assert.False(_service.CanReuseConvertedTrack(manifest, 0, track, source, converted, preset));
    }

    [Fact]
    public void CanReuseConvertedTrack_ReturnsFalseWhenPresetDoesNotMatch()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var converted = WriteFile(folder, "Track 01.m4a", "converted");
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            Codec = "MP3",
            Extension = ".mp3",
            ProcessingAction = "Konvertieren"
        };

        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(manifest, 0, track, source, converted, ExportPreset.Parse("AAC Stereo 192 kbps"));

        Assert.False(_service.CanReuseConvertedTrack(manifest, 0, track, source, converted, ExportPreset.Parse("AAC Mono 64 kbps")));
    }

    [Fact]
    public void CanReuseConvertedTrack_ReturnsFalseWhenConvertedFileIsMissing()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var converted = WriteFile(folder, "Track 01.m4a", "converted");
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            Codec = "MP3",
            Extension = ".mp3",
            ProcessingAction = "Konvertieren"
        };

        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(manifest, 0, track, source, converted, ExportPreset.Parse("AAC Stereo 192 kbps"));

        System.IO.File.Delete(converted);

        Assert.False(_service.CanReuseConvertedTrack(manifest, 0, track, source, converted, ExportPreset.Parse("AAC Stereo 192 kbps")));
    }

    [Fact]
    public void CanReuseConvertedTrack_IgnoresChangedTrackIndexForSameStableConvertedPath()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var converted = WriteFile(folder, "stable_Track 01.m4a", "converted");
        var preset = ExportPreset.Parse("AAC Stereo 192 kbps");
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            Codec = "MP3",
            Extension = ".mp3",
            ProcessingAction = "Konvertieren"
        };

        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(manifest, 0, track, source, converted, preset);

        Assert.True(_service.CanReuseConvertedTrack(manifest, 76, track, source, converted, preset));
    }

    [Fact]
    public void UpdateTrack_RemovesOldEntryForSameSourceAfterTrackWasReordered()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var oldConverted = WriteFile(folder, "old_Track 01.m4a", "converted");
        var newConverted = WriteFile(folder, "stable_Track 01.m4a", "converted");
        var preset = ExportPreset.Parse("AAC Stereo 192 kbps");
        var track = new TrackInfo
        {
            FileName = "Track 01.mp3",
            Codec = "MP3",
            Extension = ".mp3",
            ProcessingAction = "Konvertieren"
        };

        var manifest = new ExportWorkManifest
        {
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = System.IO.Path.GetFullPath(source),
                    ConvertedPath = System.IO.Path.GetFullPath(oldConverted),
                    Preset = preset.DisplayName,
                    Action = "Konvertieren",
                    Status = ProjectManifestTrackStatuses.Converted
                }
            ]
        };

        _service.UpdateTrack(manifest, 76, track, source, newConverted, preset);

        var entry = Assert.Single(manifest.Tracks);
        Assert.Equal(77, entry.TrackIndex);
        Assert.Equal(System.IO.Path.GetFullPath(source), entry.SourcePath);
        Assert.Equal(System.IO.Path.GetFullPath(newConverted), entry.ConvertedPath);
    }

    [Fact]
    public void AddHistory_KeepsOnlyLatestEntries()
    {
        var manifest = new ExportWorkManifest();

        for (var i = 0; i < 250; i++)
            _service.AddHistory(manifest, "Event" + i, "Message" + i);

        Assert.Equal(200, manifest.History.Count);
        Assert.Equal("Event50", manifest.History[0].Event);
        Assert.Equal("Event249", manifest.History[^1].Event);
    }

    private static string WriteFile(TemporaryFolder folder, string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(folder.Path, relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
            System.IO.Directory.CreateDirectory(directory);

        System.IO.File.WriteAllText(fullPath, content);
        return fullPath;
    }
    [Fact]
    public void LoadOrCreate_LegacyManifestWithoutFormatVersion_NormalizesNestedObjects()
    {
        using var folder = new TemporaryFolder();
        var manifestPath = System.IO.Path.Combine(folder.Path, "project.json");
        var legacyJson = """
        {
          "ProjectType": "FolderProject",
          "ProjectId": "legacy-id",
          "Export": null,
          "Metadata": null,
          "State": null,
          "Resume": null,
          "Discs": null,
          "Tracks": null,
          "History": null,
          "UnknownFutureField": true
        }
        """;
        System.IO.File.WriteAllText(manifestPath, legacyJson);

        var manifest = _service.LoadOrCreate(
            manifestPath,
            folder.Path,
            @"C:\Source",
            "AAC Stereo 192 kbps");

        Assert.Equal(ExportWorkManifestVersions.Current, manifest.FormatVersion);
        Assert.Equal("legacy-id", manifest.ProjectId);
        Assert.NotNull(manifest.Export);
        Assert.NotNull(manifest.Metadata);
        Assert.NotNull(manifest.State);
        Assert.NotNull(manifest.Resume);
        Assert.Empty(manifest.Discs);
        Assert.Empty(manifest.Tracks);
        Assert.Empty(manifest.History);
    }


    [Fact]
    public void ManualMergeReview_IsRememberedPerPreset()
    {
        var manifest = new ExportWorkManifest();

        _service.MarkManualMergeReviewCompleted(manifest, "AAC Stereo 128 kbps");

        Assert.True(_service.HasCompletedManualMergeReview(manifest, "AAC Stereo 128 kbps"));
        Assert.False(_service.HasCompletedManualMergeReview(manifest, "AAC Mono 64 kbps"));
    }

    [Fact]
    public void MarkConversionCompleted_RemembersReviewedPresetWithoutDuplicates()
    {
        var manifest = new ExportWorkManifest();

        _service.MarkConversionCompleted(manifest, "AAC Stereo 96 kbps");
        _service.MarkConversionCompleted(manifest, "AAC Stereo 96 kbps");

        Assert.Single(manifest.State.ManualMergeReviewCompletedPresets);
        Assert.Equal("AAC Stereo 96 kbps", manifest.State.ManualMergeReviewCompletedPresets[0]);
    }
}
