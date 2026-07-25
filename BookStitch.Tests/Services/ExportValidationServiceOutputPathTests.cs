using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public class ExportValidationServiceOutputPathTests
{
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
