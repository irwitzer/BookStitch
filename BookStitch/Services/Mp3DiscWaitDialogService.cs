using System.IO;
using System.Windows;

namespace BookStitch.Services;

public sealed class Mp3DiscWaitDialogService
{
    private readonly Mp3DiscPollingService _mp3DiscPollingService;
    private readonly IDiscWaitDialogService _discWaitDialogService;

    public Mp3DiscWaitDialogService(
        Mp3DiscPollingService mp3DiscPollingService,
        IDiscWaitDialogService? discWaitDialogService = null)
    {
        _mp3DiscPollingService = mp3DiscPollingService;
        _discWaitDialogService = discWaitDialogService ?? new DiscWaitDialogService();
    }

    public Task<DiscWaitDialogOutcome> WaitForNextDiscAsync(
        Window owner,
        string sourceFolder,
        int discNumber,
        int totalDiscs,
        ISet<string> importedDiscSignatures,
        Action<string> setStatusText,
        Action<string> setProgressText,
        CancellationToken token,
        Action<DiscPollingDisplayState>? notifyDisplayState = null,
        string? expectedDiscSignature = null)
    {
        var request = new DiscWaitDialogRequest(
            discNumber,
            totalDiscs,
            "MP3-CD",
            $"Bitte CD {discNumber} von {totalDiscs} einlegen. BookStitch prüft automatisch und startet, sobald eine neue unterstützte MP3-CD erkannt wurde.",
            "Bereits importierte CDs werden erkannt, nicht erneut importiert und nach Möglichkeit wieder ausgeworfen.",
            CreateDriveDisplayName(sourceFolder),
            notifyDisplayState);

        return _discWaitDialogService.WaitForDiscAsync(
            owner,
            request,
            cancellationToken => Task.Run(
                () => _mp3DiscPollingService.CheckDiscSourceForNextImport(
                    sourceFolder,
                    discNumber,
                    totalDiscs,
                    importedDiscSignatures,
                    expectedDiscSignature),
                cancellationToken),
            setStatusText,
            setProgressText,
            token);
    }

    private static string CreateDriveDisplayName(string sourceFolder)
    {
        var root = Path.GetPathRoot(sourceFolder)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar);
        return string.IsNullOrWhiteSpace(root) ? string.Empty : $"CD-Laufwerk {root}";
    }
}
