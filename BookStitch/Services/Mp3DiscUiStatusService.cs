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
            StatusText: string.Format(LocalizationText.Get("MainWindow.Status.Mp3AnalysisFailed"), discNumber),
            ExportProgressText: string.Format(LocalizationText.Get("MainWindow.Status.Mp3DiscUnreadable"), discNumber));
    }

    public Mp3DiscUiStatus CreateAlreadyImported(int discNumber, int totalDiscs)
    {
        return new Mp3DiscUiStatus(
            StatusText: string.Format(LocalizationText.Get("MainWindow.Status.Mp3AlreadyImported"), discNumber),
            ExportProgressText: string.Format(LocalizationText.Get("MainWindow.Status.InsertMp3Disc"), discNumber, totalDiscs));
    }

    public Mp3DiscUiStatus CreateImportFailed()
    {
        return new Mp3DiscUiStatus(
            StatusText: LocalizationText.Get("MainWindow.Status.Mp3ImportFailed"),
            ExportProgressText: LocalizationText.Get("MainWindow.Status.Mp3ImportFailed"));
    }
}
