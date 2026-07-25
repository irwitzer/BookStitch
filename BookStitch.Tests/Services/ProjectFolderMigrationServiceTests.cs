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

        var changed = new ProjectFolderMigrationService().MigrateIfNeeded(folder.Path);

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

        var changed = new ProjectFolderMigrationService().MigrateIfNeeded(folder.Path);

        Assert.True(changed);
        var migratedTrack = Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1), "001.flac");
        Assert.True(File.Exists(migratedTrack));
        Assert.False(Directory.Exists(rippedFolder));

        var audioManifest = JsonSerializer.Deserialize<AudioDiscProjectManifest>(
            File.ReadAllText(ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path)), JsonOptions)!;
        Assert.Equal(Path.Combine("originals", "CD 01", "001.flac"), audioManifest.Discs[0].Tracks[0].RelativePath);

        var exportManifest = JsonSerializer.Deserialize<ExportWorkManifest>(
            File.ReadAllText(ProjectFolderLayout.GetWorkManifestPath(folder.Path)), JsonOptions)!;
        Assert.Equal(ProjectFolderLayout.GetOriginalsFolder(folder.Path), exportManifest.SourceFolder);
        Assert.Equal(migratedTrack, exportManifest.Tracks[0].SourcePath);
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
        var first = service.MigrateIfNeeded(folder.Path);
        var second = service.MigrateIfNeeded(folder.Path);

        Assert.False(first);
        Assert.False(second);
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }
}
