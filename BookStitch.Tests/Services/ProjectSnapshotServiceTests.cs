using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectSnapshotServiceTests
{
    private readonly ProjectSnapshotService _service = new(
        new Mp3DiscProjectService(),
        new AudioDiscProjectService(),
        new WorkManifestService());

    [Fact]
    public void UpdateMp3DiscProjectSnapshot_UpdatesProjectStateWithoutRewritingLegacyMetadata()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectFolder = @"D:\Work\Book",
            Title = "Legacy Titel",
            Author = "Legacy Autor",
            Album = "Legacy Album",
            Narrator = "Legacy Sprecher",
            Genre = "Legacy Genre",
            CoverSourcePath = @"D:\Old\cover.png",
            ProcessedCoverPath = @"D:\Old\cover.jpg",
            OutputFileName = "old.m4a"
        };

        _service.UpdateMp3DiscProjectSnapshot(manifest, CreateSnapshot());

        Assert.Equal("Legacy Titel", manifest.Title);
        Assert.Equal("Legacy Autor", manifest.Author);
        Assert.Equal("Legacy Album", manifest.Album);
        Assert.Equal("Legacy Sprecher", manifest.Narrator);
        Assert.Equal("Legacy Genre", manifest.Genre);
        Assert.Equal(@"D:\Cover\source.png", manifest.CoverSourcePath);
        Assert.Equal(@"D:\Cover\processed.jpg", manifest.ProcessedCoverPath);
        Assert.Equal("Autor - Titel.m4b", manifest.OutputFileName);
        Assert.Equal("AAC Mono 64 kbps", manifest.ExportPreset);
        Assert.Equal("2", manifest.ParallelJobs);
        Assert.Equal(".m4b", manifest.OutputExtension);
        Assert.Equal(@"D:\Output", manifest.OutputFolder);
        Assert.Equal("{Autor} - {Titel}", manifest.FileNameTemplate);
    }

    [Fact]
    public void UpdateExportManifestSnapshot_UpdatesExportAndCoverStateWithoutRewritingLegacyMetadata()
    {
        var manifest = new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Legacy Titel",
                Author = "Legacy Autor",
                Album = "Legacy Album",
                Narrator = "Legacy Sprecher",
                Genre = "Legacy Genre"
            }
        };

        _service.UpdateExportManifestSnapshot(
            manifest,
            CreateSnapshot(),
            finalOutputFolder: @"D:\Final\Autor\Titel",
            finalOutputFileName: "Autor - Titel.m4b");

        Assert.Equal("Legacy Titel", manifest.Metadata.Title);
        Assert.Equal("Legacy Autor", manifest.Metadata.Author);
        Assert.Equal("Legacy Album", manifest.Metadata.Album);
        Assert.Equal("Legacy Sprecher", manifest.Metadata.Narrator);
        Assert.Equal("Legacy Genre", manifest.Metadata.Genre);
        Assert.Equal(@"D:\Cover\source.png", manifest.Metadata.CoverSourcePath);
        Assert.Equal(@"D:\Cover\processed.jpg", manifest.Metadata.ProcessedCoverPath);
        Assert.Equal("AAC Mono 64 kbps", manifest.SelectedPreset);
        Assert.Equal("AAC Mono 64 kbps", manifest.Export.SelectedPreset);
        Assert.Equal("2", manifest.Export.ParallelJobs);
        Assert.Equal(".m4b", manifest.Export.OutputExtension);
        Assert.Equal(@"D:\Final\Autor\Titel", manifest.Export.OutputFolder);
        Assert.Equal("Autor - Titel.m4b", manifest.Export.OutputFileName);
    }

    [Fact]
    public void SaveExportSnapshot_PersistsProjectMetadataStoreWithoutRewritingLegacyManifestMetadata()
    {
        using var folder = new TemporaryFolder();
        var manifestPath = ProjectFolderLayout.GetWorkManifestPath(folder.Path);
        var manifest = new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = folder.Path,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Legacy Titel",
                Author = "Legacy Autor"
            }
        };

        _service.SaveExportSnapshot(
            manifestPath,
            manifest,
            CreateSnapshot(),
            finalOutputFolder: @"D:\Final\Autor\Titel",
            finalOutputFileName: "Autor - Titel.m4b");
        var stored = new ProjectMetadataStore().LoadOrCreate(folder.Path);

        Assert.Equal("Titel", stored.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.User, stored.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(["Autor"], stored.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal("Legacy Titel", manifest.Metadata.Title);
        Assert.Equal("Legacy Autor", manifest.Metadata.Author);
        Assert.Equal("", manifest.Metadata.Album);
        Assert.Equal("", manifest.Metadata.Narrator);
        Assert.Equal("", manifest.Metadata.Genre);
    }

    [Fact]
    public void SaveExportSnapshot_PreservesStoredAlbumWithoutRewritingLegacyManifestMetadata()
    {
        using var folder = new TemporaryFolder();
        var manifestPath = ProjectFolderLayout.GetWorkManifestPath(folder.Path);
        var manifest = new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = folder.Path,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Legacy Titel",
                Album = "Legacy Album"
            }
        };

        _service.SaveExportSnapshot(
            manifestPath,
            manifest,
            CreateSnapshot(album: "Gespeichertes Album"),
            finalOutputFolder: @"D:\Final\Autor\Titel",
            finalOutputFileName: "Autor - Titel.m4b");
        var stored = new ProjectMetadataStore().LoadOrCreate(folder.Path);

        Assert.Equal("Gespeichertes Album", stored.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Equal(MetadataOrigin.User, stored.GetValue<string>(ProjectMetadataField.Album).Origin);
        Assert.Equal("Legacy Titel", manifest.Metadata.Title);
        Assert.Equal("Legacy Album", manifest.Metadata.Album);
    }

    [Fact]
    public void SaveExportSnapshot_PreservesStructuredListTailsWithoutProjectingPrimaryValuesToLegacyManifest()
    {
        using var folder = new TemporaryFolder();
        var store = new ProjectMetadataStore();
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Titel", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Album", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["Autor", "B", "C"], MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators, ["Sprecher", "N2"], MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres, ["Genre", "G2"], MetadataOrigin.User);
        store.Save(folder.Path, metadata);
        var manifestPath = ProjectFolderLayout.GetWorkManifestPath(folder.Path);
        var manifest = new ExportWorkManifest { ProjectType = ProjectManifestTypes.FolderProject };

        _service.SaveExportSnapshot(
            manifestPath,
            manifest,
            CreateSnapshot(),
            @"D:\Final",
            "book.m4b");

        var reloaded = store.LoadOrCreate(folder.Path);
        Assert.Equal(["Autor", "B", "C"], reloaded.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(["Sprecher", "N2"], reloaded.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
        Assert.Equal(["Genre", "G2"], reloaded.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.Equal("", manifest.Metadata.Author);
        Assert.Equal("", manifest.Metadata.Narrator);
        Assert.Equal("", manifest.Metadata.Genre);
    }

    [Fact]
    public void SaveExportSnapshot_WhenProjectMetadataStoreIsCorrupt_DoesNotOverwriteStoreOrLegacyManifest()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var metadataPath = ProjectFolderLayout.GetProjectMetadataPath(folder.Path);
        const string corruptJson = "{ invalid";
        File.WriteAllText(metadataPath, corruptJson);
        var manifestPath = ProjectFolderLayout.GetWorkManifestPath(folder.Path);
        var manifest = new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = folder.Path,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Legacy Titel",
                Author = "Legacy Autor"
            }
        };

        Assert.ThrowsAny<Exception>(() => _service.SaveExportSnapshot(
            manifestPath,
            manifest,
            CreateSnapshot(),
            finalOutputFolder: @"D:\Final\Autor\Titel",
            finalOutputFileName: "Autor - Titel.m4b"));

        Assert.Equal(corruptJson, File.ReadAllText(metadataPath));
        Assert.Equal("Legacy Titel", manifest.Metadata.Title);
        Assert.Equal("Legacy Autor", manifest.Metadata.Author);
        Assert.False(File.Exists(manifestPath));
    }

    [Fact]
    public void UpdateAudioDiscProjectSnapshot_DoesNotRewriteLegacyMetadataOrChapterTitles()
    {
        var manifest = new AudioDiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectFolder = @"D:\Work\Audio",
            Title = "Alt",
            Author = "Alt",
            Album = "Alt",
            Narrator = "Alt",
            Genre = "Alt",
            CoverSourcePath = @"D:\Old\cover.png",
            ProcessedCoverPath = @"D:\Old\cover.jpg",
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack { GlobalIndex = 1, ChapterTitle = "001 Alt" },
                        new AudioDiscProjectManifestTrack { GlobalIndex = 2, ChapterTitle = "002 Alt" }
                    ]
                }
            ]
        };

        var changed = _service.UpdateAudioDiscProjectSnapshot(manifest, CreateSnapshot());

        Assert.True(changed);
        Assert.Equal("Alt", manifest.Title);
        Assert.Equal("Alt", manifest.Author);
        Assert.Equal("Alt", manifest.Album);
        Assert.Equal("Alt", manifest.Narrator);
        Assert.Equal("Alt", manifest.Genre);
        Assert.Equal(@"D:\Cover\source.png", manifest.CoverSourcePath);
        Assert.Equal(@"D:\Cover\processed.jpg", manifest.ProcessedCoverPath);
        Assert.Equal("001 Alt", manifest.Discs[0].Tracks[0].ChapterTitle);
        Assert.Equal("002 Alt", manifest.Discs[0].Tracks[1].ChapterTitle);
    }

    [Fact]
    public void UpdateAudioDiscProjectSnapshot_DoesNotMirrorAlbumIntoLegacyManifest()
    {
        var manifest = new AudioDiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectFolder = @"D:\Work\Audio",
            Title = "Alt",
            Album = "Alt"
        };

        var changed = _service.UpdateAudioDiscProjectSnapshot(
            manifest,
            CreateSnapshot(album: "Gespeichertes Album"));

        Assert.True(changed);
        Assert.Equal("Alt", manifest.Title);
        Assert.Equal("Alt", manifest.Album);
    }

    [Theory]
    [InlineData(ProjectManifestTypes.Mp3DiscProject, ProjectPipelineStateNames.ReviewBeforeMerge)]
    [InlineData(ProjectManifestTypes.Mp3DiscProject, ProjectPipelineStateNames.Completed)]
    [InlineData(ProjectManifestTypes.AudioCdProject, ProjectPipelineStateNames.ReviewBeforeMerge)]
    [InlineData(ProjectManifestTypes.AudioCdProject, ProjectPipelineStateNames.Completed)]
    [InlineData(ProjectManifestTypes.FolderProject, ProjectPipelineStateNames.Completed)]
    public void Editable_checkpoint_persists_settings_order_and_exclusion_without_active_import(
        string projectType, string phase)
    {
        using var folder = new TemporaryFolder();
        if (projectType == ProjectManifestTypes.Mp3DiscProject)
            new Mp3DiscProjectService().Save(new Mp3DiscProjectManifest
                { ProjectFolder = folder.Path, PipelineState = phase, TotalDiscs = 3 });
        if (projectType == ProjectManifestTypes.AudioCdProject)
            new AudioDiscProjectService().Save(new AudioDiscProjectManifest
                { ProjectFolder = folder.Path, PipelineState = phase, TotalDiscs = 3 });
        var workService = new WorkManifestService();
        var exportPath = ProjectFolderLayout.GetExportManifestPath(folder.Path);
        var acquiredSourcesFolder = ProjectFolderLayout.GetOriginalsFolder(folder.Path);
        var export = workService.LoadOrCreate(exportPath, projectType, folder.Path, acquiredSourcesFolder, "AAC Stereo 192 kbps");
        export.State.Status = phase;
        workService.Save(exportPath, export);
        var tracks = new[]
        {
            new TrackInfo { Index = 1, SourceId = Guid.NewGuid(), FileName = "second.mp3", IsExcluded = true },
            new TrackInfo { Index = 2, SourceId = Guid.NewGuid(), FileName = "first.mp3" }
        };
        var snapshot = CreateSnapshot() with { OutputFolder = folder.Path };
        _service.SaveEditableProjectSnapshot(folder.Path, projectType, snapshot, tracks,
            Path.Combine(folder.Path, snapshot.OutputFileNamePreview));
        Assert.Equal(acquiredSourcesFolder, workService.ReadOnly(exportPath)!.SourceFolder);
        var savedExport = workService.LoadOrCreate(exportPath, projectType, folder.Path, acquiredSourcesFolder, snapshot.SelectedExportPreset);
        Assert.Equal(phase, savedExport.State.Status);
        Assert.Equal("AAC Mono 64 kbps", savedExport.Export.SelectedPreset);
        Assert.Equal(snapshot.ProcessedCoverPath, savedExport.Metadata.ProcessedCoverPath);
        var applied = new TrackListStateService().Apply(folder.Path, tracks.Reverse());
        Assert.Equal(tracks.Select(t => t.SourceId), applied.Select(t => t.SourceId));
        Assert.True(applied[0].IsExcluded);
        if (projectType == ProjectManifestTypes.Mp3DiscProject)
        {
            var saved = new Mp3DiscProjectService().TryLoad(folder.Path)!;
            Assert.Equal(phase, saved.PipelineState);
            Assert.Equal(3, saved.TotalDiscs);
            Assert.Equal(snapshot.SelectedExportPreset, saved.ExportPreset);
            Assert.Equal(snapshot.FileNameTemplate, saved.FileNameTemplate);
        }
        if (projectType == ProjectManifestTypes.AudioCdProject)
            Assert.Equal(snapshot.SelectedExportPreset, new AudioDiscProjectService().TryLoad(folder.Path)!.ExportPreset);
    }

    [Fact]
    public void Editable_checkpoint_rejects_missing_project_instead_of_silently_discarding_changes()
    {
        using var folder = new TemporaryFolder();
        Assert.Throws<InvalidDataException>(() => _service.SaveEditableProjectSnapshot(
            folder.Path, ProjectManifestTypes.Mp3DiscProject, CreateSnapshot(), [], Path.Combine(folder.Path, "book.m4b")));
        Assert.Throws<DirectoryNotFoundException>(() => _service.SaveEditableProjectSnapshot(
            Path.Combine(folder.Path, "missing"), ProjectManifestTypes.FolderProject, CreateSnapshot(), [], "book.m4b"));
    }

    private static ProjectSnapshotUiState CreateSnapshot(
        string album = "Album") =>
        new(
            SelectedExportPreset: "AAC Mono 64 kbps",
            ParallelJobs: "2",
            OutputExtension: ".m4b",
            OutputFolder: @"D:\Output",
            FileNameTemplate: "{Autor} - {Titel}",
            BookTitle: "Titel",
            Author: "Autor",
            Album: album,
            Narrator: "Sprecher",
            Genre: "Genre",
            CoverSourcePath: @"D:\Cover\source.png",
            ProcessedCoverPath: @"D:\Cover\processed.jpg",
            OutputFileNamePreview: "Autor - Titel.m4b");
}
