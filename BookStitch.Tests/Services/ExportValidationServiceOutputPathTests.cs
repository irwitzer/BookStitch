using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public class ExportValidationServiceOutputPathTests
{
    [Theory]
    [InlineData("CON.m4b")]
    [InlineData("aux.book.m4b")]
    [InlineData("COM1.m4b")]
    [InlineData("LPT9.m4b")]
    [InlineData("COM¹.m4b")]
    [InlineData("book.m4b.")]
    [InlineData("book?.m4b")]
    public void Validate_rejects_invalid_actual_windows_destination(string name)
    {
        using var source = new TemporaryFolder();
        using var output = new TemporaryFolder();
        using var work = new TemporaryFolder();
        File.WriteAllText(Path.Combine(source.Path, "track.mp3"), "audio");
        var path = Path.Combine(output.Path, name);
        var result = new ExportValidationService().Validate([ValidTrack("track.mp3")], ReadyFfmpeg(),
            source.Path, output.Path, "valid.m4b", ".m4b", work.Path, path);
        Assert.Contains(LocalizationText.Format("Export.InvalidOutputPath", path), result.Errors);
    }

    [Fact]
    public void Validate_checks_directory_collision_nested_segments_and_total_length()
    {
        using var source = new TemporaryFolder();
        using var output = new TemporaryFolder();
        using var work = new TemporaryFolder();
        File.WriteAllText(Path.Combine(source.Path, "track.mp3"), "audio");
        var occupied = Path.Combine(output.Path, "book.m4b"); Directory.CreateDirectory(occupied);
        var paths = new[] { occupied, Path.Combine(output.Path, "NUL", "book.m4b"),
            Path.Combine(output.Path, new string('a', 256) + ".m4b"),
            Path.Combine(output.Path, new string('a', 100), new string('b', 100), "book.m4b") };
        foreach (var path in paths)
        {
            var result = new ExportValidationService().Validate([ValidTrack("track.mp3")], ReadyFfmpeg(),
                source.Path, output.Path, "valid.m4b", ".m4b", work.Path, path);
            Assert.Contains(LocalizationText.Format("Export.InvalidOutputPath", path), result.Errors);
        }
        Assert.Equal("_CON.m4b", FileNameTemplateService.BuildOutputFileName("CON", "A", "", "{Titel}", ".m4b"));
    }

    [Fact]
    public void Validate_WithFinalOutputPathOverride_UsesNestedOutputPath()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        System.IO.File.WriteAllText(System.IO.Path.Combine(sourceFolder.Path, "Track 01.mp3"), "audio");

        var finalOutputPath = System.IO.Path.Combine(
            outputFolder.Path,
            "Autor Name",
            "Buch Titel",
            "Autor Name - Buch Titel.m4b");

        var service = new ExportValidationService();
        var result = service.Validate(
            [ValidTrack("Track 01.mp3")],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Autor Name - Buch Titel.m4b",
            ".m4b",
            workFolder.Path,
            finalOutputPathOverride: finalOutputPath);

        Assert.Empty(result.Errors);
        Assert.Equal(finalOutputPath, result.OutputPath);
    }

    private static TrackInfo ValidTrack(string fileName)
    {
        return new TrackInfo
        {
            Index = 1,
            FileName = fileName,
            Extension = ".mp3",
            Codec = "MP3",
            ProcessingAction = "Konvertieren",
            ChapterTitle = "Kapitel 1"
        };
    }

    private static FfmpegToolStatus ReadyFfmpeg()
    {
        return new FfmpegToolStatus
        {
            FfmpegAvailable = true,
            FfmpegPath = @"C:\Tools\ffmpeg.exe",
            FfprobeAvailable = true,
            FfprobePath = @"C:\Tools\ffprobe.exe"
        };
    }
}
