using System.IO;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscLiveConversionServiceTests
{
    [Fact]
    public void CreatePreparation_UsesPresetFolderAndStableRippedTrackIdentity()
    {
        var service = new AudioDiscLiveConversionService();
        var projectFolder = Path.Combine(Path.GetTempPath(), "BookStitch.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(projectFolder, "ripped", "003_Test.flac");
        var preset = ExportPreset.Parse("AAC Mono 64 kbps");

        var result = service.CreatePreparation(
            new AudioDiscRippedTrack(2, 3, 1, sourcePath, TimeSpan.FromMinutes(4)),
            projectFolder,
            preset);

        Assert.Equal(2, result.Track.Index);
        Assert.Equal(2, result.Track.DiscNumber);
        Assert.Equal(1, result.Track.TrackNumber);
        Assert.Equal("FLAC", result.Track.Codec);
        Assert.Equal("Konvertieren", result.Track.ProcessingAction);
        Assert.Equal(TimeSpan.FromMinutes(4).Ticks, result.Track.DurationTicks);
        Assert.Equal(sourcePath, result.SourcePath);
        Assert.StartsWith(
            Path.Combine(projectFolder, "converted", "aac_mono_64k"),
            result.ConvertedPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".m4a", result.ConvertedPath, StringComparison.OrdinalIgnoreCase);
    }
}
