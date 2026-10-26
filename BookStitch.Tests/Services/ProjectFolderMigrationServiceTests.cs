using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectFolderMigrationServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Fact]
    public void MigrateIfNeeded_WhenFolderIsNormalEmptyFolder_DoesNotCreateProjectStructure()
    {
        using var folder = new TemporaryFolder();

        var changed = new ProjectFolderMigrationService().UpgradeLegacyLayout(folder.Path);

        Assert.False(changed);
        AssertNoProjectStructure(folder.Path);
    }

    [Fact]
    public void MigrateIfNeeded_WhenFolderContainsForeignFiles_DoesNotChangeAnything()
    {
        using var folder = new TemporaryFolder();
        var notePath = Path.Combine(folder.Path, "notes.txt");
        var jsonPath = Path.Combine(folder.Path, "project.json");
        File.WriteAllText(notePath, "normal user file");
        File.WriteAllText(jsonPath, "{\"Title\":\"Not a BookStitch manifest\"}");

        var changed = new ProjectFolderMigrationService().UpgradeLegacyLayout(folder.Path);

        Assert.False(changed);
        Assert.True(File.Exists(notePath));
        Assert.True(File.Exists(jsonPath));
        AssertNoProjectStructure(folder.Path);
    }

    [Fact]
    public void MigrateIfNeeded_WhenFolderIsNamedConvertedButIsNotProject_DoesNotNestProjectStructure()
    {
        using var folder = new TemporaryFolder();
        var convertedFolder = Directory.CreateDirectory(Path.Combine(folder.Path, "converted")).FullName;
        File.WriteAllText(Path.Combine(convertedFolder, "readme.txt"), "cache or user content");

        var changed = new ProjectFolderMigrationService().UpgradeLegacyLayout(convertedFolder);

        Assert.False(changed);
        AssertNoProjectStructure(convertedFolder);
        Assert.True(File.Exists(Path.Combine(convertedFolder, "readme.txt")));
    }

    [Fact]
    public void ProjectIndexScan_WhenDesktopLikeRootContainsForeignFolders_DoesNotCreateProjectStructure()
    {
        using var folder = new TemporaryFolder();
        var desktopRoot = Directory.CreateDirectory(Path.Combine(folder.Path, "Desktop")).FullName;
        var documents = Directory.CreateDirectory(Path.Combine(desktopRoot, "Documents")).FullName;
        var converted = Directory.CreateDirectory(Path.Combine(desktopRoot, "converted")).FullName;
        File.WriteAllText(Path.Combine(documents, "project.json"), "{\"Title\":\"foreign json\"}");
        File.WriteAllText(Path.Combine(converted, "track.m4a"), "foreign content");

        var projects = new ProjectIndexService().ScanProjects(desktopRoot);

        Assert.Empty(projects);
        Assert.False(Directory.Exists(ProjectFolderLayout.GetOriginalsFolder(desktopRoot)));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetMergeFolder(desktopRoot)));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetSettingsFolder(desktopRoot)));
        AssertNoProjectStructure(documents);
        AssertNoProjectStructure(converted);
    }

    [Fact]
    public void MigrateIfNeeded_MovesMp3DiscFoldersAndSettingsFiles()
    {
        using var folder = new TemporaryFolder();
        var legacyDiscFolder = Directory.CreateDirectory(Path.Combine(folder.Path, "CD 01")).FullName;
        File.WriteAllBytes(Path.Combine(legacyDiscFolder, "001.mp3"), [1, 2, 3]);
        Write(Path.Combine(folder.Path, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = "Mp3Disc",
            ProjectFolder = folder.Path,
            TotalDiscs = 1,
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    LocalFolder = legacyDiscFolder,
                    Status = Mp3DiscImportStatus.Completed
                }
            ]
        });
        File.WriteAllText(Path.Combine(folder.Path, TrackListStateService.FileName), "{}");

        var changed = new ProjectFolderMigrationService().UpgradeLegacyLayout(folder.Path);

        Assert.True(changed);
        var migratedDiscFolder = ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1);
        Assert.True(File.Exists(Path.Combine(migratedDiscFolder, "001.mp3")));
        Assert.False(Directory.Exists(legacyDiscFolder));
        Assert.True(File.Exists(ProjectFolderLayout.GetWorkManifestPath(folder.Path)));
        Assert.True(File.Exists(ProjectFolderLayout.GetTrackListStatePath(folder.Path)));

        var manifest = JsonSerializer.Deserialize<Mp3DiscProjectManifest>(
            File.ReadAllText(ProjectFolderLayout.GetWorkManifestPath(folder.Path)), JsonOptions)!;
        Assert.Equal(migratedDiscFolder, manifest.ImportedDiscs[0].LocalFolder);
    }

    [Fact]
    public void MigrateIfNeeded_MovesAudioTracksIntoDiscFoldersAndUpdatesManifests()
    {
        using var folder = new TemporaryFolder();
        var rippedFolder = Directory.CreateDirectory(Path.Combine(folder.Path, "ripped")).FullName;
        var legacyTrack = Path.Combine(rippedFolder, "001.flac");
        File.WriteAllBytes(legacyTrack, [1, 2, 3]);

        Write(Path.Combine(folder.Path, "audio-disc-project.json"), new AudioDiscProjectManifest
        {
            ProjectFolder = folder.Path,
            TotalDiscs = 1,
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = 1,
                            DiscNumber = 1,
                            TrackNumber = 1,
                            FileName = "001.flac",
                            RelativePath = Path.Combine("ripped", "001.flac"),
                            Status = AudioDiscTrackStatus.Ripped
                        }
                    ]
                }
            ]
        });
        Write(Path.Combine(folder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = folder.Path,
            SourceFolder = rippedFolder,
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = legacyTrack,
                    SourceFileName = "001.flac"
                }
            ]
        });

        var changed = new ProjectFolderMigrationService().UpgradeLegacyLayout(folder.Path);

        Assert.True(changed);
        var migratedTrack = Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1), "001.flac");
        Assert.True(File.Exists(migratedTrack));
        Assert.False(Directory.Exists(rippedFolder));

        var audioManifest = JsonSerializer.Deserialize<AudioDiscProjectManifest>(
            File.ReadAllText(ProjectFolderLayout.ResolveAudioDiscManifestPath(folder.Path)), JsonOptions)!;
        Assert.Equal(Path.Combine("originals", "CD 01", "001.flac"), audioManifest.Discs[0].Tracks[0].RelativePath);

        var exportManifest = JsonSerializer.Deserialize<ExportWorkManifest>(
            File.ReadAllText(ProjectFolderLayout.GetWorkManifestPath(folder.Path)), JsonOptions)!;
        Assert.Equal(ProjectFolderLayout.GetOriginalsFolder(folder.Path), exportManifest.SourceFolder);
        Assert.Equal(migratedTrack, exportManifest.Tracks[0].SourcePath);
    }

    [Fact]
    public void MigrateIfNeeded_MovesLegacyExportManifestFromRoot()
    {
        using var folder = new TemporaryFolder();
        Write(Path.Combine(folder.Path, "export-project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = folder.Path,
            SourceFolder = folder.Path,
            Tracks = []
        });

        var changed = new ProjectFolderMigrationService().UpgradeLegacyLayout(folder.Path);

        Assert.True(changed);
        Assert.False(File.Exists(Path.Combine(folder.Path, "export-project.json")));
        Assert.True(File.Exists(ProjectFolderLayout.GetExportManifestPath(folder.Path)));
        Assert.True(ProjectFolderMigrationService.IsRecognizedBookStitchProjectFolder(folder.Path));
    }

    [Fact]
    public void MigrateIfNeeded_NormalizesLegacyDiscTrackPathsWithoutRewritingCurrentPaths()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);

        var legacyDiscFolder = Directory.CreateDirectory(
            Path.Combine(folder.Path, "CD 37")).FullName;
        var legacyTrack = Path.Combine(legacyDiscFolder, "037.mp3");
        File.WriteAllBytes(legacyTrack, [1, 2, 3]);

        var currentDiscFolder = ProjectFolderLayout.GetDiscOriginalsFolder(
            folder.Path,
            2);
        Directory.CreateDirectory(currentDiscFolder);
        var currentTrack = Path.Combine(currentDiscFolder, "002.mp3");
        File.WriteAllBytes(currentTrack, [4, 5, 6]);

        Write(ProjectFolderLayout.GetWorkManifestPath(folder.Path), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectWorkFolder = folder.Path,
            SourceFolder = legacyDiscFolder,
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = legacyTrack,
                    SourceFileName = "037.mp3"
                },
                new ExportWorkManifestTrack
                {
                    TrackIndex = 2,
                    SourcePath = currentTrack,
                    SourceFileName = "002.mp3"
                }
            ]
        });

        var changed = new ProjectFolderMigrationService().UpgradeLegacyLayout(folder.Path);

        Assert.True(changed);

        var migratedDiscFolder = ProjectFolderLayout.GetDiscOriginalsFolder(
            folder.Path,
            37);
        var manifest = JsonSerializer.Deserialize<ExportWorkManifest>(
            File.ReadAllText(ProjectFolderLayout.GetWorkManifestPath(folder.Path)),
            JsonOptions)!;

        Assert.Equal(migratedDiscFolder, manifest.SourceFolder);
        Assert.Equal(
            Path.Combine(migratedDiscFolder, "037.mp3"),
            manifest.Tracks[0].SourcePath);
        Assert.Equal(currentTrack, manifest.Tracks[1].SourcePath);
    }

    [Fact]
    public void MigrateIfNeeded_IsIdempotent()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        Write(ProjectFolderLayout.GetWorkManifestPath(folder.Path), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = folder.Path,
            SourceFolder = ProjectFolderLayout.GetOriginalsFolder(folder.Path)
        });

        var service = new ProjectFolderMigrationService();
        var first = service.UpgradeLegacyLayout(folder.Path);
        var second = service.UpgradeLegacyLayout(folder.Path);

        Assert.False(first);
        Assert.False(second);
    }

    [Fact]
    public void MigrateIfNeeded_KeepsLegacyManifestWhenTargetManifestDiffers()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var legacyManifestPath = Path.Combine(folder.Path, ProjectFolderLayout.WorkManifestFileName);
        var targetManifestPath = ProjectFolderLayout.GetWorkManifestPath(folder.Path);
        File.WriteAllText(legacyManifestPath, "{\"Title\":\"Legacy\"}");
        File.WriteAllText(targetManifestPath, "{\"Title\":\"Current\"}");

        var changed = new ProjectFolderMigrationService().UpgradeLegacyLayout(folder.Path);

        Assert.False(changed);
        Assert.True(File.Exists(legacyManifestPath));
        Assert.True(File.Exists(targetManifestPath));
        Assert.Contains("Legacy", File.ReadAllText(legacyManifestPath));
        Assert.Contains("Current", File.ReadAllText(targetManifestPath));
    }

    [Fact]
    public void MigrateIfNeeded_KeepsEqualSizedDifferentFilesInLegacyFolder()
    {
        using var folder = new TemporaryFolder();
        var legacyDiscFolder = Directory.CreateDirectory(Path.Combine(folder.Path, "CD 01")).FullName;
        var targetDiscFolder = ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1);
        Directory.CreateDirectory(targetDiscFolder);
        var legacyTrack = Path.Combine(legacyDiscFolder, "001.mp3");
        var targetTrack = Path.Combine(targetDiscFolder, "001.mp3");
        File.WriteAllBytes(legacyTrack, [1, 2, 3, 4]);
        File.WriteAllBytes(targetTrack, [4, 3, 2, 1]);

        var changed = new ProjectFolderMigrationService().UpgradeLegacyLayout(folder.Path);

        Assert.False(changed);
        Assert.True(File.Exists(legacyTrack));
        Assert.True(File.Exists(targetTrack));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(legacyTrack));
        Assert.Equal([4, 3, 2, 1], File.ReadAllBytes(targetTrack));
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void AssertNoProjectStructure(string folder)
    {
        Assert.False(Directory.Exists(ProjectFolderLayout.GetOriginalsFolder(folder)));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetConvertedFolder(folder)));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetMergeFolder(folder)));
        Assert.False(Directory.Exists(ProjectFolderLayout.GetSettingsFolder(folder)));
    }
}
