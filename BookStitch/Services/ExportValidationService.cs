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
        string? finalOutputPathOverride = null,
        string? chapterMode = null)
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
            errors.Add(LocalizationText.Get("ExportNoTracks"));

        if (!ffmpegStatus.FfmpegAvailable || string.IsNullOrWhiteSpace(ffmpegStatus.FfmpegPath))
            errors.Add(LocalizationText.Get("ExportFfmpegMissing"));

        if (!ffmpegStatus.FfprobeAvailable || string.IsNullOrWhiteSpace(ffmpegStatus.FfprobePath))
            errors.Add(LocalizationText.Get("Export.FfprobeMissing"));

        if (!string.IsNullOrWhiteSpace(outputPath) && !WindowsOutputPathService.IsValid(outputPath))
            errors.Add(LocalizationText.Format("Export.InvalidOutputPath", outputPath));

        if (string.IsNullOrWhiteSpace(currentFolderPath) || !Directory.Exists(currentFolderPath))
            errors.Add(LocalizationText.Get("ExportSourceFolderMissing"));

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            errors.Add(LocalizationText.Get("ExportOutputFolderUnset"));
        }
        else if (!Directory.Exists(outputFolder))
        {
            errors.Add(string.Format(LocalizationText.Get("ExportOutputFolderMissing"), outputFolder));
        }
        else if (!CanWriteToFolder(outputFolder, out var outputFolderError))
        {
            errors.Add(string.Format(LocalizationText.Get("ExportOutputFolderNotWritable"), outputFolderError));
        }

        if (string.IsNullOrWhiteSpace(outputFileNamePreview))
            errors.Add(LocalizationText.Get("ExportFileNameEmpty"));

        if (!outputExtension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) &&
            !outputExtension.Equals(".m4b", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(LocalizationText.Get("ExportExtensionInvalid"));
        }

        try
        {
            if (!CanWriteToFolder(workingRootFolder, out var workingFolderError))
                errors.Add(string.Format(LocalizationText.Get("ExportWorkFolderNotWritable"), workingFolderError));
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(LocalizationText.Get("ExportWorkFolderPreparationFailed"), ex.Message));
        }

        if (!string.IsNullOrWhiteSpace(currentFolderPath) && Directory.Exists(currentFolderPath))
        {
            ValidateTracksForExport(trackSnapshot, currentFolderPath, outputPath, errors, warnings, chapterMode);
        }

        return new ExportCheckResult(trackSnapshot, outputPath, errors, warnings);
    }

    private static void ValidateTracksForExport(
        List<TrackInfo> trackSnapshot,
        string currentFolderPath,
        string outputPath,
        List<string> errors,
        List<string> warnings,
        string? chapterMode)
    {
        var invalidAudioTracks = trackSnapshot
            .Where(IsInvalidAudioTrack)
            .ToList();

        foreach (var track in invalidAudioTracks.Take(20))
            errors.Add(string.Format(LocalizationText.Get("ExportInvalidAudioTrack"), track.Index, track.FileName));

        if (invalidAudioTracks.Count > 20)
            errors.Add(string.Format(LocalizationText.Get("ExportMoreInvalidAudio"), invalidAudioTracks.Count - 20));

        var unsupportedTracks = trackSnapshot
            .Where(track => !IsInvalidAudioTrack(track))
            .Where(track => !IsTrackSupportedForExport(track))
            .ToList();

        foreach (var track in unsupportedTracks.Take(10))
        {
            errors.Add(string.Format(LocalizationText.Get("ExportUnsupportedTrack"),
                track.Index, FormatTrackTechnicalSummary(track)));
        }

        if (unsupportedTracks.Count > 10)
            errors.Add(string.Format(LocalizationText.Get("ExportMoreUnsupported"), unsupportedTracks.Count - 10));

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
            errors.Add(string.Format(LocalizationText.Get("ExportMissingFile"), item.Track.Index, item.Track.FileName));

        if (missingFiles.Count > 10)
            errors.Add(string.Format(LocalizationText.Get("ExportMoreMissingFiles"), missingFiles.Count - 10));

        var zeroByteFiles = sourcePaths
            .Where(item => File.Exists(item.Path) && new FileInfo(item.Path).Length == 0)
            .ToList();

        foreach (var item in zeroByteFiles.Take(10))
            errors.Add(string.Format(LocalizationText.Get("ExportEmptyFile"), item.Track.Index, item.Track.FileName));

        if (zeroByteFiles.Count > 10)
            errors.Add(string.Format(LocalizationText.Get("ExportMoreEmptyFiles"), zeroByteFiles.Count - 10));

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var sourceEqualsOutput = sourcePaths
                .Where(item => TrackPathService.PathEquals(item.Path, outputPath))
                .ToList();

            foreach (var item in sourceEqualsOutput.Take(5))
                errors.Add(string.Format(LocalizationText.Get("ExportSourceEqualsOutput"), item.Track.Index));
        }

        var duplicateSources = sourcePaths
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Take(8)
            .ToList();

        foreach (var group in duplicateSources)
        {
            var indexes = FormatTrackIndexes(group.Select(item => item.Track.Index));
            warnings.Add(string.Format(LocalizationText.Get("ExportDuplicateSource"), indexes));
        }

        if (ChapterSettingsService.NormalizeMode(chapterMode) == ChapterSettingsService.ModeNone) return;

        var emptyChapterTitles = new ChapterTimelineService()
            .BuildTimeline(trackSnapshot)
            .Where(chapter => chapter.IsSynthetic)
            .Select(chapter => chapter.SourceTrack)
            .Where(track => string.IsNullOrWhiteSpace(track.ChapterTitle))
            .Distinct()
            .Take(8)
            .ToList();

        foreach (var track in emptyChapterTitles)
            warnings.Add(string.Format(LocalizationText.Get("ExportEmptyChapterProposal"), track.Index));

    }


    private static bool IsInvalidAudioTrack(TrackInfo track)
    {
        return track.AudioValidationPassed == false ||
               track.WarningCodes.HasFlag(TrackWarningCodes.InvalidAudio) ||
               string.Equals(
                   AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction),
                   ProcessingActionIds.Invalid,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrackSupportedForExport(TrackInfo track)
    {
        var action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction);

        return action is ProcessingActionIds.Reuse or ProcessingActionIds.Convert;
    }

    private static string FormatTrackTechnicalSummary(TrackInfo track)
    {
        var codec = string.IsNullOrWhiteSpace(track.Codec) ? LocalizationText.Get("CodecUnknown") : track.DisplayCodec;
        var extension = string.IsNullOrWhiteSpace(track.Extension) ? LocalizationText.Get("TypeUnknown") : track.Extension;
        var action = string.IsNullOrWhiteSpace(track.ProcessingAction)
            ? LocalizationText.Get("ActionOpen")
            : ProcessingActionPresentation.Format(track.ProcessingAction);

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
