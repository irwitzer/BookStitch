using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectFormatCreationTests
{
    [Fact]
    public void MissingManifestResolvers_ReturnCanonicalNamesWithoutCreatingLegacyFiles()
    {
        using var folder = new TemporaryFolder();
        Assert.Equal(ProjectFolderLayout.GetMp3DiscManifestPath(folder.Path), ProjectFolderLayout.ResolveMp3DiscManifestPath(folder.Path));
        Assert.Equal(ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path), ProjectFolderLayout.ResolveAudioDiscManifestPath(folder.Path));
        Assert.Equal(ProjectFolderLayout.GetExportManifestPath(folder.Path), ProjectFolderLayout.ResolveExportManifestPath(folder.Path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PassiveDiscReader_LeavesLegacyJsonAndDirectoriesUntouched(bool audio)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, audio ? "audio-disc-project.json" : "project.json");
        File.WriteAllText(path, audio
            ? JsonSerializer.Serialize(new AudioDiscProjectManifest { ProjectFolder = "old", TotalDiscs = 1 })
            : JsonSerializer.Serialize(new Mp3DiscProjectManifest { ProjectFolder = "old", TotalDiscs = 1 }));
        var bytes = File.ReadAllBytes(path);
        if (audio) Assert.NotNull(new AudioDiscProjectService().TryLoad(folder.Path));
        else Assert.NotNull(new Mp3DiscProjectService().TryLoad(folder.Path));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetSettingsFolder(folder.Path)));
    }

    [Fact]
    public void NewFolder_ImmediatelyHasCurrentMainAndExportUsesSameIdentity()
    {
        using var folder = new TemporaryFolder();
        var source = Directory.CreateDirectory(Path.Combine(folder.Path, "source")).FullName;
        var project = new LocalProjectImportService().CreateProjectFolder(Path.Combine(folder.Path, "projects"), source);
        var main = new FolderProjectManifestService().Read(project)!;
        Assert.Equal(1, main.ProjectFormatVersion);
        Assert.Equal(source, main.SourceFolder);
        Assert.Equal("NotChecked", main.Health.LastIntegrityStatus);
        Assert.True(Directory.Exists(ProjectFolderLayout.GetSourceMetadataSourcesFolder(project)));
        Assert.False(File.Exists(ProjectFolderLayout.GetWorkManifestPath(project)));
        var store = new WorkManifestService();
        var path = ProjectFolderLayout.GetExportManifestPath(project);
        var export = store.LoadOrCreate(path, project, ProjectFolderLayout.GetOriginalsFolder(project), "AAC Mono 64 kbps");
        store.Save(path, export);
        Assert.Equal(main.ProjectId, export.ProjectId);
        Assert.Equal(source, new FolderProjectManifestService().Read(project)!.SourceFolder);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewDiscSave_WritesCurrentMainAndNeverCreatesLegacyNameOrAudioCopyIdentity(bool audio)
    {
        using var folder = new TemporaryFolder();
        if (audio) new AudioDiscProjectService().Save(new AudioDiscProjectManifest { ProjectFolder = folder.Path });
        else new Mp3DiscProjectService().Save(new Mp3DiscProjectManifest { ProjectFolder = folder.Path });
        var mainPath = audio ? ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path) : ProjectFolderLayout.GetMp3DiscManifestPath(folder.Path);
        using var json = JsonDocument.Parse(File.ReadAllText(mainPath));
        Assert.Equal(1, json.RootElement.GetProperty("ProjectFormatVersion").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("ProjectId").GetString()));
        Assert.False(File.Exists(ProjectFolderLayout.GetWorkManifestPath(folder.Path)));
        Assert.False(File.Exists(Path.Combine(ProjectFolderLayout.GetSettingsFolder(folder.Path), "audio-disc-project.json")));
        Assert.False(File.Exists(Path.Combine(ProjectFolderLayout.GetSettingsFolder(folder.Path), "source-copy-identities.json")));
        Assert.Equal(ProjectUpgradeOutcome.OK, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(folder.Path).Outcome);
    }
}
