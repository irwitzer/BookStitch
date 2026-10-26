namespace BookStitch.Services;

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
                LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Dialog", discNumber, totalDiscs),
                LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Status", discNumber),
                LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Progress", discNumber),
                DiscPollingDisplayState.Waiting),
            DeveloperDiscSimulationScenario.UnsupportedDisc => new DiscPollingResult(
                false,
                LocalizationText.Format("Disc.Wait.Mp3Instruction", discNumber, totalDiscs),
                LocalizationText.Format("Disc.Polling.Mp3.Unsupported.ManualEject.Status", discNumber),
                LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Progress", discNumber),
                DiscPollingDisplayState.Unsupported),
            DeveloperDiscSimulationScenario.DuplicateEjected => new DiscPollingResult(
                false,
                LocalizationText.Format("Disc.Polling.Mp3.Duplicate.Ejected.Dialog", discNumber, totalDiscs),
                LocalizationText.Format("Disc.Polling.Mp3.Duplicate.Ejected.Status", discNumber),
                LocalizationText.Format("Disc.Polling.Mp3.Duplicate.Ejected.Progress", discNumber),
                DiscPollingDisplayState.Duplicate),
            DeveloperDiscSimulationScenario.DuplicateManualEject => new DiscPollingResult(
                false,
                LocalizationText.Format("Disc.Polling.Mp3.Duplicate.ManualEject.Dialog", discNumber, totalDiscs),
                LocalizationText.Format("Disc.Polling.Mp3.Duplicate.ManualEject.Status", discNumber),
                LocalizationText.Format("Disc.Polling.Mp3.Duplicate.ManualEject.Progress", discNumber),
                DiscPollingDisplayState.Duplicate),
            DeveloperDiscSimulationScenario.SlowThenReady when normalizedCheckNumber < 4 => new DiscPollingResult(
                false,
                LocalizationText.Get("MainWindow.DiscAnalysis.Slow"),
                LocalizationText.Get("MainWindow.DiscAnalysis.Slow"),
                LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Progress", discNumber),
                DiscPollingDisplayState.Waiting),
            DeveloperDiscSimulationScenario.SlowThenReady or DeveloperDiscSimulationScenario.Ready => new DiscPollingResult(
                true,
                LocalizationText.Format("Disc.Polling.Mp3.Ready.Dialog", discNumber, totalDiscs),
                LocalizationText.Format("Developer.SimulationReadyStatus", discNumber, totalDiscs),
                LocalizationText.Format("Developer.SimulationReadyProgress", discNumber),
                DiscPollingDisplayState.Ready),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
    }
}
