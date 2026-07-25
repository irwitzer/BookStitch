namespace BookStitch.Services;

public sealed record Mp3DiscUiStatus(
    string StatusText,
    string ExportProgressText,
    double? ExportProgressPercent = null);

public sealed class Mp3DiscUiStatusService
{
    public Mp3DiscUiStatus CreateAnalysisFailed(int discNumber)
    {
        return new Mp3DiscUiStatus(
            StatusText: $"MP3-CD-Analyse für CD {discNumber} fehlgeschlagen.",
            ExportProgressText: $"CD {discNumber} wurde nicht gelesen.");
    }

    public Mp3DiscUiStatus CreateAlreadyImported(int discNumber, int totalDiscs)
    {
        return new Mp3DiscUiStatus(
            StatusText: $"CD {discNumber} nicht importiert: bereits importierte CD erkannt.",
            ExportProgressText: $"Bitte CD {discNumber} von {totalDiscs} einlegen.");
    }

    public Mp3DiscUiStatus CreateImportFailed()
    {
        return new Mp3DiscUiStatus(
            StatusText: "MP3-CD-Import fehlgeschlagen.",
            ExportProgressText: "MP3-CD-Import fehlgeschlagen.");
    }
}
