using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectPersistenceHardeningTests
{
    [Fact]
    public void Future_schema_is_rejected_before_moving_legacy_files()
    {
        using var folder = new TemporaryFolder();
        var legacy = Directory.CreateDirectory(Path.Combine(folder.Path, "CD 01")).FullName;
        File.WriteAllBytes(Path.Combine(legacy, "track.mp3"), [1, 2, 3]);
        var path = Path.Combine(folder.Path, "project.json");
        var json = JsonSerializer.Serialize(new Mp3DiscProjectManifest
        { SchemaVersion = 999, ProjectFolder = folder.Path, TotalDiscs = 1 });
        File.WriteAllText(path, json);
        Assert.Throws<PersistentJsonSchemaException>(() => new ProjectFolderMigrationService().UpgradeLegacyLayout(folder.Path));
        Assert.Equal(json, File.ReadAllText(path));
        Assert.True(File.Exists(Path.Combine(legacy, "track.mp3")));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetSettingsFolder(folder.Path)));
    }

    [Fact]
    public void Migration_failure_after_first_publication_retains_all_legacy_sources_and_can_retry()
    {
        using var folder = new TemporaryFolder();
        var legacy = Directory.CreateDirectory(Path.Combine(folder.Path, "CD 01")).FullName;
        var first = Path.Combine(legacy, "01.mp3");
        var second = Path.Combine(legacy, "02.mp3");
        File.WriteAllBytes(first, [1, 2]);
        File.WriteAllBytes(second, [3, 4]);
        File.WriteAllText(Path.Combine(folder.Path, "project.json"), JsonSerializer.Serialize(new Mp3DiscProjectManifest
        {
            ProjectFolder = folder.Path, TotalDiscs = 1,
            ImportedDiscs = [new() { DiscNumber = 1, FileCount = 2, CopiedFiles = 2, LocalFolder = legacy }]
        }));
        var copies = 0;
        var service = new ProjectFolderMigrationService((source, target) =>
        {
            if (++copies == 2) throw new IOException("second publication failed");
            File.Copy(source, target);
        });
        Assert.Throws<IOException>(() => service.UpgradeLegacyLayout(folder.Path));
        Assert.Equal(2, copies);
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(first));
        Assert.Equal(new byte[] { 3, 4 }, File.ReadAllBytes(second));
        Assert.True(File.Exists(Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1), "01.mp3")));
        Assert.True(new ProjectFolderMigrationService().UpgradeLegacyLayout(folder.Path));
        Assert.False(Directory.Exists(legacy));
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(folder.Path)!;
        Assert.False(plan.CanContinueDiscImport);
        Assert.Equal(2, plan.Tracks.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_or_same_size_corrupt_mp3_source_reopens_acquisition_and_persists_recovery(bool corrupt)
    {
        using var folder = new TemporaryFolder();
        var discFolder = Directory.CreateDirectory(ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1)).FullName;
        var source = Path.Combine(discFolder, "track.mp3");
        File.WriteAllBytes(source, [1, 2, 3]);
        var id = Guid.NewGuid();
        new SourceCopyIdentityStore().RecordCopiedFile(folder.Path, source, source, id);
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = folder.Path, TotalDiscs = 1,
            ImportedDiscs = [new() { DiscNumber = 1, FileCount = 1, CopiedFiles = 1, LocalFolder = discFolder,
                Signature = "disc", SourceFiles = [new() { SourceId = id, SourceSizeBytes = 3,
                    SourceFileName = "track.mp3", RelativePath = Path.Combine("CD 01", "track.mp3") }] }]
        };
        var service = new Mp3DiscProjectService();
        service.Save(manifest);
        if (corrupt) File.WriteAllBytes(source, [3, 2, 1]); else File.Delete(source);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(folder.Path)!;
        Assert.True(plan.CanContinueDiscImport);
        Assert.Equal(1, plan.NextMissingDiscNumber);
        var loaded = service.TryLoad(folder.Path)!;
        Assert.Equal("Incomplete", loaded.ImportedDiscs[0].Status);
        Assert.Equal(0, service.CountCompletedImportedDiscs(loaded));
        Assert.Contains("Incomplete", File.ReadAllText(ProjectFolderLayout.GetMp3DiscManifestPath(folder.Path)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_or_same_size_corrupt_flac_is_pending_and_persisted(bool corrupt)
    {
        using var folder = new TemporaryFolder();
        var discFolder = Directory.CreateDirectory(ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1)).FullName;
        var source = Path.Combine(discFolder, "track.flac");
        File.WriteAllBytes(source, [1, 2, 3]);
        var manifest = new AudioDiscProjectManifest
        {
            ProjectFolder = folder.Path, TotalDiscs = 1,
            Discs = [new() { DiscNumber = 1, Tracks = [new() { DiscNumber = 1, TrackNumber = 1, GlobalIndex = 1,
                SourceId = Guid.NewGuid(), FileName = "track.flac", RelativePath = Path.GetRelativePath(folder.Path, source),
                OutputSizeBytes = 3, ContentSha256 = ProjectSourceIntegrityService.ComputeHash(source), Status = AudioDiscTrackStatus.Ripped }] }]
        };
        var service = new AudioDiscProjectService();
        service.Save(manifest);
        if (corrupt) File.WriteAllBytes(source, [3, 2, 1]); else File.Delete(source);
        var loaded = service.LoadForResume(folder.Path)!;
        Assert.Equal(AudioDiscTrackStatus.Pending, loaded.Discs[0].Tracks[0].Status);
        Assert.False(service.IsProjectRipCompleted(loaded));
        Assert.Equal(1, service.GetNextRequiredDiscNumber(loaded));
        var persisted = JsonSerializer.Deserialize<AudioDiscProjectManifest>(File.ReadAllText(ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path)))!;
        Assert.Equal(AudioDiscTrackStatus.Pending, persisted.Discs[0].Tracks[0].Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Corrupt_disc_manifest_never_falls_back_to_valid_folder_export(bool audio)
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        File.WriteAllText(ProjectFolderLayout.GetExportManifestPath(folder.Path), JsonSerializer.Serialize(new ExportWorkManifest
        { ProjectType = ProjectManifestTypes.FolderProject, ProjectWorkFolder = folder.Path }));
        var damaged = audio ? ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path) : ProjectFolderLayout.GetWorkManifestPath(folder.Path);
        File.WriteAllText(damaged, "{ damaged");
        Assert.ThrowsAny<IOException>(() => new ProjectResumePlanService().BuildFromProjectFolder(folder.Path));
        Assert.Equal("{ damaged", File.ReadAllText(damaged));

    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"ProjectType\":\"Mp3Disc\"}")]
    public void Structurally_invalid_existing_work_manifest_is_preserved_and_blocks_folder_fallback(string json)
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var work = ProjectFolderLayout.GetWorkManifestPath(folder.Path);
        File.WriteAllText(work, json);
        File.WriteAllText(ProjectFolderLayout.GetExportManifestPath(folder.Path), JsonSerializer.Serialize(new ExportWorkManifest
        { ProjectType = ProjectManifestTypes.FolderProject, ProjectWorkFolder = folder.Path }));
        Assert.ThrowsAny<IOException>(() => new ProjectResumePlanService().BuildFromProjectFolder(folder.Path));
        Assert.Throws<PersistentJsonSchemaException>(() => new WorkManifestService().LoadOrCreate(work, folder.Path, "source", "preset"));
        Assert.Equal(json, File.ReadAllText(work));
    }

    [Fact]
    public void Work_normalization_persists_folder_settings_and_revision_neutral_reloads()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "export-project.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new ExportWorkManifest
        { ProjectWorkFolder = "old", SourceFolder = "old-source", SelectedPreset = "old-preset", ProjectId = "stable" }));
        var service = new WorkManifestService();
        service.LoadOrCreate(path, folder.Path, "new-source", "new-preset");
        var persisted = JsonSerializer.Deserialize<ExportWorkManifest>(File.ReadAllText(path))!;
        Assert.Equal(folder.Path, persisted.ProjectWorkFolder);
        Assert.Equal("new-source", persisted.SourceFolder);
        Assert.Equal("new-preset", persisted.SelectedPreset);
        var before = File.ReadAllBytes(path);
        service.LoadOrCreate(path, folder.Path, "new-source", "new-preset");
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disc_folder_only_normalization_is_persisted(bool audio)
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        if (audio)
        {
            var path = ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path);
            File.WriteAllText(path, JsonSerializer.Serialize(new AudioDiscProjectManifest { ProjectFolder = "old" }));
            Assert.Equal(ProjectUpgradeOutcome.Upgraded, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
            Assert.Equal(folder.Path, JsonSerializer.Deserialize<AudioDiscProjectManifest>(File.ReadAllText(path))!.ProjectFolder);
        }
        else
        {
            var path = ProjectFolderLayout.GetWorkManifestPath(folder.Path);
            File.WriteAllText(path, JsonSerializer.Serialize(new Mp3DiscProjectManifest { ProjectFolder = "old" }));
            Assert.Equal(ProjectUpgradeOutcome.Upgraded, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
            Assert.Equal(folder.Path, JsonSerializer.Deserialize<Mp3DiscProjectManifest>(File.ReadAllText(ProjectFolderLayout.GetMp3DiscManifestPath(folder.Path)))!.ProjectFolder);
        }
    }
}
