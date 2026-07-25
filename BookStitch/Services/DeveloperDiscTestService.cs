namespace BookStitch.Services;

public enum DeveloperDiscSimulationScenario
{
    EmptyDrive,
    UnsupportedDisc,
    DuplicateEjected,
    DuplicateManualEject,
    SlowThenReady,
    Ready
}

/// <summary>
/// Provides deterministic polling results for the developer-only disc wait dialog preview.
/// Production polling, drive access and pipeline code remain untouched.
/// </summary>
public sealed class DeveloperDiscTestService
{
    public DiscPollingResult CreateSimulationResult(
        DeveloperDiscSimulationScenario scenario,
        int checkNumber,
        int discNumber = 2,
        int totalDiscs = 3)
    {
        var normalizedCheckNumber = Math.Max(1, checkNumber);

        return scenario switch
        {
            DeveloperDiscSimulationScenario.EmptyDrive => new DiscPollingResult(
                false,
                $"Bitte CD {discNumber} von {totalDiscs} einlegen.\n\nBookStitch erkennt die eingelegte Disc automatisch und setzt anschließend fort.",
                $"Warte auf CD {discNumber}: Laufwerk noch nicht bereit.",
                $"Bitte CD {discNumber} einlegen. Laufwerk wird geprüft...",
                DiscPollingDisplayState.Waiting),

            DeveloperDiscSimulationScenario.UnsupportedDisc => new DiscPollingResult(
                false,
                $"Bitte CD {discNumber} von {totalDiscs} einlegen.\n\nNoch keine unterstützte MP3-CD / Daten-CD erkannt. BookStitch prüft automatisch weiter.",
                $"Warte auf CD {discNumber} von {totalDiscs}...",
                $"Bitte CD {discNumber} einlegen. Automatische Erkennung läuft...",
                DiscPollingDisplayState.Unsupported),

            DeveloperDiscSimulationScenario.DuplicateEjected => new DiscPollingResult(
                false,
                "Diese CD wurde bereits importiert und wieder ausgeworfen.\n\n" +
                $"Bitte CD {discNumber} von {totalDiscs} einlegen. BookStitch prüft automatisch weiter.",
                $"Warte auf CD {discNumber}: bereits importierte CD erkannt und ausgeworfen.",
                $"Bitte CD {discNumber} einlegen. Bereits importierte CD wurde ausgeworfen.",
                DiscPollingDisplayState.Duplicate),

            DeveloperDiscSimulationScenario.DuplicateManualEject => new DiscPollingResult(
                false,
                "Diese CD wurde bereits importiert. Bitte wirf sie manuell aus.\n\n" +
                $"Bitte CD {discNumber} von {totalDiscs} einlegen. BookStitch prüft automatisch weiter.",
                $"Warte auf CD {discNumber}: bereits importierte CD erkannt, manuelles Auswerfen nötig.",
                $"Bitte CD {discNumber} einlegen. Bereits importierte CD bitte auswerfen.",
                DiscPollingDisplayState.Duplicate),

            DeveloperDiscSimulationScenario.SlowThenReady when normalizedCheckNumber < 4 => new DiscPollingResult(
                false,
                $"Das Laufwerk antwortet langsam.\n\nBookStitch prüft CD {discNumber} von {totalDiscs} automatisch weiter.",
                $"Warte auf CD {discNumber}: Laufwerk antwortet langsam.",
                "Automatische Erkennung läuft...",
                DiscPollingDisplayState.Waiting),

            DeveloperDiscSimulationScenario.SlowThenReady or DeveloperDiscSimulationScenario.Ready => new DiscPollingResult(
                true,
                $"Neue MP3-CD erkannt. CD {discNumber} von {totalDiscs} wird vorbereitet...",
                $"CD {discNumber} von {totalDiscs} erkannt. Test erfolgreich.",
                $"CD {discNumber} erkannt. Im Testmodus wird kein Import gestartet.",
                DiscPollingDisplayState.Ready),

            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
    }
}
