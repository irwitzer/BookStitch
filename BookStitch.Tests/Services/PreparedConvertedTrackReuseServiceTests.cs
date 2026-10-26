using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PreparedConvertedTrackReuseServiceTests
{
    [Fact]
    public void Prepared_file_counter_counts_only_current_audio_outputs_and_leaves_originals_unchanged()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var preset = ExportPreset.Parse("AAC Stereo 128 kbps");
        var originals = ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, 1);
        Directory.CreateDirectory(originals);
        var converted = ProjectFolderLayout.GetConvertedPresetFolder(folder.Path, preset.GetFolderName());
        Directory.CreateDirectory(converted);
        foreach (var name in new[] { "current.mp3", "stale.flac", "missing.mp3", "notes.txt" })
        {
            var source = Path.Combine(originals, name);
            File.WriteAllBytes(source, [1, 2, 3]);
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-2));
            if (name == "missing.mp3") continue;
            var track = new TrackInfo { FilePath = source, FileName = name,
                Extension = Path.GetExtension(source).TrimStart('.'), Codec = name.EndsWith("mp3") ? "MP3" : string.Empty,
                ProcessingAction = ProcessingActionIds.Convert };
            var output = ConvertedTrackPathService.GetConvertedTrackPath(converted, source, track);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllBytes(output, [4]);
            File.SetLastWriteTimeUtc(output, DateTime.UtcNow.AddMinutes(name == "stale.flac" ? -3 : -1));
        }
        Assert.Equal(1, PreparedConvertedTrackReuseService.CountReusablePreparedConvertedFiles(folder.Path, preset));
        Assert.All(Directory.EnumerateFiles(originals), source => Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source)));
    }

    [Theory]
    [InlineData(ProjectManifestTypes.Mp3DiscProject)]
    [InlineData(ProjectManifestTypes.AudioCdProject)]
    public void CanReuseForDiscProject_AcceptsCurrentPreparedTrack(string projectType)
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");

        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        File.WriteAllBytes(convertedPath, [4, 5, 6]);
        File.SetLastWriteTimeUtc(convertedPath, DateTime.UtcNow.AddMinutes(-1));

        Assert.True(PreparedConvertedTrackReuseService.CanReuseForDiscProject(
            projectType,
            sourcePath,
            convertedPath));
    }

    [Fact]
    public void CanReuseForDiscProject_RejectsLocalProject()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");

        File.WriteAllBytes(sourcePath, [1]);
        File.WriteAllBytes(convertedPath, [2]);

        Assert.False(PreparedConvertedTrackReuseService.CanReuseForDiscProject(
            ProjectManifestTypes.FolderProject,
            sourcePath,
            convertedPath));
    }

    [Theory]
    [InlineData("track.m4a.part")]
    [InlineData("track.m4a.copying")]
    public void CanReuseForDiscProject_RejectsTemporaryOutput(string fileName)
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, fileName);

        File.WriteAllBytes(sourcePath, [1]);
        File.WriteAllBytes(convertedPath, [2]);

        Assert.False(PreparedConvertedTrackReuseService.CanReuseForDiscProject(
            ProjectManifestTypes.AudioCdProject,
            sourcePath,
            convertedPath));
    }

    [Fact]
    public void CanReuseForDiscProject_RejectsOutputOlderThanSource()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "track.flac");
        var convertedPath = Path.Combine(folder.Path, "track.m4a");

        File.WriteAllBytes(convertedPath, [2]);
        File.SetLastWriteTimeUtc(convertedPath, DateTime.UtcNow.AddMinutes(-2));
        File.WriteAllBytes(sourcePath, [1]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-1));

        Assert.False(PreparedConvertedTrackReuseService.CanReuseForDiscProject(
            ProjectManifestTypes.AudioCdProject,
            sourcePath,
            convertedPath));
    }
}
