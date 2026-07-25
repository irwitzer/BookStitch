namespace BookStitch.Services;

public enum DiscPollingDisplayState
{
    Waiting,
    Unsupported,
    Duplicate,
    Ready
}

public static class DiscPollingDisplayStateRules
{
    public static bool ShouldKeepNoticeVisible(
        DiscPollingDisplayState displayedState,
        DiscPollingDisplayState nextState) =>
        (displayedState is DiscPollingDisplayState.Unsupported or DiscPollingDisplayState.Duplicate) &&
        nextState == DiscPollingDisplayState.Waiting;
}

public sealed record DiscPollingResult(
    bool CanImport,
    string DialogText,
    string StatusText,
    string ProgressText,
    DiscPollingDisplayState DisplayState = DiscPollingDisplayState.Waiting);

public sealed class Mp3DiscPollingService
{
    private readonly Mp3DiscImportService _mp3DiscImportService;
    private readonly DiscDriveService _discDriveService;

    public Mp3DiscPollingService(
        Mp3DiscImportService mp3DiscImportService,
        DiscDriveService discDriveService)
    {
        _mp3DiscImportService = mp3DiscImportService;
        _discDriveService = discDriveService;
    }

    public DiscPollingResult CheckDiscSourceForNextImport(
        string sourceFolder,
        int discNumber,
        int totalDiscs,
        ISet<string> importedDiscSignatures,
        string? expectedDiscSignature = null)
    {
        if (!_discDriveService.IsDiscSourceReady(sourceFolder))
        {
            return new DiscPollingResult(
                CanImport: false,
                DialogText: $"Bitte CD {discNumber} von {totalDiscs} einlegen.\n\nBookStitch erkennt die eingelegte Disc automatisch und setzt anschließend fort.",
                StatusText: $"Warte auf CD {discNumber}: Laufwerk noch nicht bereit.",
                ProgressText: $"Bitte CD {discNumber} einlegen. Laufwerk wird geprüft...",
                DisplayState: DiscPollingDisplayState.Waiting);
        }

        var analysis = _mp3DiscImportService.AnalyzeSource(sourceFolder);
        if (!analysis.IsSupportedDataDisc)
        {
            var ejected = _discDriveService.TryEjectDisc(sourceFolder);
            var ejectText = ejected
                ? "Die CD wurde wieder ausgeworfen."
                : "Bitte wirf die CD manuell aus.";

            return new DiscPollingResult(
                CanImport: false,
                DialogText: $"Keine MP3-CD erkannt. / {ejectText}\n\n" +
                            $"Bitte CD {discNumber} von {totalDiscs} einlegen. BookStitch prüft automatisch weiter.",
                StatusText: ejected
                    ? $"Warte auf CD {discNumber}: falscher Datenträgertyp erkannt und ausgeworfen."
                    : $"Warte auf CD {discNumber}: falscher Datenträgertyp erkannt, manuelles Auswerfen nötig.",
                ProgressText: ejected
                    ? $"Bitte CD {discNumber} einlegen. Falscher Datenträger wurde ausgeworfen."
                    : $"Bitte CD {discNumber} einlegen. Falschen Datenträger bitte auswerfen.",
                DisplayState: DiscPollingDisplayState.Unsupported);
        }

        var signature = _mp3DiscImportService.CreateDiscSignature(sourceFolder, analysis);
        if (!string.IsNullOrWhiteSpace(expectedDiscSignature) &&
            !string.Equals(signature, expectedDiscSignature, StringComparison.OrdinalIgnoreCase))
        {
            var ejected = _discDriveService.TryEjectDisc(sourceFolder);
            var ejectText = ejected
                ? "Die falsche MP3-CD wurde wieder ausgeworfen."
                : "Bitte wirf die falsche MP3-CD manuell aus.";

            return new DiscPollingResult(
                CanImport: false,
                DialogText: "Diese MP3-CD gehört nicht zum vorbereiteten Kurztest. " + ejectText + "\n\n" +
                            $"Bitte CD {discNumber} von {totalDiscs} einlegen. BookStitch prüft automatisch weiter.",
                StatusText: ejected
                    ? $"Warte auf CD {discNumber}: falsche MP3-CD erkannt und ausgeworfen."
                    : $"Warte auf CD {discNumber}: falsche MP3-CD erkannt, manuelles Auswerfen nötig.",
                ProgressText: ejected
                    ? $"Bitte CD {discNumber} einlegen. Falsche MP3-CD wurde ausgeworfen."
                    : $"Bitte CD {discNumber} einlegen. Falsche MP3-CD bitte auswerfen.",
                DisplayState: DiscPollingDisplayState.Unsupported);
        }

        if (importedDiscSignatures.Contains(signature))
        {
            var ejected = _discDriveService.TryEjectDisc(sourceFolder);
            var ejectText = ejected
                ? "Die bereits importierte CD wurde wieder ausgeworfen."
                : "Bitte wirf die bereits importierte CD manuell aus.";

            return new DiscPollingResult(
                CanImport: false,
                DialogText: "Diese CD wurde bereits importiert. " + ejectText + "\n\n" +
                            $"Bitte CD {discNumber} von {totalDiscs} einlegen. BookStitch prüft automatisch weiter.",
                StatusText: ejected
                    ? $"Warte auf CD {discNumber}: bereits importierte CD erkannt und ausgeworfen."
                    : $"Warte auf CD {discNumber}: bereits importierte CD erkannt, manuelles Auswerfen nötig.",
                ProgressText: ejected
                    ? $"Bitte CD {discNumber} einlegen. Bereits importierte CD wurde ausgeworfen."
                    : $"Bitte CD {discNumber} einlegen. Bereits importierte CD bitte auswerfen.",
                DisplayState: DiscPollingDisplayState.Duplicate);
        }

        return new DiscPollingResult(
            CanImport: true,
            DialogText: $"Neue MP3-CD erkannt. CD {discNumber} von {totalDiscs} wird vorbereitet...",
            StatusText: $"CD {discNumber} von {totalDiscs} erkannt. Import startet...",
            ProgressText: $"CD {discNumber} erkannt. Import startet...",
            DisplayState: DiscPollingDisplayState.Ready);
    }
}
