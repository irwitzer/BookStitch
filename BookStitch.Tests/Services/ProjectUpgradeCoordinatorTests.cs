using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectUpgradeCoordinatorTests
{
    [Fact]
    public void IdenticalLegacyAndCanonicalMain_DoNotConflictAfterOurOwnPathNormalization()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var oldDisc = Directory.CreateDirectory(Path.Combine(folder.Path, "CD 01")).FullName;
        File.WriteAllBytes(Path.Combine(oldDisc, "one.mp3"), [1, 2]);
        var json = JsonSerializer.Serialize(new Mp3DiscProjectManifest { ProjectFolder = folder.Path, TotalDiscs = 1,
            ImportedDiscs = [new() { DiscNumber = 1, LocalFolder = oldDisc, FileCount = 1, CopiedFiles = 1 }] });
        File.WriteAllText(Path.Combine(folder.Path, "project.json"), json);
        File.WriteAllText(ProjectFolderLayout.GetMp3DiscManifestPath(folder.Path), json);

        Assert.Equal(ProjectUpgradeOutcome.Upgraded, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.False(File.Exists(Path.Combine(folder.Path, "project.json")));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1), "one.mp3")));
        Assert.Equal(ProjectUpgradeOutcome.RebuildRequired, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.Equal(Guid.Empty, new Mp3DiscProjectService().LoadForResume(folder.Path)!.ImportedDiscs.Single().SourceFiles.Single().SourceId);
    }

    [Fact]
    public void AmbiguousAcquisitionAndExportManifest_DoesNotGuessOrChangeAnything()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "project.json");
        const string json = "{\"ProjectType\":\"Mp3DiscProject\",\"ImportedDiscs\":[],\"Tracks\":[]}";
        File.WriteAllText(path, json);
        Assert.Equal(ProjectUpgradeOutcome.RebuildRequired, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.Equal(json, File.ReadAllText(path));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetSettingsFolder(folder.Path)));
    }

    [Fact]
    public void ForeignAudioManifest_DoesNotInferAudioTypeFromFileName()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "audio-disc-project.json");
        const string json = "{\"ProjectType\":\"OtherApp\",\"Discs\":[]}";
        File.WriteAllText(path, json);
        Assert.Equal(ProjectUpgradeOutcome.RebuildRequired, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.Equal(json, File.ReadAllText(path));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetSettingsFolder(folder.Path)));
    }

    [Fact]
    public void Upgrade_AfterRenameFailure_DoesNotPublishVersionAndCanResume()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var settings = ProjectFolderLayout.GetSettingsFolder(folder.Path);
        File.WriteAllText(Path.Combine(settings, "project.json"), JsonSerializer.Serialize(new Mp3DiscProjectManifest
            { ProjectFolder = folder.Path, TotalDiscs = 2 }));
        var metadata = ProjectFolderLayout.GetProjectMetadataPath(folder.Path);
        File.WriteAllText(metadata, "{\"schemaVersion\":999}");
        var coordinator = new ProjectUpgradeCoordinator();

        Assert.Equal(ProjectUpgradeOutcome.RebuildRequired, coordinator.EnsureCurrentProjectFormat(folder.Path).Outcome);
        var main = ProjectFolderLayout.GetMp3DiscManifestPath(folder.Path);
        Assert.True(File.Exists(main));
        Assert.Equal(0, JsonNode.Parse(File.ReadAllText(main))!["ProjectFormatVersion"]!.GetValue<int>());
        File.Delete(metadata); // Correct only the intentionally invalid test fixture.
        Assert.Equal(ProjectUpgradeOutcome.Upgraded, coordinator.EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.Equal(ProjectUpgradeOutcome.OK, coordinator.EnsureCurrentProjectFormat(folder.Path).Outcome);
    }

    [Fact]
    public void Upgrade_LegacyMediaConflict_IsNeedsAttentionAndRetainsBothFiles()
    {
        using var folder = new TemporaryFolder();
        var oldFolder = Directory.CreateDirectory(Path.Combine(folder.Path, "CD 01")).FullName;
        var targetFolder = Directory.CreateDirectory(ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1)).FullName;
        File.WriteAllBytes(Path.Combine(oldFolder, "track.mp3"), [1, 2]);
        File.WriteAllBytes(Path.Combine(targetFolder, "track.mp3"), [2, 1]);
        File.WriteAllText(Path.Combine(folder.Path, "project.json"), JsonSerializer.Serialize(new Mp3DiscProjectManifest
            { ProjectFolder = folder.Path, TotalDiscs = 1, ImportedDiscs = [new() { DiscNumber = 1, LocalFolder = oldFolder }] }));
        Assert.Equal(ProjectUpgradeOutcome.RebuildRequired, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(oldFolder, "track.mp3")));
        Assert.Equal(new byte[] { 2, 1 }, File.ReadAllBytes(Path.Combine(targetFolder, "track.mp3")));
        Assert.False(File.Exists(ProjectFolderLayout.GetMp3DiscManifestPath(folder.Path)));
    }

    [Theory]
    [InlineData("{\"ProjectType\":\"OtherApp\",\"Tracks\":[]}")]
    [InlineData("{\"ProjectType\":\"Mp3Disc\",\"ImportedDiscs\":[null]}")]
    public void Upgrade_UnsupportedOrMalformedShape_DoesNotGuessProjectType(string json)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "project.json");
        File.WriteAllText(path, json);
        Assert.Equal(ProjectUpgradeOutcome.RebuildRequired, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.Equal(json, File.ReadAllText(path));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetSettingsFolder(folder.Path)));
    }

    [Fact]
    public void CurrentFormatAndDamagedHealth_AreIndependentAndRemainUnchanged()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var path = ProjectFolderLayout.GetFolderManifestPath(folder.Path);
        var main = new FolderProjectManifest { ProjectId = "known-id", ProjectFolder = folder.Path,
            Health = new() { LastIntegrityStatus = "Damaged", LastIntegrityCheckProjectFormatVersion = 1 } };
        File.WriteAllText(path, JsonSerializer.Serialize(main));
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(ProjectUpgradeOutcome.OK, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Upgrade_EmptyExportIdentity_InitializesSameIdentityForMainAndExport()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(Path.Combine(folder.Path, "project.json"), JsonSerializer.Serialize(new ExportWorkManifest { ProjectWorkFolder = folder.Path }));
        Assert.Equal(ProjectUpgradeOutcome.Upgraded, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
        var main = new FolderProjectManifestService().Read(folder.Path)!;
        var export = new WorkManifestService().ReadOnly(ProjectFolderLayout.GetExportManifestPath(folder.Path))!;
        Assert.False(string.IsNullOrWhiteSpace(main.ProjectId));
        Assert.Equal(main.ProjectId, export.ProjectId);
    }

    [Theory]
    [InlineData("FolderProject", "folder-project.json")]
    [InlineData("Mp3DiscProject", "mp3-cd-project.json")]
    [InlineData("AudioCdProject", "audio-cd-project.json")]
    public void Upgrade_CurrentLegacyTypes_PreservesExportAndCreatesVersionedMain(string type, string mainName)
    {
        using var folder = new TemporaryFolder();
        var settings = Directory.CreateDirectory(ProjectFolderLayout.GetSettingsFolder(folder.Path)).FullName;
        var export = new ExportWorkManifest { ProjectType = type, ProjectId = "existing-id", SourceFolder = "original source", ProjectWorkFolder = folder.Path };
        File.WriteAllText(Path.Combine(settings, type == ProjectManifestTypes.Mp3DiscProject ? "export-project.json" : "project.json"), JsonSerializer.Serialize(export));
        if (type == ProjectManifestTypes.Mp3DiscProject)
            File.WriteAllText(Path.Combine(settings, "project.json"), JsonSerializer.Serialize(new Mp3DiscProjectManifest { ProjectFolder = folder.Path, TotalDiscs = 2 }));
        if (type == ProjectManifestTypes.AudioCdProject)
            File.WriteAllText(Path.Combine(settings, "audio-disc-project.json"), JsonSerializer.Serialize(new AudioDiscProjectManifest { ProjectFolder = folder.Path, TotalDiscs = 2 }));
        var sourceId = Guid.NewGuid().ToString("N");
        var legacyMetadata = Directory.CreateDirectory(Path.Combine(settings, "metadata", "sources")).FullName;
        File.WriteAllText(Path.Combine(legacyMetadata, sourceId + ".json"), "{\"Evidence\":42}");

        var service = new ProjectUpgradeCoordinator();
        var result = service.EnsureCurrentProjectFormat(folder.Path);

        Assert.Equal(ProjectUpgradeOutcome.Upgraded, result.Outcome);
        var mainPath = Path.Combine(settings, mainName);
        var main = JsonNode.Parse(File.ReadAllText(mainPath))!;
        Assert.Equal(1, main["ProjectFormatVersion"]!.GetValue<int>());
        Assert.Equal("existing-id", main["ProjectId"]!.GetValue<string>());
        Assert.Equal("NotChecked", main["Health"]!["LastIntegrityStatus"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(settings, "export-project.json")));
        Assert.False(File.Exists(Path.Combine(settings, "project.json")));
        Assert.False(File.Exists(Path.Combine(settings, "audio-disc-project.json")));
        Assert.Equal("{\"Evidence\":42}", File.ReadAllText(Path.Combine(settings, "source-metadata", sourceId + ".json")));
        var timestamp = File.GetLastWriteTimeUtc(mainPath);
        Assert.Equal(ProjectUpgradeOutcome.OK, service.EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(mainPath));
    }

    [Fact]
    public void Upgrade_ConflictingExport_PreservesAllBytesAndDoesNotPublishVersion()
    {
        using var folder = new TemporaryFolder();
        var settings = Directory.CreateDirectory(ProjectFolderLayout.GetSettingsFolder(folder.Path)).FullName;
        var legacy = Path.Combine(settings, "project.json");
        var target = Path.Combine(settings, "export-project.json");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new ExportWorkManifest { ProjectId = "one" }));
        File.WriteAllText(target, JsonSerializer.Serialize(new ExportWorkManifest { ProjectId = "two" }));
        var before = File.ReadAllBytes(legacy);
        var targetBefore = File.ReadAllBytes(target);

        Assert.Equal(ProjectUpgradeOutcome.RebuildRequired, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.Equal(before, File.ReadAllBytes(legacy));
        Assert.Equal(targetBefore, File.ReadAllBytes(target));
        Assert.False(File.Exists(Path.Combine(settings, "folder-project.json")));
    }

    [Fact]
    public void Upgrade_ConflictingSourceMetadata_DoesNotMoveAnyManifest()
    {
        using var folder = new TemporaryFolder();
        var settings = Directory.CreateDirectory(ProjectFolderLayout.GetSettingsFolder(folder.Path)).FullName;
        var legacy = Path.Combine(settings, "project.json");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new ExportWorkManifest()));
        var oldFolder = Directory.CreateDirectory(Path.Combine(settings, "metadata", "sources")).FullName;
        var newFolder = Directory.CreateDirectory(Path.Combine(settings, "source-metadata")).FullName;
        File.WriteAllText(Path.Combine(oldFolder, "source.json"), "old");
        File.WriteAllText(Path.Combine(newFolder, "source.json"), "different");
        Assert.Equal(ProjectUpgradeOutcome.RebuildRequired, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
        Assert.True(File.Exists(legacy));
        Assert.Equal("old", File.ReadAllText(Path.Combine(oldFolder, "source.json")));
        Assert.Equal("different", File.ReadAllText(Path.Combine(newFolder, "source.json")));
    }
}
