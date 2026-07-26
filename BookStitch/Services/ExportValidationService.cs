using BookStitch.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BookStitch.Services;

public sealed class ExportValidationService
{
    public ExportCheckResult Validate(
        IEnumerable<TrackInfo> tracks,
        FfmpegToolStatus ffmpegStatus,
        string currentFolderPath,
        string outputFolder,
        string outputFileNamePreview,
        string outputExtension,
        string workingRootFolder,
        string? finalOutputPathOverride = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var trackSnapshot = tracks.Where(track => !track.IsExcluded).ToList();

        var outputPath = !string.IsNullOrWhiteSpace(finalOutputPathOverride)
            ? finalOutputPathOverride
            : string.IsNullOrWhiteSpace(outputFolder)
                ? ""
                : Path.Combine(outputFolder, outputFileNamePreview);

        if (trackSnapshot.Count == 0)
            errors.Add("• Es sind noch keine Tracks geladen.");

        if (!ffmpegStatus.FfmpegAvailable || string.IsNullOrWhiteSpace(ffmpegStatus.FfmpegPath))
            errors.Add("• FFmpeg ist noch nicht bereit. Bitte richte FFmpeg zuerst ein.");

        if (string.IsNullOrWhiteSpace(currentFolderPath) || !Directory.Exists(currentFolderPath))
            errors.Add("• Der aktuelle Hörbuchordner wurde nicht gefunden. Bitte wähle den Ordner erneut aus.");

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            errors.Add("• Es ist kein Ausgabeordner gesetzt.");
        }
        else if (!Directory.Exists(outputFolder))
        {
            errors.Add("• Der Ausgabeordner wurde nicht gefunden: " + outputFolder);
        }
        else if (!CanWriteToFolder(outputFolder, out var outputFolderError))
        {
            errors.Add("• Der Ausgabeordner ist nicht beschreibbar: " + outputFolderError);
        }

        if (string.IsNullOrWhiteSpace(outputFileNamePreview))
            errors.Add("• Der Ausgabedateiname ist leer.");

        if (!outputExtension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) &&
            !outputExtension.Equals(".m4b", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("• Die Ausgabeendung muss .m4a oder .m4b sein.");
        }

        try
        {
            if (!CanWriteToFolder(workingRootFolder, out var workingFolderError))
                errors.Add("• Der Arbeitsordner ist nicht beschreibbar: " + workingFolderError);
        }
        catch (Exception ex)
        {
            errors.Add("• Der Arbeitsordner konnte nicht vorbereitet werden: " + ex.Message);
        }

        if (!string.IsNullOrWhiteSpace(currentFolderPath) && Directory.Exists(currentFolderPath))
        {
            ValidateTracksForExport(trackSnapshot, currentFolderPath, outputPath, errors, warnings);
        }

        return new ExportCheckResult(trackSnapshot, outputPath, errors, warnings);
    }

    private static void ValidateTracksForExport(
        List<TrackInfo> trackSnapshot,
        string currentFolderPath,
        string outputPath,
        List<string> errors,
        List<string> warnings)
    {
        var invalidAudioTracks = trackSnapshot
            .Where(IsInvalidAudioTrack)
            .ToList();

        foreach (var track in invalidAudioTracks.Take(20))
            errors.Add($"• #{track.Index}  Keine gültige Audiodatei: {track.FileName}");

        if (invalidAudioTracks.Count > 20)
            errors.Add($"• ... und {invalidAudioTracks.Count - 20} weitere ungültige Datei(en).");

        var unsupportedTracks = trackSnapshot
            .Where(track => !IsInvalidAudioTrack(track))
            .Where(track => !IsTrackSupportedForExport(track))
            .ToList();

        foreach (var track in unsupportedTracks.Take(10))
        {
            errors.Add(
                $"• #{track.Index}  Audioformat konnte nicht sicher verarbeitet werden " +
                $"({FormatTrackTechnicalSummary(track)}).");
        }

        if (unsupportedTracks.Count > 10)
            errors.Add($"• ... und {unsupportedTracks.Count - 10} weitere Datei(en), die noch nicht exportiert werden können.");

        var sourcePaths = trackSnapshot
            .Select(track => new
            {
                Track = track,
                Path = TrackPathService.GetTrackPath(currentFolderPath, track)
            })
            .ToList();

        var missingFiles = sourcePaths
            .Where(item => !File.Exists(item.Path))
            .ToList();

        foreach (var item in missingFiles.Take(10))
            errors.Add($"• #{item.Track.Index}  Datei wurde nicht gefunden: {item.Track.FileName}");

        if (missingFiles.Count > 10)
            errors.Add($"• ... und {missingFiles.Count - 10} weitere fehlende Quelldatei(en).");

        var zeroByteFiles = sourcePaths
            .Where(item => File.Exists(item.Path) && new FileInfo(item.Path).Length == 0)
            .ToList();

        foreach (var item in zeroByteFiles.Take(10))
            errors.Add($"• #{item.Track.Index}  Datei ist leer: {item.Track.FileName}");

        if (zeroByteFiles.Count > 10)
            errors.Add($"• ... und {zeroByteFiles.Count - 10} weitere 0-Byte-Datei(en).");

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var sourceEqualsOutput = sourcePaths
                .Where(item => TrackPathService.PathEquals(item.Path, outputPath))
                .ToList();

            foreach (var item in sourceEqualsOutput.Take(5))
                errors.Add($"• #{item.Track.Index}  Quelldatei wäre gleichzeitig die Ausgabedatei.");
        }

        var duplicateSources = sourcePaths
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Take(8)
            .ToList();

        foreach (var group in duplicateSources)
        {
            var indexes = FormatTrackIndexes(group.Select(item => item.Track.Index));
            warnings.Add($"• {indexes}  Dieselbe Quelldatei kommt mehrfach vor.");
        }

        var emptyChapterTitles = trackSnapshot
            .Where(track => string.IsNullOrWhiteSpace(track.ChapterTitle))
            .Take(8)
            .ToList();

        foreach (var track in emptyChapterTitles)
            warnings.Add($"• #{track.Index}  Kapitelvorschlag ist leer.");

    }


    private static bool IsInvalidAudioTrack(TrackInfo track)
    {
        return track.AudioValidationPassed == false ||
               string.Equals(
                   AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction),
                   "Ungültig",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrackSupportedForExport(TrackInfo track)
    {
        var action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction);
        var codec = (track.Codec ?? "").Trim();
        var extension = (track.Extension ?? "").Trim().TrimStart('.');

        if (action is "Übernehmen" or "Konvertieren")
            return true;

        return codec.Equals("MP3", StringComparison.OrdinalIgnoreCase) ||
               codec.Equals("FLAC", StringComparison.OrdinalIgnoreCase) ||
               AudioProcessingService.IsPcmCodec(codec) ||
               extension.Equals("MP3", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals("M4A", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals("M4B", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals("AAC", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals("WAV", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals("FLAC", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTrackTechnicalSummary(TrackInfo track)
    {
        var codec = string.IsNullOrWhiteSpace(track.Codec) ? "Codec unbekannt" : track.Codec;
        var extension = string.IsNullOrWhiteSpace(track.Extension) ? "Typ unbekannt" : track.Extension;
        var action = string.IsNullOrWhiteSpace(track.ProcessingAction) ? "Aktion offen" : track.ProcessingAction;

        return $"{extension}, {codec}, {action}";
    }

    private static string FormatTrackIndexes(IEnumerable<int> indexes)
    {
        return string.Join(", ", indexes.Select(index => "#" + index.ToString(CultureInfo.InvariantCulture)));
    }

    private static bool CanWriteToFolder(string folderPath, out string error)
    {
        error = "";

        try
        {
            Directory.CreateDirectory(folderPath);
            var testPath = Path.Combine(folderPath, ".bookstitch-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(testPath, "test", Encoding.UTF8);
            File.Delete(testPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
