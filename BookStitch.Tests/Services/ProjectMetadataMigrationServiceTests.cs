using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectMetadataMigrationServiceTests
{
    private readonly ProjectMetadataMigrationService _service = new();

    [Fact]
    public void EnsureMigrated_FolderProjectUsesExportMetadataAndSecondLoadIsIdempotent()
    {
        using var folder = new TemporaryFolder();
        var exportManifest = new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = folder.Path,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Export Titel",
                Author = "Export Autor",
                Album = "Export Album",
                Narrator = "Export Sprecher",
                Genre = "Export Genre"
            }
        };

        var first = _service.EnsureMigrated(folder.Path, null, exportManifest, null);
        var firstJson = File.ReadAllText(ProjectFolderLayout.GetProjectMetadataPath(folder.Path));
        var second = _service.EnsureMigrated(folder.Path, null, exportManifest, null);

        Assert.Equal("Export Titel", first.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.LegacyMigrated, first.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(["Export Autor"], first.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(firstJson, File.ReadAllText(ProjectFolderLayout.GetProjectMetadataPath(folder.Path)));
    }

    [Fact]
    public void EnsureMigrated_DiscProjectUsesDiscFieldsBeforeStaleExportFields()
    {
        using var folder = new TemporaryFolder();
        var mp3DiscManifest = new Mp3DiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectFolder = folder.Path,
            Title = "Disc Titel",
            Author = "Disc Autor",
            Album = "Disc Album",
            Narrator = "Disc Sprecher",
            Genre = "Disc Genre"
        };
        var exportManifest = new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectWorkFolder = folder.Path,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Export Titel",
                Author = "Export Autor",
                Album = "Export Album",
                Narrator = "Export Sprecher",
                Genre = "Export Genre"
            }
        };

        var metadata = _service.EnsureMigrated(folder.Path, mp3DiscManifest, exportManifest, null);
        var projection = ProjectMetadataMigrationService.CreateProjection(metadata);

        Assert.Equal("Disc Titel", projection.Title);
        Assert.Equal("Disc Autor", projection.Author);
        Assert.Equal("Disc Album", projection.Album);
        Assert.Equal("Disc Sprecher", projection.Narrator);
        Assert.Equal("Disc Genre", projection.Genre);
    }

    [Fact]
    public void EnsureMigrated_ExistingProjectMetadataStoreWinsOverLegacyManifests()
    {
        using var folder = new TemporaryFolder();
        var store = new ProjectMetadataStore();
        var existing = new ProjectMetadata();
        existing.SetValue(ProjectMetadataField.Title, "Store Titel", MetadataOrigin.User);
        existing.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["Store Autor"], MetadataOrigin.User);
        store.Save(folder.Path, existing);
        var originalJson = File.ReadAllText(ProjectFolderLayout.GetProjectMetadataPath(folder.Path));
        var exportManifest = new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = folder.Path,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Legacy Titel",
                Author = "Legacy Autor"
            }
        };

        var metadata = _service.EnsureMigrated(folder.Path, null, exportManifest, null);
        var projection = ProjectMetadataMigrationService.CreateProjection(metadata);

        Assert.Equal("Store Titel", projection.Title);
        Assert.Equal("Store Autor", projection.Author);
        Assert.Equal(originalJson, File.ReadAllText(ProjectFolderLayout.GetProjectMetadataPath(folder.Path)));
    }

    [Fact]
    public void LoadAuthoritativeProjectionOrLegacy_WhenStoreIsMissing_ReadsAudioDiscLegacyValuesWithoutPersisting()
    {
        using var folder = new TemporaryFolder();
        var audioManifest = new AudioDiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectFolder = folder.Path,
            Title = "Legacy Titel",
            Author = "Legacy Autor",
            Album = "Legacy Album",
            Narrator = "Legacy Sprecher",
            Genre = "Legacy Genre"
        };

        var projection = _service.LoadAuthoritativeProjectionOrLegacy(
            folder.Path,
            audioDiscManifest: audioManifest);

        Assert.Equal("Legacy Titel", projection.Title);
        Assert.Equal("Legacy Autor", projection.Author);
        Assert.Equal("Legacy Album", projection.Album);
        Assert.Equal("Legacy Sprecher", projection.Narrator);
        Assert.Equal("Legacy Genre", projection.Genre);
        Assert.False(File.Exists(ProjectFolderLayout.GetProjectMetadataPath(folder.Path)));
    }

    [Fact]
    public void LoadAuthoritativeProjectionOrLegacy_WhenStoreExists_PrefersCanonicalMetadata()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Store Titel", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["Store Autor"], MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Store Album", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators, ["Store Sprecher"], MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres, ["Store Genre"], MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder.Path, metadata);
        var audioManifest = new AudioDiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectFolder = folder.Path,
            Title = "Legacy Titel",
            Author = "Legacy Autor",
            Album = "Legacy Album",
            Narrator = "Legacy Sprecher",
            Genre = "Legacy Genre"
        };

        var projection = _service.LoadAuthoritativeProjectionOrLegacy(
            folder.Path,
            audioDiscManifest: audioManifest);

        Assert.Equal("Store Titel", projection.Title);
        Assert.Equal("Store Autor", projection.Author);
        Assert.Equal("Store Album", projection.Album);
        Assert.Equal("Store Sprecher", projection.Narrator);
        Assert.Equal("Store Genre", projection.Genre);
    }

    [Fact]
    public void SaveCurrentProjectMetadata_WhenValuesAreUnchanged_PreservesOriginRevisionAndJson()
    {
        using var folder = new TemporaryFolder();
        var migrated = new ProjectMetadata();
        migrated.SetValue(ProjectMetadataField.Title, "Titel", MetadataOrigin.LegacyMigrated);
        migrated.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["Autor"], MetadataOrigin.LegacyMigrated);
        new ProjectMetadataStore().Save(folder.Path, migrated);
        var originalJson = File.ReadAllText(ProjectFolderLayout.GetProjectMetadataPath(folder.Path));

        var projection = _service.SaveCurrentProjectMetadata(
            folder.Path,
            title: "Titel",
            author: "Autor",
            album: "",
            narrator: "",
            genre: "");
        var loaded = new ProjectMetadataStore().LoadOrCreate(folder.Path);

        Assert.Equal("Titel", projection.Title);
        Assert.Equal(MetadataOrigin.LegacyMigrated, loaded.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(migrated.Revision, loaded.Revision);
        Assert.Equal(originalJson, File.ReadAllText(ProjectFolderLayout.GetProjectMetadataPath(folder.Path)));
    }

    [Fact]
    public void SaveCurrentProjectMetadata_WhenValueChanges_StoresUserOrigin()
    {
        using var folder = new TemporaryFolder();
        var migrated = new ProjectMetadata();
        migrated.SetValue(ProjectMetadataField.Title, "Alter Titel", MetadataOrigin.LegacyMigrated);
        new ProjectMetadataStore().Save(folder.Path, migrated);

        var projection = _service.SaveCurrentProjectMetadata(
            folder.Path,
            title: "Neuer Titel",
            author: "",
            album: "",
            narrator: "",
            genre: "");
        var loaded = new ProjectMetadataStore().LoadOrCreate(folder.Path);

        Assert.Equal("Neuer Titel", projection.Title);
        Assert.Equal(MetadataOrigin.User, loaded.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.True(loaded.Revision > migrated.Revision);
    }

    [Fact]
    public void SaveCurrentProjectMetadata_WhenStoreIsCorrupt_ThrowsAndDoesNotOverwrite()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var path = ProjectFolderLayout.GetProjectMetadataPath(folder.Path);
        const string corruptJson = "{ invalid";
        File.WriteAllText(path, corruptJson);

        Assert.ThrowsAny<Exception>(() => _service.SaveCurrentProjectMetadata(
            folder.Path,
            title: "Titel",
            author: "Autor",
            album: "",
            narrator: "",
            genre: ""));
        Assert.Equal(corruptJson, File.ReadAllText(path));
    }
}
