namespace BookStitch.Services;

public enum ApplicationActivity
{
    None,
    Mp3DiscImport,
    AudioDiscProcessing,
    Export,
    BackgroundWork
}

public sealed record ApplicationShutdownPrompt(
    string Heading,
    string Message,
    string ProgressText);

public sealed class ApplicationShutdownService
{
    public ApplicationActivity GetActiveActivity(
        bool isDiscImporting,
        bool isAudioDiscProcessing,
        bool isExporting,
        bool isBusy,
        bool isPipelinePaused = false,
        bool hasPausedAudioDiscProject = false,
        bool hasPausedMp3DiscProject = false)
    {
        if (isDiscImporting)
        {
            return isAudioDiscProcessing
                ? ApplicationActivity.AudioDiscProcessing
                : ApplicationActivity.Mp3DiscImport;
        }

        if (isExporting)
            return ApplicationActivity.Export;

        if (isPipelinePaused)
        {
            if (hasPausedAudioDiscProject)
                return ApplicationActivity.AudioDiscProcessing;

            if (hasPausedMp3DiscProject)
                return ApplicationActivity.Mp3DiscImport;

            return ApplicationActivity.Export;
        }

        return isBusy
            ? ApplicationActivity.BackgroundWork
            : ApplicationActivity.None;
    }

    public ApplicationShutdownPrompt CreatePrompt(ApplicationActivity activity)
    {
        return activity switch
        {
            ApplicationActivity.Mp3DiscImport => new ApplicationShutdownPrompt(
                "MP3-CD-Import läuft noch",
                "Möchtest du den Import kontrolliert abbrechen und BookStitch schließen?\n\n" +
                "Bereits vollständig kopierte und vorbereitete Dateien bleiben erhalten.",
                "MP3-CD-Import wird beendet …"),

            ApplicationActivity.AudioDiscProcessing => new ApplicationShutdownPrompt(
                "Audio-CD-Verarbeitung läuft noch",
                "Möchtest du das Rippen und die begleitende Konvertierung kontrolliert abbrechen und BookStitch schließen?\n\n" +
                "Bereits vollständig gerippte und konvertierte Dateien bleiben erhalten.",
                "Audio-CD-Verarbeitung wird beendet …"),

            ApplicationActivity.Export => new ApplicationShutdownPrompt(
                "Export läuft noch",
                "Möchtest du den Export kontrolliert abbrechen und BookStitch schließen?\n\n" +
                "Bereits vorbereitete Projektdateien bleiben erhalten.",
                "Export wird beendet …"),

            ApplicationActivity.BackgroundWork => new ApplicationShutdownPrompt(
                "Vorgang läuft noch",
                "BookStitch arbeitet gerade noch. Möchtest du den Vorgang beenden und BookStitch schließen?",
                "Vorgang wird beendet …"),

            _ => new ApplicationShutdownPrompt(
                "BookStitch schließen",
                "Möchtest du BookStitch schließen?",
                "BookStitch wird beendet …")
        };
    }

    public async Task<bool> WaitForIdleAsync(
        Func<bool> isWorkActive,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(isWorkActive);

        if (!isWorkActive())
            return true;

        var startedUtc = DateTime.UtcNow;
        while (isWorkActive())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTime.UtcNow - startedUtc >= timeout)
                return false;

            await Task.Delay(100, cancellationToken);
        }

        return true;
    }
}
