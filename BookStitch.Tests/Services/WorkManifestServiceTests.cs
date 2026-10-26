using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public class WorkManifestServiceTests
{
    [Fact]
    public void Diagnostics_measure_actual_reads_and_serialized_saves_without_allowing_stale_writes()
    {
        using var folder = new TemporaryFolder();
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        var service = new WorkManifestService(trace);
        var path = Path.Combine(folder.Path, "manifest.json");
        var owner = service.LoadOrCreate(path, folder.Path, folder.Path, "AAC Mono 64 kbps");
        service.Save(path, owner);
        var firstBytes = File.ReadAllBytes(path);
        var stale = service.LoadOrCreate(path, folder.Path, folder.Path, owner.SelectedPreset);
        service.ReadOnly(path);
        service.Save(path, owner);
        Assert.Throws<IOException>(() => service.Save(path, stale));
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.tmp"));
        var aggregates = trace.CompleteSession(PerformanceOutcome.Success)!.Operations
            .ToDictionary(item => item.Name, item => item.Metrics.ToDictionary(metric => metric.Name, metric => metric.Value));
        Assert.Equal(2, aggregates["manifest.load"]["Count"]);
        Assert.Equal(2, aggregates["manifest.read"]["Count"]);
        Assert.Equal(firstBytes.Length * 2, aggregates["manifest.read"]["Bytes"]);
        Assert.Equal(3, aggregates["manifest.save"]["Count"]); // Includes the rejected attempt.
        Assert.Equal(2, aggregates["manifest.save.atomic-store"]["CommittedWrites"]);
        Assert.Equal(firstBytes.Length + File.ReadAllBytes(path).Length, aggregates["manifest.save.atomic-store"]["SerializedBytes"]);
        Assert.True(aggregates["manifest.save.prepare"]["SumDuration"] >= 0);
        Assert.True(aggregates["manifest.save.atomic-store"]["MaxDuration"] >= aggregates["manifest.save.atomic-store"]["MeanDuration"]);
    }

    private readonly WorkManifestService _service = new();

    [Fact]
    public async Task Migration_read_modify_write_waits_for_the_current_manifest_commit()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var path = ProjectFolderLayout.GetWorkManifestPath(folder.Path);
        var owner = _service.LoadOrCreate(path, folder.Path, folder.Path, "AAC Mono 64 kbps");
        owner.ProjectWorkFolder = "legacy-project-folder";
        _service.Save(path, owner);
        var normalize = typeof(ProjectFolderMigrationService).GetMethod("NormalizeExportManifest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        using var started = new ManualResetEventSlim();
        Task migration;
        lock (AtomicJsonStore.ForPath(path))
        {
            migration = Task.Run(() => { started.Set(); normalize.Invoke(null, [folder.Path]); });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Thread.Sleep(30);
            Assert.False(migration.IsCompleted);
            owner.Metadata.Title = "newer checkpoint";
            _service.Save(path, owner);
        }
        await migration.WaitAsync(TimeSpan.FromSeconds(5));
        var saved = _service.ReadOnly(path);
        Assert.Equal("newer checkpoint", saved.Metadata.Title);
        Assert.Equal(folder.Path, saved.ProjectWorkFolder);
        Assert.Throws<IOException>(() => _service.Save(path, owner));
    }

    [Fact]
    public void Windows_case_alias_uses_the_same_owner_revision_and_rejects_a_stale_copy()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "project.json");
        var owner = _service.LoadOrCreate(path, folder.Path, folder.Path, "AAC Mono 64 kbps");
        _service.Save(path, owner);
        var stale = _service.LoadOrCreate(path.ToUpperInvariant(), folder.Path, folder.Path, owner.SelectedPreset);
        owner.Metadata.Title = "newer";
        _service.Save(path.ToUpperInvariant(), owner);
        Assert.Throws<IOException>(() => _service.Save(path, stale));
        Assert.Equal("newer", _service.ReadOnly(path).Metadata.Title);
    }

    [Fact]
    public async Task Independent_concurrent_writers_cannot_overwrite_a_newer_manifest()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "project.json");
        var initial = _service.LoadOrCreate(path, folder.Path, folder.Path, "AAC Mono 64 kbps");
        _service.Save(path, initial);
        var first = _service.LoadOrCreate(path, folder.Path, folder.Path, initial.SelectedPreset);
        var stale = new WorkManifestService().LoadOrCreate(path, folder.Path, folder.Path, initial.SelectedPreset);
        first.Metadata.Title = "newer";
        stale.Metadata.Title = "older";
        await Task.Run(() => _service.Save(path, first));
        var bytes = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<IOException>(() => Task.Run(() => new WorkManifestService().Save(path, stale)));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal("newer", _service.ReadOnly(path).Metadata.Title);
    }

    [Fact]
    public async Task Shared_owner_parallel_saves_are_serialized_and_valid()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "project.json");
        var owner = _service.LoadOrCreate(path, folder.Path, folder.Path, "AAC Mono 64 kbps");
        await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() => new WorkManifestService().Save(path, owner))));
        Assert.Equal(owner.ProjectId, _service.ReadOnly(path).ProjectId);
        Assert.Empty(Directory.EnumerateFiles(folder.Path, "*.tmp"));
    }

    [Fact]
    public void Transactional_restore_invalidates_an_old_manifest_owner()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "project.json");
        var owner = _service.LoadOrCreate(path, folder.Path, folder.Path, "AAC Mono 64 kbps");
        _service.Save(path, owner);
        var original = File.ReadAllBytes(path);
        new AtomicJsonStore().RestoreBytes(path, original);
        Assert.Throws<IOException>(() => _service.Save(path, owner));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void Mp3ImportCachePreparation_KeepsImportManifestIntactAndReloadsSeparateExportState()
    {
        using var folder = new TemporaryFolder();
        var projectService = new Mp3DiscProjectService();
        var import = projectService.LoadOrCreate(
            folder.Path, folder.Path, 3, "AAC Stereo 192 kbps", "Auto", ".m4a",
            System.IO.Path.Combine(folder.Path, "output"), "{Autor} - {Titel}");
        projectService.Save(import);
        var importPath = ProjectFolderLayout.ResolveMp3DiscManifestPath(folder.Path);
        var importBytes = System.IO.File.ReadAllBytes(importPath);
        var exportPath = ProjectFolderLayout.ResolveExportManifestPath(folder.Path);

        var export = _service.LoadOrCreate(
            exportPath, ProjectManifestTypes.Mp3DiscProject, folder.Path,
            ProjectFolderLayout.GetOriginalsFolder(folder.Path), import.ExportPreset);
        Assert.Empty(export.Tracks);
        export.State.Status = ProjectManifestStatuses.AcquiringSources;
        _service.Save(exportPath, export);

        var reloaded = _service.LoadOrCreate(
            ProjectFolderLayout.ResolveExportManifestPath(folder.Path),
            ProjectManifestTypes.Mp3DiscProject, folder.Path,
            ProjectFolderLayout.GetOriginalsFolder(folder.Path), import.ExportPreset);

        Assert.Equal(export.ProjectId, reloaded.ProjectId);
        Assert.Equal(ProjectManifestStatuses.AcquiringSources, reloaded.State.Status);
        Assert.Equal(importBytes, System.IO.File.ReadAllBytes(importPath));
        Assert.Equal(3, projectService.TryLoad(folder.Path)!.TotalDiscs);
    }

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
    public void LoadOrCreate_WhenManifestIsCorrupt_RejectsWithoutOverwriting()
    {
        using var folder = new TemporaryFolder();

        var manifestPath = System.IO.Path.Combine(folder.Path, "project.json");
        System.IO.File.WriteAllText(manifestPath, "{ kaputt");

        Assert.Throws<System.IO.IOException>(() => _service.LoadOrCreate(
            manifestPath,
            folder.Path,
            @"C:\Source",
            "AAC Stereo 192 kbps"));

        Assert.Equal("{ kaputt", System.IO.File.ReadAllText(manifestPath));
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
    public void UpdateCoverMetadata_LeavesLegacyScalarMetadataUntouched()
    {
        var manifest = new ExportWorkManifest
        {
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Legacy Titel",
                Author = "Legacy Autor",
                Album = "Legacy Album",
                Narrator = "Legacy Sprecher",
                Genre = "Legacy Genre"
            }
        };

        _service.UpdateCoverMetadata(
            manifest,
            @"C:\Cover.jpg",
            @"C:\Work\cover.jpg");

        Assert.Equal("Legacy Titel", manifest.Metadata.Title);
        Assert.Equal("Legacy Autor", manifest.Metadata.Author);
        Assert.Equal("Legacy Album", manifest.Metadata.Album);
        Assert.Equal("Legacy Sprecher", manifest.Metadata.Narrator);
        Assert.Equal("Legacy Genre", manifest.Metadata.Genre);
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
    public void UpdateTrack_PreservesTrackSourceIdInManifestEntry()
    {
        using var folder = new TemporaryFolder();

        var source = WriteFile(folder, "Track 01.mp3", "source");
        var converted = WriteFile(folder, "Track 01.m4a", "converted");
        var sourceId = Guid.NewGuid();
        var manifest = new ExportWorkManifest();

        _service.UpdateTrack(
            manifest,
            index: 0,
            new TrackInfo
            {
                SourceId = sourceId,
                FileName = "Track 01.mp3",
                ProcessingAction = "Konvertieren"
            },
            source,
            converted,
            ExportPreset.Parse("AAC Stereo 192 kbps"));

        Assert.Equal(sourceId, Assert.Single(manifest.Tracks).SourceId);
    }


    [Fact]
    public void UpdateTrack_ReplacesExistingEntryWithoutMirroringChapterPresentation()
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
        Assert.Equal(string.Empty, entry.ChapterTitle);
        Assert.Equal(ProcessingActionIds.Convert, entry.Action);
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
        Assert.Equal(ExportWorkManifestVersions.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal("legacy-id", manifest.ProjectId);
        Assert.NotNull(manifest.Export);
        Assert.NotNull(manifest.Metadata);
        Assert.NotNull(manifest.State);
        Assert.NotNull(manifest.Resume);
        Assert.Empty(manifest.Discs);
        Assert.Empty(manifest.Tracks);
        Assert.Empty(manifest.History);

        var migratedJson = System.IO.File.ReadAllText(manifestPath);
        Assert.Contains("\"schemaVersion\": 1", migratedJson);
    }

    [Fact]
    public void LoadOrCreate_CurrentSchemaManifest_LoadsNormally()
    {
        using var folder = new TemporaryFolder();
        var manifestPath = System.IO.Path.Combine(folder.Path, "project.json");
        var json = $$"""
        {
          "schemaVersion": 1,
          "FormatVersion": "{{ExportWorkManifestVersions.Current}}",
          "ProjectType": "FolderProject",
          "ProjectId": "current-id",
          "State": {
            "Status": "ReviewBeforeMerge",
            "ManualMergeReviewCompletedPresets": []
          },
          "Tracks": []
        }
        """;
        System.IO.File.WriteAllText(manifestPath, json);

        var manifest = _service.LoadOrCreate(
            manifestPath,
            folder.Path,
            @"C:\Source",
            "AAC Stereo 192 kbps");

        Assert.Equal(ExportWorkManifestVersions.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal("current-id", manifest.ProjectId);
    }

    [Fact]
    public void LoadOrCreate_CurrentSchemaTrackWithoutSourceId_BackfillsAndPersistsOnce()
    {
        using var folder = new TemporaryFolder();
        var manifestPath = System.IO.Path.Combine(folder.Path, "project.json");
        var sourcePath = System.IO.Path.Combine(folder.Path, "source.mp3");
        var convertedPath = System.IO.Path.Combine(folder.Path, "converted.m4a");
        var json = $$"""
        {
          "schemaVersion": 1,
          "FormatVersion": "{{ExportWorkManifestVersions.Current}}",
          "ProjectType": "FolderProject",
          "ProjectId": "current-id",
          "State": {
            "Status": "ReviewBeforeMerge",
            "ManualMergeReviewCompletedPresets": []
          },
          "Tracks": [
            {
              "TrackIndex": 1,
              "SourcePath": "{{JsonEncodedText.Encode(sourcePath)}}",
              "SourceFileName": "source.mp3",
              "ConvertedPath": "{{JsonEncodedText.Encode(convertedPath)}}",
              "Preset": "AAC Stereo 192 kbps",
              "Action": "Konvertieren",
              "Status": "Pending"
            }
          ]
        }
        """;
        System.IO.File.WriteAllText(manifestPath, json);

        var first = _service.LoadOrCreate(
            manifestPath,
            folder.Path,
            folder.Path,
            "AAC Stereo 192 kbps");
        var sourceId = Assert.Single(first.Tracks).SourceId;
        var persistedJson = System.IO.File.ReadAllText(manifestPath);

        Assert.Equal(ProcessingActionIds.Convert, Assert.Single(first.Tracks).Action);
        Assert.Contains("\"Action\": \"Convert\"", persistedJson, StringComparison.Ordinal);

        var second = _service.LoadOrCreate(
            manifestPath,
            folder.Path,
            folder.Path,
            "AAC Stereo 192 kbps");

        Assert.NotEqual(Guid.Empty, sourceId);
        Assert.Equal(sourceId, Assert.Single(second.Tracks).SourceId);
        Assert.Contains(sourceId.ToString(), persistedJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(persistedJson, System.IO.File.ReadAllText(manifestPath));
    }

    [Fact]
    public void LoadOrCreate_TrackWithoutSourceId_UsesSourceCopyIdentityEvidence()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = folder.Path;
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var sourceFile = WriteFile(folder, System.IO.Path.Combine("external", "source.mp3"), "audio");
        var targetFile = System.IO.Path.Combine(ProjectFolderLayout.GetOriginalsFolder(projectFolder), "source.mp3");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetFile)!);
        System.IO.File.WriteAllText(targetFile, "audio");
        var lastWriteUtc = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
        System.IO.File.SetLastWriteTimeUtc(sourceFile, lastWriteUtc);
        var identity = new SourceCopyIdentityStore().RecordCopiedFile(projectFolder, sourceFile, targetFile);
        var manifestPath = System.IO.Path.Combine(projectFolder, "project.json");

        WriteExportManifestWithoutSourceId(
            manifestPath,
            targetFile,
            "source.mp3",
            SourceSizeBytes: new System.IO.FileInfo(sourceFile).Length,
            SourceLastWriteUtcTicks: lastWriteUtc.Ticks);

        var manifest = _service.LoadOrCreate(
            manifestPath,
            projectFolder,
            ProjectFolderLayout.GetOriginalsFolder(projectFolder),
            "AAC Stereo 192 kbps");

        Assert.Equal(identity.SourceId, Assert.Single(manifest.Tracks).SourceId);
    }

    [Fact]
    public void LoadOrCreate_TrackWithoutSourceId_UsesMp3DiscManifestEvidence()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = folder.Path;
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var sourceId = Guid.NewGuid();
        var sourcePath = System.IO.Path.Combine(ProjectFolderLayout.GetOriginalsFolder(projectFolder), "CD 01", "Track 01.mp3");
        var exportManifestPath = ProjectFolderLayout.GetExportManifestPath(projectFolder);
        var mp3Manifest = new Mp3DiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectFolder = projectFolder,
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    SourceFiles =
                    [
                        new Mp3DiscProjectManifestSourceFile
                        {
                            SourceId = sourceId,
                            RelativePath = System.IO.Path.Combine("CD 01", "Track 01.mp3"),
                            SourceFileName = "Track 01.mp3"
                        }
                    ]
                }
            ]
        };
        WriteJson(ProjectFolderLayout.GetWorkManifestPath(projectFolder), mp3Manifest);
        WriteExportManifestWithoutSourceId(exportManifestPath, sourcePath, "Track 01.mp3");

        var manifest = _service.LoadOrCreate(
            exportManifestPath,
            ProjectManifestTypes.Mp3DiscProject,
            projectFolder,
            ProjectFolderLayout.GetOriginalsFolder(projectFolder),
            "AAC Stereo 192 kbps");

        Assert.Equal(sourceId, Assert.Single(manifest.Tracks).SourceId);
    }

    [Fact]
    public void LoadOrCreate_TrackWithoutSourceId_UsesAudioDiscManifestEvidence()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = folder.Path;
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var sourceId = Guid.NewGuid();
        var relativePath = System.IO.Path.Combine(ProjectFolderLayout.OriginalsFolderName, "CD 01", "001.flac");
        var sourcePath = System.IO.Path.Combine(projectFolder, relativePath);
        var exportManifestPath = ProjectFolderLayout.GetWorkManifestPath(projectFolder);
        var audioManifest = new AudioDiscProjectManifest
        {
            ProjectFolder = projectFolder,
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack
                        {
                            SourceId = sourceId,
                            GlobalIndex = 1,
                            DiscNumber = 1,
                            TrackNumber = 1,
                            FileName = "001.flac",
                            RelativePath = relativePath
                        }
                    ]
                }
            ]
        };
        WriteJson(ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder), audioManifest);
        WriteExportManifestWithoutSourceId(exportManifestPath, sourcePath, "001.flac");

        var manifest = _service.LoadOrCreate(
            exportManifestPath,
            ProjectManifestTypes.AudioCdProject,
            projectFolder,
            ProjectFolderLayout.GetOriginalsFolder(projectFolder),
            "AAC Stereo 192 kbps");

        Assert.Equal(sourceId, Assert.Single(manifest.Tracks).SourceId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void LoadOrCreate_UnsupportedSchema_RejectsAndLeavesFileUnchanged(int schemaVersion)
    {
        using var folder = new TemporaryFolder();
        var manifestPath = System.IO.Path.Combine(folder.Path, "project.json");
        var json = $$"""
        {
          "schemaVersion": {{schemaVersion}},
          "FormatVersion": "{{ExportWorkManifestVersions.Current}}",
          "ProjectType": "FolderProject",
          "ProjectId": "future-id",
          "State": {
            "Status": "Preparing",
            "ManualMergeReviewCompletedPresets": []
          },
          "Tracks": []
        }
        """;
        System.IO.File.WriteAllText(manifestPath, json);

        Assert.Throws<PersistentJsonSchemaException>(() => _service.LoadOrCreate(
            manifestPath,
            folder.Path,
            @"C:\Source",
            "AAC Stereo 192 kbps"));
        Assert.Equal(json, System.IO.File.ReadAllText(manifestPath));
    }


    [Fact]
    public void ConversionAndExportCompletion_DoNotPersistMergePermission()
    {
        var manifest = new ExportWorkManifest();
        _service.MarkConversionCompleted(manifest);
        _service.MarkExportCompleted(manifest);
        Assert.Empty(manifest.State.ManualMergeReviewCompletedPresets);
    }

    private static void WriteExportManifestWithoutSourceId(
        string manifestPath,
        string sourcePath,
        string sourceFileName,
        long SourceSizeBytes = 0,
        long SourceLastWriteUtcTicks = 0)
    {
        var json = $$"""
        {
          "schemaVersion": 1,
          "FormatVersion": "{{ExportWorkManifestVersions.Current}}",
          "ProjectType": "FolderProject",
          "ProjectId": "legacy-id",
          "State": {
            "Status": "ReviewBeforeMerge",
            "ManualMergeReviewCompletedPresets": []
          },
          "Tracks": [
            {
              "TrackIndex": 1,
              "SourcePath": "{{JsonEncodedText.Encode(sourcePath)}}",
              "SourceFileName": "{{JsonEncodedText.Encode(sourceFileName)}}",
              "SourceSizeBytes": {{SourceSizeBytes}},
              "SourceLastWriteUtcTicks": {{SourceLastWriteUtcTicks}},
              "Preset": "AAC Stereo 192 kbps",
              "Action": "Konvertieren",
              "Status": "Pending"
            }
          ]
        }
        """;
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(manifestPath)!);
        System.IO.File.WriteAllText(manifestPath, json);
    }

    private static void WriteJson<T>(string path, T value)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(path, json);
    }
}
