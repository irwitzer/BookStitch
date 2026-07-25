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

    public AudioDiscPollingResult CheckRequiredDisc(
        string expectedDiscIdentity,
        int discNumber,
        int totalDiscs,
        string? preferredDriveRoot = null)
    {
        AudioDiscPollingResult? preferredDriveProblem = null;

        foreach (var drive in _discDriveService.GetCdDrives().Where(item => item.IsReady))
        {
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
                        FormatUnsupportedDiscMessage(ejectedWrongType, discNumber, totalDiscs),
                        ejectedWrongType
                            ? $"Warte auf Audio-CD {discNumber}: falscher Datenträgertyp erkannt und ausgeworfen."
                            : $"Warte auf Audio-CD {discNumber}: falscher Datenträgertyp erkannt, manuelles Auswerfen nötig.",
                        ejectedWrongType
                            ? $"Bitte Audio-CD {discNumber} einlegen. Falscher Datenträger wurde ausgeworfen."
                            : $"Bitte Audio-CD {discNumber} einlegen. Falschen Datenträger bitte auswerfen.",
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
                        ejectedWrongDisc
                            ? $"Warte auf Audio-CD {discNumber}: falsche Audio-CD erkannt und ausgeworfen."
                            : $"Warte auf Audio-CD {discNumber}: falsche Audio-CD erkannt, manuelles Auswerfen nötig.",
                        ejectedWrongDisc
                            ? $"Bitte Audio-CD {discNumber} einlegen. Falsche Audio-CD wurde ausgeworfen."
                            : $"Bitte Audio-CD {discNumber} einlegen. Falsche Audio-CD bitte auswerfen.",
                        DiscPollingDisplayState.Unsupported);
                }

                continue;
            }

            return Create(true, readResult.Disc, drive,
                $"Audio-CD {discNumber} von {totalDiscs} wurde in Laufwerk {drive.DriveLetter} erkannt.",
                $"Audio-CD {discNumber} von {totalDiscs} erkannt. Ripping startet …",
                $"Benötigte Audio-CD in Laufwerk {drive.DriveLetter} erkannt.");
        }

        return preferredDriveProblem ?? Create(false, null, null,
            $"Bitte Audio-CD {discNumber} von {totalDiscs} in eines der optischen Laufwerke einlegen.\n\nBookStitch erkennt die benötigte Disc automatisch und setzt anschließend fort.",
            $"Warte auf Audio-CD {discNumber} von {totalDiscs} …",
            $"Bitte Audio-CD {discNumber} einlegen. Alle optischen Laufwerke werden geprüft …");
    }

    public AudioDiscPollingResult CheckNextDisc(
        string sourceFolder,
        int discNumber,
        int totalDiscs,
        ISet<string> importedDiscIdentities)
    {
        if (!_discDriveService.IsDiscSourceReady(sourceFolder))
        {
            return Create(false, null, null,
                $"Bitte Audio-CD {discNumber} von {totalDiscs} einlegen.\n\nBookStitch erkennt die eingelegte Disc automatisch und setzt anschließend fort.",
                $"Warte auf Audio-CD {discNumber}: Laufwerk noch nicht bereit.",
                $"Bitte Audio-CD {discNumber} einlegen. Laufwerk wird geprüft …");
        }

        var selectedMediaKind = _discDriveService.GetMediaKindForPath(sourceFolder);
        if (IsClearlyNotAudioDisc(selectedMediaKind))
        {
            var ejectedWrongType = _discDriveService.TryEjectDisc(sourceFolder);
            return Create(false, null, null,
                FormatUnsupportedDiscMessage(ejectedWrongType, discNumber, totalDiscs),
                ejectedWrongType
                    ? $"Warte auf Audio-CD {discNumber}: falscher Datenträgertyp erkannt und ausgeworfen."
                    : $"Warte auf Audio-CD {discNumber}: falscher Datenträgertyp erkannt, manuelles Auswerfen nötig.",
                ejectedWrongType
                    ? $"Bitte Audio-CD {discNumber} einlegen. Falscher Datenträger wurde ausgeworfen."
                    : $"Bitte Audio-CD {discNumber} einlegen. Falschen Datenträger bitte auswerfen.",
                DiscPollingDisplayState.Unsupported);
        }

        var readResult = _audioDiscReaderService.ReadDisc(sourceFolder);
        var duplicateDisc = readResult.Disc is not null && importedDiscIdentities.Contains(readResult.Disc.DiscIdentity);
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
            driveInfo);
    }

    internal static string FormatUnsupportedDiscMessage(bool ejectedWrongType, int discNumber, int totalDiscs)
    {
        var ejectText = ejectedWrongType
            ? "Die CD wurde wieder ausgeworfen."
            : "Bitte wirf die CD manuell aus.";

        return $"Keine Audio-CD erkannt. / {ejectText}\n\n" +
               $"Bitte Audio-CD {discNumber} von {totalDiscs} einlegen. BookStitch prüft automatisch weiter.";
    }

    internal static string FormatWrongRequiredDiscMessage(bool ejectedWrongDisc, int discNumber, int totalDiscs)
    {
        var ejectText = ejectedWrongDisc
            ? "Die falsche Audio-CD wurde wieder ausgeworfen."
            : "Bitte wirf die falsche Audio-CD manuell aus.";

        return $"Das ist nicht die benötigte Audio-CD. {ejectText}\n\n" +
               $"Bitte Audio-CD {discNumber} von {totalDiscs} einlegen. BookStitch prüft automatisch weiter.";
    }

    public static bool IsClearlyNotAudioDisc(DiscMediaKind mediaKind) =>
        mediaKind is DiscMediaKind.Mp3Disc or DiscMediaKind.DataDisc;

    public static AudioDiscPollingResult EvaluateReadResult(
        AudioDiscReadResult readResult,
        int discNumber,
        int totalDiscs,
        ISet<string> importedDiscIdentities,
        bool duplicateDiscWasEjected,
        DiscDriveInfo? driveInfo = null)
    {
        ArgumentNullException.ThrowIfNull(readResult);
        ArgumentNullException.ThrowIfNull(importedDiscIdentities);

        if (!readResult.IsAudioDisc || readResult.Disc is null)
        {
            return Create(false, null, null,
                $"Bitte Audio-CD {discNumber} von {totalDiscs} einlegen.\n\nNoch keine lesbare Audio-CD erkannt. BookStitch prüft automatisch weiter.",
                $"Warte auf Audio-CD {discNumber} von {totalDiscs} …",
                $"Bitte Audio-CD {discNumber} einlegen. Automatische Erkennung läuft …");
        }

        var disc = readResult.Disc;
        if (importedDiscIdentities.Contains(disc.DiscIdentity))
        {
            return Create(false, disc, null,
                "Diese Audio-CD wurde bereits aufgenommen. " +
                (duplicateDiscWasEjected ? "Die Audio-CD wurde wieder ausgeworfen." : "Bitte wirf die Audio-CD manuell aus.") +
                $"\n\nBitte Audio-CD {discNumber} von {totalDiscs} einlegen. BookStitch prüft automatisch weiter.",
                duplicateDiscWasEjected
                    ? $"Warte auf Audio-CD {discNumber}: bekannte Disc erkannt und ausgeworfen."
                    : $"Warte auf Audio-CD {discNumber}: bekannte Disc erkannt, manuelles Auswerfen nötig.",
                duplicateDiscWasEjected
                    ? $"Bitte Audio-CD {discNumber} einlegen. Bekannte Disc wurde ausgeworfen."
                    : $"Bitte Audio-CD {discNumber} einlegen. Bekannte Disc bitte auswerfen.",
                DiscPollingDisplayState.Duplicate);
        }

        return Create(true, disc, driveInfo,
            $"Neue Audio-CD erkannt. Disc {discNumber} von {totalDiscs} wird vorbereitet …",
            $"Audio-CD {discNumber} von {totalDiscs} erkannt. Ripping startet …",
            $"Audio-CD {discNumber} erkannt. Ripping startet …",
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
