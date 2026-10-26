using System.IO;

using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscLiveConversionManifestSessionTests
{
    [Theory]
    [InlineData("Failed")]
    [InlineData("Canceled")]
    [InlineData("SessionCanceled")]
    public void Start_is_memory_only_but_failure_and_cancel_boundaries_persist_non_reusable_state(string boundary)
    {
        using var folder = new TemporaryFolder();
        var trace = new PerformanceTraceService(true);
        trace.TryStartSession(new());
        var store = new WorkManifestService(trace);
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var session = new AudioDiscLiveConversionManifestSession(store, CreateAudioManifest(folder.Path), preset);
        var source = Path.Combine(folder.Path, "track.flac");
        var target = Path.Combine(folder.Path, "track.m4a");
        File.WriteAllText(source, "original");
        File.WriteAllText(target, "unfinished audio");
        var preparation = new AudioDiscLiveConversionPreparation(new TrackInfo
        {
            Index = 0, SourceId = Guid.NewGuid(), FilePath = source, FileName = "track.flac",
            ProcessingAction = ProcessingActionIds.Convert
        }, source, target);
        var before = File.ReadAllBytes(session.ManifestPath);
        session.MarkTrackStarted(preparation);
        Assert.Equal(before, File.ReadAllBytes(session.ManifestPath));
        if (boundary == "Failed") session.MarkTrackFailed(preparation, "conversion failed");
        else if (boundary == "Canceled") session.MarkTrackCanceled(preparation);
        else session.MarkSessionCanceled("pause boundary");
        var entry = Assert.Single(store.ReadOnly(session.ManifestPath).Tracks);
        Assert.Equal(boundary == "SessionCanceled" ? ProjectManifestTrackStatuses.Converting : boundary, entry.Status);
        Assert.False(WorkManifestService.IsReusableConvertedManifestEntry(entry, preset.DisplayName));
        var saves = trace.CompleteSession(PerformanceOutcome.Success)!.Operations.Single(operation => operation.Name == "manifest.save");
        Assert.Equal(2, saves.Metrics.Single(metric => metric.Name == "Count").Value);
    }

    [Fact]
    public void Constructor_CreatesProjectManifestBeforeFirstLiveConversion()
    {
        using var folder = new TemporaryFolder();
        var audioManifest = CreateAudioManifest(folder.Path);
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var workManifestService = new WorkManifestService();

        var session = new AudioDiscLiveConversionManifestSession(
            workManifestService,
            audioManifest,
            preset);

        Assert.Equal(ProjectFolderLayout.GetExportManifestPath(folder.Path), session.ManifestPath);
        Assert.True(File.Exists(session.ManifestPath));

        var manifest = workManifestService.LoadOrCreate(
            session.ManifestPath,
            ProjectManifestTypes.AudioCdProject,
            folder.Path,
            ProjectFolderLayout.GetOriginalsFolder(folder.Path),
            preset.DisplayName);

        Assert.Equal(ProjectManifestTypes.AudioCdProject, manifest.ProjectType);
        Assert.Equal(ProjectManifestStatuses.AcquiringSources, manifest.State.Status);
        Assert.True(manifest.Resume.CanResume);
        Assert.Equal(preset.DisplayName, manifest.SelectedPreset);
        Assert.Equal("", manifest.Metadata.Title);
        Assert.Equal("", manifest.Metadata.Author);
        Assert.Equal("Live Author - Live Title.m4a", manifest.Export.OutputFileName);
    }

    [Fact]
    public void Constructor_WhenProjectMetadataStoreExists_UsesStoreInsteadOfAudioDiscManifestMetadata()
    {
        using var folder = new TemporaryFolder();
        var audioManifest = CreateAudioManifest(folder.Path);
        audioManifest.Title = "Legacy Title";
        audioManifest.Author = "Legacy Author";
        audioManifest.Album = "Legacy Album";
        audioManifest.Narrator = "Legacy Narrator";
        audioManifest.Genre = "Legacy Genre";
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Store Title", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["Store Author"], MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Store Album", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators, ["Store Narrator"], MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres, ["Store Genre"], MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder.Path, metadata);
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var workManifestService = new WorkManifestService();

        var session = new AudioDiscLiveConversionManifestSession(
            workManifestService,
            audioManifest,
            preset);
        var manifest = Load(workManifestService, session.ManifestPath, folder.Path, preset);

        Assert.Equal("", manifest.Metadata.Title);
        Assert.Equal("", manifest.Metadata.Author);
        Assert.Equal("", manifest.Metadata.Album);
        Assert.Equal("", manifest.Metadata.Narrator);
        Assert.Equal("", manifest.Metadata.Genre);
        Assert.Equal("Store Author - Store Title.m4a", manifest.Export.OutputFileName);
    }

    [Fact]
    public void Constructor_WhenProjectMetadataStoreIsMissing_MigratesAudioDiscManifestOnce()
    {
        using var folder = new TemporaryFolder();
        var audioManifest = CreateAudioManifest(folder.Path);
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var workManifestService = new WorkManifestService();

        _ = new AudioDiscLiveConversionManifestSession(
            workManifestService,
            audioManifest,
            preset);
        var metadataPath = ProjectFolderLayout.GetProjectMetadataPath(folder.Path);
        var firstJson = File.ReadAllText(metadataPath);
        _ = new AudioDiscLiveConversionManifestSession(
            workManifestService,
            audioManifest,
            preset);
        var stored = new ProjectMetadataStore().LoadOrCreate(folder.Path);

        Assert.Equal("Live Title", stored.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.LegacyMigrated, stored.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(firstJson, File.ReadAllText(metadataPath));
    }

    [Fact]
    public void Constructor_WhenCanceledManifestExists_RecordsResumeWithoutDuplicateStart()
    {
        using var folder = new TemporaryFolder();
        var audioManifest = CreateAudioManifest(folder.Path);
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var workManifestService = new WorkManifestService();
        var manifestPath = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        var existing = workManifestService.LoadOrCreate(
            manifestPath,
            ProjectManifestTypes.AudioCdProject,
            folder.Path,
            ProjectFolderLayout.GetOriginalsFolder(folder.Path),
            preset.DisplayName);
        workManifestService.MarkConversionPreparationStarted(existing);
        workManifestService.MarkExportCanceled(existing, "Benutzerabbruch während des Rippings.");
        workManifestService.Save(manifestPath, existing);

        _ = new AudioDiscLiveConversionManifestSession(
            workManifestService,
            audioManifest,
            preset);

        var resumed = Load(workManifestService, manifestPath, folder.Path, preset);
        Assert.Equal(ProjectManifestStatuses.AcquiringSources, resumed.State.Status);
        Assert.Equal("AAC-Vorbereitung wurde fortgesetzt.", resumed.Resume.Reason);
        Assert.Single(resumed.History, entry => entry.Event == "ConversionPreparationStarted");
        Assert.Single(resumed.History, entry => entry.Event == "ConversionPreparationResumed");
    }

    [Fact]
    public void TrackTransitions_ArePersistedAtomicallyDuringLiveConversion()
    {
        using var folder = new TemporaryFolder();
        var audioManifest = CreateAudioManifest(folder.Path);
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var workManifestService = new WorkManifestService();
        var session = new AudioDiscLiveConversionManifestSession(
            workManifestService,
            audioManifest,
            preset);

        var sourcePath = Path.Combine(folder.Path, "ripped", "001.flac");
        var convertedPath = Path.Combine(folder.Path, "converted", preset.GetFolderName(), "001.m4a");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
        File.WriteAllBytes(sourcePath, [1, 2, 3]);

        var preparation = new AudioDiscLiveConversionPreparation(
            new TrackInfo
            {
                Index = 0,
                FilePath = sourcePath,
                FileName = "001.flac",
                Extension = "flac",
                Codec = "FLAC",
                Duration = "00:01:00",
                DurationTicks = TimeSpan.FromMinutes(1).Ticks,
                ProcessingAction = "Konvertieren"
            },
            sourcePath,
            convertedPath);

        var beforeStart = File.ReadAllBytes(session.ManifestPath);
        session.MarkTrackStarted(preparation);
        var started = Load(workManifestService, session.ManifestPath, folder.Path, preset);
        Assert.Equal(beforeStart, File.ReadAllBytes(session.ManifestPath));
        Assert.Empty(started.Tracks);

        File.WriteAllBytes(convertedPath, [4, 5, 6, 7]);
        session.MarkTrackCompleted(preparation);
        var completed = Load(workManifestService, session.ManifestPath, folder.Path, preset);
        Assert.Single(completed.Tracks);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, completed.Tracks[0].Status);
        Assert.Equal(4, completed.Tracks[0].ConvertedSizeBytes);
        Assert.NotNull(completed.Tracks[0].CompletedUtc);
    }

    [Fact]
    public void MarkSessionCanceled_PersistsProjectLevelResumeState()
    {
        using var folder = new TemporaryFolder();
        var audioManifest = CreateAudioManifest(folder.Path);
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var workManifestService = new WorkManifestService();
        var session = new AudioDiscLiveConversionManifestSession(
            workManifestService,
            audioManifest,
            preset);

        session.MarkSessionCanceled("Benutzerabbruch während des Rippings.");

        var manifest = Load(workManifestService, session.ManifestPath, folder.Path, preset);
        Assert.Equal(ProjectManifestStatuses.AcquiringSources, manifest.State.Status);
        Assert.True(manifest.Resume.CanResume);
        Assert.Equal("Benutzerabbruch während des Rippings.", manifest.Resume.Reason);
    }

    [Fact]
    public void MarkTrackCanceled_PersistsResumableCanceledState()
    {
        using var folder = new TemporaryFolder();
        var audioManifest = CreateAudioManifest(folder.Path);
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var workManifestService = new WorkManifestService();
        var session = new AudioDiscLiveConversionManifestSession(
            workManifestService,
            audioManifest,
            preset);

        var sourcePath = Path.Combine(folder.Path, "ripped", "001.flac");
        var convertedPath = Path.Combine(folder.Path, "converted", preset.GetFolderName(), "001.m4a");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllBytes(sourcePath, [1]);

        var preparation = new AudioDiscLiveConversionPreparation(
            new TrackInfo
            {
                Index = 0,
                FilePath = sourcePath,
                FileName = "001.flac",
                Extension = "flac",
                Codec = "FLAC",
                ProcessingAction = "Konvertieren"
            },
            sourcePath,
            convertedPath);

        session.MarkTrackStarted(preparation);
        session.MarkTrackCanceled(preparation);

        var manifest = Load(workManifestService, session.ManifestPath, folder.Path, preset);
        Assert.Equal(ProjectManifestTrackStatuses.Canceled, manifest.Tracks[0].Status);
        Assert.Equal(ProjectManifestStatuses.AcquiringSources, manifest.State.Status);
        Assert.True(manifest.Resume.CanResume);
        Assert.NotNull(manifest.State.CancelRequestedUtc);
    }


    [Fact]
    public void ReconcileTrack_ReusesCompleteConvertedFileAndNormalizesManifest()
    {
        using var folder = new TemporaryFolder();
        var audioManifest = CreateAudioManifest(folder.Path);
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var workManifestService = new WorkManifestService();
        var session = new AudioDiscLiveConversionManifestSession(
            workManifestService,
            audioManifest,
            preset);

        var sourcePath = Path.Combine(folder.Path, "ripped", "001.flac");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllBytes(sourcePath, [1, 2, 3]);

        var track = new TrackInfo
        {
            Index = 0,
            FilePath = sourcePath,
            FileName = "001.flac",
            Extension = "flac",
            Codec = "FLAC",
            Duration = "00:01:00",
            DurationTicks = TimeSpan.FromMinutes(1).Ticks,
            ProcessingAction = "Konvertieren"
        };
        var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
            Path.Combine(folder.Path, "converted", preset.GetFolderName()),
            sourcePath,
            track);
        Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);
        File.WriteAllBytes(convertedPath, [4, 5, 6, 7]);

        session.MarkTrackCompleted(new AudioDiscLiveConversionPreparation(track, sourcePath, convertedPath));
        var reused = session.ReconcileTrack(new AudioDiscLiveConversionPreparation(
            track,
            sourcePath,
            convertedPath));

        Assert.True(reused);
        var manifest = Load(workManifestService, session.ManifestPath, folder.Path, preset);
        var entry = Assert.Single(manifest.Tracks);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, entry.Status);
        Assert.Equal(convertedPath, entry.ConvertedPath);
        Assert.Equal(4, entry.ConvertedSizeBytes);
    }

    private static AudioDiscProjectManifest CreateAudioManifest(string projectFolder)
    {
        return new AudioDiscProjectManifest
        {
            ProjectFolder = projectFolder,
            ExportPreset = "AAC Stereo 128 kbps",
            ParallelJobs = "4",
            OutputExtension = ".m4a",
            OutputFolder = Path.Combine(projectFolder, "output"),
            FileNameTemplate = "{Autor} - {Titel}",
            Title = "Live Title",
            Author = "Live Author",
            Narrator = "Live Narrator",
            Genre = "Hörbuch"
        };
    }

    private static ExportWorkManifest Load(
        WorkManifestService service,
        string manifestPath,
        string projectFolder,
        ExportPreset preset)
    {
        return service.ReadOnly(manifestPath);
    }
}
