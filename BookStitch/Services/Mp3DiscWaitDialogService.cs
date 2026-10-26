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
        string? driveDisplayName = null,
        bool canCompleteImport = true)
    {
        var request = new DiscWaitDialogRequest(
            discNumber,
            totalDiscs,
            "MP3-CD",
            LocalizationText.Format("Disc.Wait.Mp3Instruction", discNumber, totalDiscs),
            LocalizationText.Get("Disc.Wait.Mp3Hint"),
            driveDisplayName ?? CreateDriveDisplayName(sourceFolder),
            notifyDisplayState,
            CanCompleteImport: canCompleteImport && discNumber > 1);

        return _discWaitDialogService.WaitForDiscAsync(
            owner,
            request,
            (checkRequest, cancellationToken) => _mp3DiscPollingService.CheckDiscSourceForNextImportAsync(
                sourceFolder,
                discNumber,
                totalDiscs,
                importedDiscSignatures,
                cancellationToken,
                checkRequest.AcceptNextDuplicate),
            setStatusText,
            setProgressText,
            token);
    }

    private static string CreateDriveDisplayName(string sourceFolder)
    {
        var root = Path.GetPathRoot(sourceFolder)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar);
        return string.IsNullOrWhiteSpace(root) ? string.Empty : LocalizationText.Format("Disc.Wait.CdDrive", root);
    }
}
