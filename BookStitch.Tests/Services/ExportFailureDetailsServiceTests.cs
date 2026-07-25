using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class ExportFailureDetailsServiceTests
{
    private readonly ExportFailureDetailsService _service = new();

    [Fact]
    public void SplitMessageLines_ReturnsEmptyListForBlankMessage()
    {
        Assert.Empty(ExportFailureDetailsService.SplitMessageLines(null));
        Assert.Empty(ExportFailureDetailsService.SplitMessageLines(""));
        Assert.Empty(ExportFailureDetailsService.SplitMessageLines("   "));
    }

    [Fact]
    public void SplitMessageLines_SplitsTrimsAndRemovesEmptyLines()
    {
        var lines = ExportFailureDetailsService.SplitMessageLines(" Erste Zeile \r\n\r\n Zweite Zeile \n  ");

        Assert.Equal(["Erste Zeile", "Zweite Zeile"], lines);
    }

    [Fact]
    public void BuildExportFailureDetails_ForGeneralException_AddsGeneralErrorAndMessageLines()
    {
        var exception = new InvalidOperationException("Erste Zeile\nZweite Zeile");

        var details = _service.BuildExportFailureDetails(exception, "");

        Assert.Contains("• Allgemeiner Exportfehler", details);
        Assert.Contains("   ◦ Erste Zeile", details);
        Assert.Contains("   ◦ Zweite Zeile", details);
        Assert.DoesNotContain("Arbeitsordner:", details);
    }

    [Fact]
    public void BuildExportFailureDetails_AddsWorkFolderWhenPresent()
    {
        var exception = new InvalidOperationException("Fehler");

        var details = _service.BuildExportFailureDetails(exception, @"C:\Work\Converted");

        Assert.Contains("", details);
        Assert.Contains("Arbeitsordner:", details);
        Assert.Contains(@"C:\Work\Converted", details);
    }

    [Fact]
    public void BuildExportFailureDetails_LimitsGeneralExceptionMessageLines()
    {
        var message = string.Join("\n", Enumerable.Range(1, 30).Select(number => "Zeile " + number));
        var exception = new InvalidOperationException(message);

        var details = _service.BuildExportFailureDetails(exception, "");

        Assert.Equal(1 + 16, details.Count);
        Assert.Contains("   ◦ Zeile 1", details);
        Assert.Contains("   ◦ Zeile 16", details);
        Assert.DoesNotContain("   ◦ Zeile 17", details);
    }

    [Fact]
    public void BuildExportFailureDetails_ForTrackException_AddsTrackContext()
    {
        var inner = new InvalidOperationException("FFmpeg konnte nicht konvertieren.");
        var exception = new ExportTrackException(
            trackIndex: 3,
            fileName: "Track 03.mp3",
            sourcePath: @"C:\Source\Track 03.mp3",
            innerException: inner);

        var details = _service.BuildExportFailureDetails(exception, "");

        Assert.Contains("• #3  Track 03.mp3", details);
        Assert.Contains(details, line => line.Contains("FFmpeg konnte nicht konvertieren"));
        Assert.Contains(@"   ◦ Pfad: C:\Source\Track 03.mp3", details);
    }

    [Fact]
    public void BuildExportFailureDetails_ForTrackException_LimitsTechnicalLines()
    {
        var message = string.Join("\n", Enumerable.Range(1, 30).Select(number => "FFmpeg Zeile " + number));
        var inner = new InvalidOperationException(message);
        var exception = new ExportTrackException(
            trackIndex: 1,
            fileName: "Track 01.mp3",
            sourcePath: @"C:\Source\Track 01.mp3",
            innerException: inner);

        var details = _service.BuildExportFailureDetails(exception, "");

        var ffmpegLines = details.Where(line => line.Contains("◦ FFmpeg:")).ToList();

        Assert.True(ffmpegLines.Count <= 14);
    }
}
