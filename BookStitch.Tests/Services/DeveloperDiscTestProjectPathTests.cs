using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DeveloperDiscTestProjectPathTests
{
    [Fact]
    public void AudioDiscRebasePath_UsesCopiedManifestRootWhenTemplateFolderDiffers()
    {
        using var root = new TemporaryFolder();
        var persistedRoot = Path.Combine(root.Path, "Original project");
        var selectedTemplateRoot = Path.Combine(root.Path, "Renamed template folder");
        var workingRoot = Path.Combine(root.Path, "Working copy");
        var storedPath = Path.Combine(persistedRoot, "originals", "CD 01", "001.flac");

        var rebased = DeveloperAudioDiscTestProjectService.RebasePath(
            storedPath,
            workingRoot,
            persistedRoot,
            selectedTemplateRoot);

        Assert.Equal(
            Path.Combine(workingRoot, "originals", "CD 01", "001.flac"),
            rebased);
    }

    [Fact]
    public void Mp3DiscRebasePath_UsesCopiedManifestRootWhenTemplateFolderDiffers()
    {
        using var root = new TemporaryFolder();
        var persistedRoot = Path.Combine(root.Path, "Original project");
        var selectedTemplateRoot = Path.Combine(root.Path, "Renamed template folder");
        var workingRoot = Path.Combine(root.Path, "Working copy");
        var storedPath = Path.Combine(persistedRoot, "converted", "aac_stereo_192k", "001.m4a");

        var rebased = DeveloperMp3DiscTestProjectService.RebasePath(
            storedPath,
            workingRoot,
            persistedRoot,
            selectedTemplateRoot);

        Assert.Equal(
            Path.Combine(workingRoot, "converted", "aac_stereo_192k", "001.m4a"),
            rebased);
    }
    [Fact]
    public void ResolveStoredProjectRoot_UsesTrackPathsInsteadOfRenamedTemplateFolder()
    {
        using var root = new TemporaryFolder();
        var persistedRoot = Path.Combine(root.Path, "Original project");
        var selectedTemplateRoot = Path.Combine(root.Path, "Renamed template folder");
        var tracks = new List<ExportWorkManifestTrack>
        {
            new()
            {
                SourcePath = Path.Combine(persistedRoot, "originals", "CD 01", "001.flac"),
                ConvertedPath = Path.Combine(persistedRoot, "converted", "aac_stereo_192k", "001.m4a")
            }
        };

        var audioRoot = DeveloperAudioDiscTestProjectService.ResolveStoredProjectRoot(tracks, selectedTemplateRoot);
        var mp3Root = DeveloperMp3DiscTestProjectService.ResolveStoredProjectRoot(tracks, selectedTemplateRoot);

        Assert.Equal(persistedRoot, audioRoot);
        Assert.Equal(persistedRoot, mp3Root);
    }


    [Fact]
    public void Mp3DiscRebaseCopiedConvertedFiles_RenamesOldHashToCurrentSourceHash()
    {
        using var root = new TemporaryFolder();
        var projectFolder = Path.Combine(root.Path, "Working copy");
        var oldProjectRoot = Path.Combine(root.Path, "Original project");
        var sourcePath = Path.Combine(projectFolder, "originals", "CD 01", "001_Book.mp3");
        var oldSourcePath = Path.Combine(oldProjectRoot, "originals", "CD 01", "001_Book.mp3");
        var preset = ExportPreset.Parse("AAC Mono 64 kbps");
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, preset.GetFolderName());
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        Directory.CreateDirectory(convertedFolder);
        File.WriteAllBytes(sourcePath, [1, 2, 3]);

        var oldConvertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
            convertedFolder,
            oldSourcePath,
            new TrackInfo { FileName = "001_Book.mp3" });
        var expectedConvertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
            convertedFolder,
            sourcePath,
            new TrackInfo { FileName = "001_Book.mp3" });
        File.WriteAllBytes(oldConvertedPath, [1, 2, 3, 4]);
        File.SetLastWriteTimeUtc(oldConvertedPath, File.GetLastWriteTimeUtc(sourcePath).AddSeconds(1));

        DeveloperMp3DiscTestProjectService.RebaseCopiedConvertedFilesForPreset(
            projectFolder,
            preset.DisplayName);

        Assert.False(File.Exists(oldConvertedPath));
        Assert.True(File.Exists(expectedConvertedPath));
        Assert.Equal(1, DeveloperMp3DiscTestProjectService.CountReusablePreparedConvertedFiles(projectFolder, preset));
    }

    [Fact]
    public void Mp3DiscRebaseCopiedConvertedFiles_RemovesResetConvertedFile()
    {
        using var root = new TemporaryFolder();
        var projectFolder = Path.Combine(root.Path, "Working copy");
        var oldProjectRoot = Path.Combine(root.Path, "Original project");
        var sourcePath = Path.Combine(projectFolder, "originals", "CD 01", "143_Book.mp3");
        var oldSourcePath = Path.Combine(oldProjectRoot, "originals", "CD 01", "143_Book.mp3");
        var preset = ExportPreset.Parse("AAC Mono 64 kbps");
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectFolder, preset.GetFolderName());
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        Directory.CreateDirectory(convertedFolder);
        File.WriteAllBytes(sourcePath, [1, 2, 3]);

        var oldConvertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
            convertedFolder,
            oldSourcePath,
            new TrackInfo { FileName = "143_Book.mp3" });
        File.WriteAllBytes(oldConvertedPath, [1, 2, 3, 4]);

        DeveloperMp3DiscTestProjectService.RebaseCopiedConvertedFilesForPreset(
            projectFolder,
            preset.DisplayName,
            [sourcePath]);

        Assert.False(File.Exists(oldConvertedPath));
        Assert.Equal(0, DeveloperMp3DiscTestProjectService.CountReusablePreparedConvertedFiles(projectFolder, preset));
    }

}
