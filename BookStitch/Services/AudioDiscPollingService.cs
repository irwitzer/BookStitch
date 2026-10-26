using System.IO;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed record AudioDiscPollingResult(
    DiscPollingResult PollingResult,
    AudioDiscInfo? Disc,
    DiscDriveInfo? DriveInfo);

public sealed class AudioDiscPollingService
{
    private readonly AudioDiscReaderService _audioDiscReaderService;
    private readonly DiscDriveService _discDriveService;

    public AudioDiscPollingService(
        AudioDiscReaderService audioDiscReaderService,
        DiscDriveService discDriveService)
    {
        _audioDiscReaderService = audioDiscReaderService;
        _discDriveService = discDriveService;
    }

    public Task<AudioDiscPollingResult> CheckRequiredDiscAsync(
        string expectedDiscIdentity,
        int discNumber,
        int totalDiscs,
        string? preferredDriveRoot = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? driveRoots = null,
        Func<string, string>? driveLabelFormatter = null, Action<string>? checkingDrive = null) =>
        _discDriveService.MeasureOperationAsync(
            string.Empty,
            "drive.audio-analysis",
            () => CheckRequiredDisc(expectedDiscIdentity, discNumber, totalDiscs, preferredDriveRoot, driveRoots, driveLabelFormatter, checkingDrive),
            cancellationToken);

    public AudioDiscPollingResult CheckRequiredDisc(
        string expectedDiscIdentity,
        int discNumber,
        int totalDiscs,
        string? preferredDriveRoot = null,
        IReadOnlyList<string>? driveRoots = null,
        Func<string, string>? driveLabelFormatter = null, Action<string>? checkingDrive = null)
    {
        AudioDiscPollingResult? preferredDriveProblem = null;

        foreach (var drive in OrderRequiredDiscDrives(_discDriveService.GetCdDrives(), driveRoots).Where(item => item.IsReady))
        {
            DiscDriveOperationService.CurrentCancellationToken.ThrowIfCancellationRequested();
            checkingDrive?.Invoke(drive.RootPath);
            var readResult = _audioDiscReaderService.ReadDisc(drive.RootPath);
            var isPreferredDrive = !string.IsNullOrWhiteSpace(preferredDriveRoot) &&
                string.Equals(
                    Path.GetPathRoot(drive.RootPath),
                    Path.GetPathRoot(preferredDriveRoot),
                    StringComparison.OrdinalIgnoreCase);

            if (!readResult.IsAudioDisc || readResult.Disc is null)
            {
                if (isPreferredDrive)
                {
                    var ejectedWrongType = _discDriveService.TryEjectDisc(drive.RootPath);
                    preferredDriveProblem ??= Create(false, null, drive,
                        readResult.IsMixedModeUnsupported ? readResult.ErrorMessage : FormatUnsupportedDiscMessage(ejectedWrongType, discNumber, totalDiscs),
                        LocalizationText.Format(
                            ejectedWrongType ? "Disc.Polling.Audio.Unsupported.Ejected.Status" : "Disc.Polling.Audio.Unsupported.ManualEject.Status",
                            discNumber),
                        LocalizationText.Format(
                            ejectedWrongType ? "Disc.Polling.Audio.Unsupported.Ejected.Progress" : "Disc.Polling.Audio.Unsupported.ManualEject.Progress",
                            discNumber),
                        DiscPollingDisplayState.Unsupported);
                }

                continue;
            }

            if (!string.Equals(readResult.Disc.DiscIdentity, expectedDiscIdentity, StringComparison.OrdinalIgnoreCase))
            {
                if (isPreferredDrive)
                {
                    var ejectedWrongDisc = _discDriveService.TryEjectDisc(drive.RootPath);
                    preferredDriveProblem ??= Create(false, readResult.Disc, drive,
                        FormatWrongRequiredDiscMessage(ejectedWrongDisc, discNumber, totalDiscs),
                        LocalizationText.Format(
                            ejectedWrongDisc ? "Disc.Polling.Audio.WrongRequired.Ejected.Status" : "Disc.Polling.Audio.WrongRequired.ManualEject.Status",
                            discNumber),
                        LocalizationText.Format(
                            ejectedWrongDisc ? "Disc.Polling.Audio.WrongRequired.Ejected.Progress" : "Disc.Polling.Audio.WrongRequired.ManualEject.Progress",
                            discNumber),
                        DiscPollingDisplayState.Unsupported);
                }

                continue;
            }

            var label = driveLabelFormatter?.Invoke(drive.RootPath) ?? drive.DriveLetter;
            return Create(true, readResult.Disc, drive,
                LocalizationText.Format("Disc.Polling.Audio.Required.Ready.Dialog", discNumber, totalDiscs, label),
                LocalizationText.Format("Disc.Polling.Audio.Ready.Status", discNumber, totalDiscs),
                LocalizationText.Format("Disc.Polling.Audio.Required.Ready.Progress", label));
        }

        return preferredDriveProblem ?? Create(false, null, null,
            LocalizationText.Format("Disc.Polling.Audio.Required.Waiting.Dialog", discNumber, totalDiscs),
            LocalizationText.Format("Disc.Polling.Audio.Waiting.Status", discNumber, totalDiscs),
            LocalizationText.Format("Disc.Polling.Audio.Required.Waiting.Progress", discNumber));
    }

    public static IReadOnlyList<DiscDriveInfo> OrderRequiredDiscDrives(
        IReadOnlyList<DiscDriveInfo> drives, IReadOnlyList<string>? driveRoots)
    {
        if (driveRoots is null)
            return drives;
        return driveRoots.Select(DiscDriveConfigurationService.NormalizeRootPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(root => drives.FirstOrDefault(drive => string.Equals(
                DiscDriveConfigurationService.NormalizeRootPath(drive.RootPath), root, StringComparison.OrdinalIgnoreCase)))
            .Where(drive => drive is not null).Select(drive => drive!).ToArray();
    }

    public Task<AudioDiscPollingResult> CheckNextDiscAsync(
        string sourceFolder,
        int discNumber,
        int totalDiscs,
        ISet<string> importedDiscIdentities,
        CancellationToken cancellationToken = default,
        bool acceptDuplicate = false) =>
        _discDriveService.MeasureOperationAsync(
            sourceFolder,
            "drive.audio-analysis",
            () => CheckNextDisc(sourceFolder, discNumber, totalDiscs, importedDiscIdentities, acceptDuplicate),
            cancellationToken);

    public AudioDiscPollingResult CheckNextDisc(
        string sourceFolder,
        int discNumber,
        int totalDiscs,
        ISet<string> importedDiscIdentities,
        bool acceptDuplicate = false)
    {
        if (!_discDriveService.IsDiscSourceReady(sourceFolder))
        {
            return Create(false, null, null,
                LocalizationText.Format("Disc.Polling.Audio.SourceNotReady.Dialog", discNumber, totalDiscs),
                LocalizationText.Format("Disc.Polling.Audio.SourceNotReady.Status", discNumber),
                LocalizationText.Format("Disc.Polling.Audio.SourceNotReady.Progress", discNumber));
        }

        var selectedMediaKind = _discDriveService.GetMediaKindForPath(sourceFolder);
        if (IsClearlyNotAudioDisc(selectedMediaKind))
        {
            var ejectedWrongType = _discDriveService.TryEjectDisc(sourceFolder);
            return Create(false, null, null,
                FormatUnsupportedDiscMessage(ejectedWrongType, discNumber, totalDiscs),
                LocalizationText.Format(
                    ejectedWrongType ? "Disc.Polling.Audio.Unsupported.Ejected.Status" : "Disc.Polling.Audio.Unsupported.ManualEject.Status",
                    discNumber),
                LocalizationText.Format(
                    ejectedWrongType ? "Disc.Polling.Audio.Unsupported.Ejected.Progress" : "Disc.Polling.Audio.Unsupported.ManualEject.Progress",
                    discNumber),
                DiscPollingDisplayState.Unsupported);
        }

        var readResult = _audioDiscReaderService.ReadDisc(sourceFolder);
        var duplicateDisc = !acceptDuplicate && readResult.Disc is not null && importedDiscIdentities.Contains(readResult.Disc.DiscIdentity);
        var ejected = duplicateDisc && _discDriveService.TryEjectDisc(sourceFolder);

        DiscDriveInfo? driveInfo = null;
        try
        {
            driveInfo = _discDriveService.GetDriveDiagnosticsForPath(sourceFolder);
        }
        catch
        {
            // Diagnoseinformationen sind optional.
        }

        return EvaluateReadResult(
            readResult,
            discNumber,
            totalDiscs,
            importedDiscIdentities,
            ejected,
            driveInfo,
            acceptDuplicate);
    }

    internal static string FormatUnsupportedDiscMessage(bool ejectedWrongType, int discNumber, int totalDiscs) =>
        LocalizationText.Format(
            ejectedWrongType ? "Disc.Polling.Audio.Unsupported.Ejected.Dialog" : "Disc.Polling.Audio.Unsupported.ManualEject.Dialog",
            discNumber,
            totalDiscs);

    internal static string FormatWrongRequiredDiscMessage(bool ejectedWrongDisc, int discNumber, int totalDiscs) =>
        LocalizationText.Format(
            ejectedWrongDisc ? "Disc.Polling.Audio.WrongRequired.Ejected.Dialog" : "Disc.Polling.Audio.WrongRequired.ManualEject.Dialog",
            discNumber,
            totalDiscs);

    public static bool IsClearlyNotAudioDisc(DiscMediaKind mediaKind) =>
        mediaKind is DiscMediaKind.Mp3Disc or DiscMediaKind.DataDisc;

    public static AudioDiscPollingResult EvaluateReadResult(
        AudioDiscReadResult readResult,
        int discNumber,
        int totalDiscs,
        ISet<string> importedDiscIdentities,
        bool duplicateDiscWasEjected,
        DiscDriveInfo? driveInfo = null,
        bool acceptDuplicate = false)
    {
        ArgumentNullException.ThrowIfNull(readResult);
        ArgumentNullException.ThrowIfNull(importedDiscIdentities);

        if (readResult.IsMixedModeUnsupported)
            return Create(false, null, driveInfo, readResult.ErrorMessage, readResult.ErrorMessage,
                readResult.ErrorMessage, DiscPollingDisplayState.Unsupported);

        if (!readResult.IsAudioDisc || readResult.Disc is null)
        {
            return Create(false, null, null,
                LocalizationText.Format("Disc.Polling.Audio.NoReadableDisc.Dialog", discNumber, totalDiscs),
                LocalizationText.Format("Disc.Polling.Audio.Waiting.Status", discNumber, totalDiscs),
                LocalizationText.Format("Disc.Polling.Audio.NoReadableDisc.Progress", discNumber));
        }

        var disc = readResult.Disc;
        if (importedDiscIdentities.Contains(disc.DiscIdentity) && !acceptDuplicate)
        {
            return Create(false, disc, null,
                LocalizationText.Format(
                    duplicateDiscWasEjected ? "Disc.Polling.Audio.Duplicate.Ejected.Dialog" : "Disc.Polling.Audio.Duplicate.ManualEject.Dialog",
                    discNumber,
                    totalDiscs),
                LocalizationText.Format(
                    duplicateDiscWasEjected ? "Disc.Polling.Audio.Duplicate.Ejected.Status" : "Disc.Polling.Audio.Duplicate.ManualEject.Status",
                    discNumber),
                LocalizationText.Format(
                    duplicateDiscWasEjected ? "Disc.Polling.Audio.Duplicate.Ejected.Progress" : "Disc.Polling.Audio.Duplicate.ManualEject.Progress",
                    discNumber),
                DiscPollingDisplayState.Duplicate);
        }

        return Create(true, disc, driveInfo,
            LocalizationText.Format("Disc.Polling.Audio.Next.Ready.Dialog", discNumber, totalDiscs),
            LocalizationText.Format("Disc.Polling.Audio.Ready.Status", discNumber, totalDiscs),
            LocalizationText.Format("Disc.Polling.Audio.Next.Ready.Progress", discNumber),
            DiscPollingDisplayState.Ready);
    }

    private static AudioDiscPollingResult Create(
        bool canImport,
        AudioDiscInfo? disc,
        DiscDriveInfo? driveInfo,
        string dialogText,
        string statusText,
        string progressText,
        DiscPollingDisplayState displayState = DiscPollingDisplayState.Waiting) =>
        new(new DiscPollingResult(canImport, dialogText, statusText, progressText, displayState), disc, driveInfo);
}
