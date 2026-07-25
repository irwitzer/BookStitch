using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public class ExportValidationServiceTests
{
    private readonly ExportValidationService _service = new();

    [Fact]
    public void Validate_WithValidExportPlan_ReturnsNoErrors()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Track 01.mp3", "audio");

        var result = _service.Validate(
            [ValidTrack("Track 01.mp3")],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Single(result.TrackSnapshot);
        Assert.Equal(System.IO.Path.Combine(outputFolder.Path, "Test.m4b"), result.OutputPath);
    }

    [Fact]
    public void Validate_WithoutTracks_ReturnsBlockingError()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        var result = _service.Validate(
            [],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("noch keine Tracks"));
    }

    [Fact]
    public void Validate_WithoutFfmpeg_ReturnsBlockingError()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Track 01.mp3", "audio");

        var result = _service.Validate(
            [ValidTrack("Track 01.mp3")],
            new FfmpegToolStatus(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("FFmpeg ist noch nicht bereit"));
    }

    [Fact]
    public void Validate_WithMissingCurrentFolder_ReturnsBlockingError()
    {
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        var missingSourceFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = _service.Validate(
            [ValidTrack("Track 01.mp3")],
            ReadyFfmpeg(),
            missingSourceFolder,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("aktuelle Hörbuchordner wurde nicht gefunden"));
    }

    [Fact]
    public void Validate_WithoutOutputFolder_ReturnsBlockingError()
    {
        using var sourceFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Track 01.mp3", "audio");

        var result = _service.Validate(
            [ValidTrack("Track 01.mp3")],
            ReadyFfmpeg(),
            sourceFolder.Path,
            "",
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("kein Ausgabeordner gesetzt"));
    }

    [Fact]
    public void Validate_WithMissingOutputFolder_ReturnsBlockingError()
    {
        using var sourceFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Track 01.mp3", "audio");
        var missingOutputFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var result = _service.Validate(
            [ValidTrack("Track 01.mp3")],
            ReadyFfmpeg(),
            sourceFolder.Path,
            missingOutputFolder,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("Ausgabeordner wurde nicht gefunden"));
    }

    [Fact]
    public void Validate_WithInvalidOutputExtension_ReturnsBlockingError()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Track 01.mp3", "audio");

        var result = _service.Validate(
            [ValidTrack("Track 01.mp3")],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.mp3",
            ".mp3",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("Ausgabeendung muss .m4a oder .m4b sein"));
    }

    [Fact]
    public void Validate_WithMissingSourceFile_ReturnsBlockingError()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        var result = _service.Validate(
            [ValidTrack("Track 01.mp3")],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("Datei wurde nicht gefunden"));
    }

    [Fact]
    public void Validate_WithZeroByteSourceFile_ReturnsBlockingError()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        sourceFolder.CreateFile("Track 01.mp3");

        var result = _service.Validate(
            [ValidTrack("Track 01.mp3")],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("Datei ist leer"));
    }

    [Fact]
    public void Validate_WhenSourceEqualsOutput_ReturnsBlockingError()
    {
        using var sourceFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Test.m4b", "audio");

        var result = _service.Validate(
            [ValidTrack("Test.m4b", ".m4b", "AAC", "Übernehmen")],
            ReadyFfmpeg(),
            sourceFolder.Path,
            sourceFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("Quelldatei wäre gleichzeitig die Ausgabedatei"));
    }

    [Fact]
    public void Validate_WithInvalidAudioTracks_ReturnsAllBlockingErrors()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Fake 01.flac", "not audio");
        WriteFile(sourceFolder, "Fake 02.aac", "not audio");

        var first = ValidTrack("Fake 01.flac", ".flac", "Ungültig", "Ungültig", index: 7);
        first.AudioValidationPassed = false;
        var second = ValidTrack("Fake 02.aac", ".aac", "Ungültig", "Ungültig", index: 8);
        second.AudioValidationPassed = false;

        var result = _service.Validate(
            [first, second],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("#7") && error.Contains("Fake 01.flac"));
        Assert.Contains(result.Errors, error => error.Contains("#8") && error.Contains("Fake 02.aac"));
        Assert.DoesNotContain(result.Errors, error => error.Contains("Audioformat konnte nicht sicher verarbeitet"));
    }

    [Fact]
    public void Validate_WithUnsupportedTrack_ReturnsBlockingError()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Track 01.wma", "audio");

        var result = _service.Validate(
            [ValidTrack("Track 01.wma", ".wma", "WMA", "Prüfen")],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Contains(result.Errors, error => error.Contains("Audioformat konnte nicht sicher verarbeitet werden"));
    }

    [Fact]
    public void Validate_WithDuplicateSourceFiles_ReturnsWarning()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Track 01.mp3", "audio");

        var tracks = new[]
        {
            ValidTrack("Track 01.mp3", index: 1),
            ValidTrack("Track 01.mp3", index: 2)
        };

        var result = _service.Validate(
            tracks,
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, warning => warning.Contains("Dieselbe Quelldatei kommt mehrfach vor"));
    }

    [Fact]
    public void Validate_WithEmptyChapterTitle_ReturnsWarning()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Track 01.mp3", "audio");

        var track = ValidTrack("Track 01.mp3");
        track.ChapterTitle = "";

        var result = _service.Validate(
            [track],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, warning => warning.Contains("Kapitelvorschlag ist leer"));
    }

    [Fact]
    public void Validate_WithDuplicateChapterTitles_ReturnsWarning()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "Track 01.mp3", "audio");
        WriteFile(sourceFolder, "Track 02.mp3", "audio");

        var tracks = new[]
        {
            ValidTrack("Track 01.mp3", index: 1, chapterTitle: "Kapitel"),
            ValidTrack("Track 02.mp3", index: 2, chapterTitle: "kapitel")
        };

        var result = _service.Validate(
            tracks,
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, warning => warning.Contains("Doppelter Kapitelvorschlag"));
    }

    [Fact]
    public void Validate_UsesRelativeFolderWhenCheckingSourceFile()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, System.IO.Path.Combine("CD 01", "Track 01.mp3"), "audio");

        var track = ValidTrack("Track 01.mp3");
        track.RelativeFolder = "CD 01";

        var result = _service.Validate(
            [track],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_UsesAbsoluteFilePathFromResumeTrackWhenCheckingDuplicates()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        var firstPath = System.IO.Path.Combine(sourceFolder.Path, "001_Track.mp3");
        var secondPath = System.IO.Path.Combine(sourceFolder.Path, "002_Track.mp3");
        System.IO.File.WriteAllText(firstPath, "audio");
        System.IO.File.WriteAllText(secondPath, "audio");

        var first = ValidTrack("001_Track.mp3", index: 1, chapterTitle: "001 Kapitel");
        first.FilePath = firstPath;
        var second = ValidTrack("001_Track.mp3", index: 2, chapterTitle: "002 Kapitel");
        second.FilePath = secondPath;

        var result = _service.Validate(
            [first, second],
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Dieselbe Quelldatei kommt mehrfach vor"));
    }

    [Fact]
    public void Validate_IgnoresDuplicateChapterWarningsForNumberedChapterTitles()
    {
        using var sourceFolder = new TemporaryFolder();
        using var outputFolder = new TemporaryFolder();
        using var workFolder = new TemporaryFolder();

        WriteFile(sourceFolder, "076_Track_A.mp3", "audio");
        WriteFile(sourceFolder, "076_Track_B.mp3", "audio");

        var tracks = new[]
        {
            ValidTrack("076_Track_A.mp3", index: 74, chapterTitle: "076 Die Waechter"),
            ValidTrack("076_Track_B.mp3", index: 75, chapterTitle: "076 Die Waechter")
        };

        var result = _service.Validate(
            tracks,
            ReadyFfmpeg(),
            sourceFolder.Path,
            outputFolder.Path,
            "Test.m4b",
            ".m4b",
            workFolder.Path);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Doppelter Kapitelvorschlag"));
    }

    private static TrackInfo ValidTrack(
        string fileName,
        string extension = ".mp3",
        string codec = "MP3",
        string processingAction = "Konvertieren",
        int index = 1,
        string chapterTitle = "Kapitel 1")
    {
        return new TrackInfo
        {
            Index = index,
            FileName = fileName,
            Extension = extension,
            Codec = codec,
            ProcessingAction = processingAction,
            ChapterTitle = chapterTitle
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

    private static void WriteFile(TemporaryFolder folder, string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(folder.Path, relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
            System.IO.Directory.CreateDirectory(directory);

        System.IO.File.WriteAllText(fullPath, content);
    }
}
