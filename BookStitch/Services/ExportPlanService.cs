using BookStitch.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace BookStitch.Services;

public sealed class ExportPlanService
{
    public ExportPlan Create(ExportPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trackSnapshot = request.TrackSnapshot.ToList();
        var preset = ExportPreset.Parse(request.SelectedExportPreset);
        var totalDuration = GetTotalDuration(trackSnapshot);
        var totalTicks = Math.Max(totalDuration.Ticks, TimeSpan.FromSeconds(trackSnapshot.Count).Ticks);
        var parallelConversions = Math.Clamp(request.ParallelConversions, 1, 40);
        var projectType = string.IsNullOrWhiteSpace(request.ProjectType)
            ? ProjectManifestTypes.FolderProject
            : request.ProjectType;

        var projectWorkFolder = !string.IsNullOrWhiteSpace(request.ProjectWorkFolderOverride)
            ? request.ProjectWorkFolderOverride
            : Path.Combine(request.WorkingRootFolder, BuildProjectWorkFolderName(request.SourceFolder, request.Author, request.BookTitle));

        var presetFolder = preset.GetFolderName();
        ProjectFolderLayout.EnsureProjectFolders(projectWorkFolder);
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(projectWorkFolder, presetFolder);
        var mergeFolder = ProjectFolderLayout.GetMergeFolder(projectWorkFolder);
        var concatListPath = Path.Combine(mergeFolder, "concat-list.txt");
        var chapterMetadataPath = Path.Combine(mergeFolder, "chapters.ffmetadata");
        var finalPartPath = Path.Combine(mergeFolder, Path.GetFileName(ConvertedTrackPathService.GetPartFilePath(request.FinalOutputPath)));
        var manifestPath = string.Equals(projectType, ProjectManifestTypes.Mp3DiscProject, StringComparison.OrdinalIgnoreCase)
            ? ProjectFolderLayout.GetExportManifestPath(projectWorkFolder)
            : ProjectFolderLayout.GetWorkManifestPath(projectWorkFolder);
        var finalOutputFolder = Path.GetDirectoryName(request.FinalOutputPath) ?? string.Empty;
        var finalOutputFileName = Path.GetFileName(request.FinalOutputPath);

        return new ExportPlan(
            trackSnapshot,
            preset,
            totalDuration,
            totalTicks,
            parallelConversions,
            projectType,
            request.SourceFolder,
            request.WorkingRootFolder,
            projectWorkFolder,
            presetFolder,
            convertedFolder,
            mergeFolder,
            concatListPath,
            chapterMetadataPath,
            request.FinalOutputPath,
            finalOutputFolder,
            finalOutputFileName,
            finalPartPath,
            manifestPath);
    }

    public string BuildProjectWorkFolderName(string sourceFolder, string? author, string? bookTitle)
    {
        var titlePart = FileNameTemplateService.CleanWindowsFileName($"{author} - {bookTitle}");

        if (string.IsNullOrWhiteSpace(titlePart) ||
            titlePart == "-" ||
            titlePart.Equals("Autor - Titel", StringComparison.OrdinalIgnoreCase))
        {
            titlePart = string.IsNullOrWhiteSpace(sourceFolder)
                ? ""
                : FileNameTemplateService.CleanWindowsFileName(new DirectoryInfo(sourceFolder).Name);
        }

        if (string.IsNullOrWhiteSpace(titlePart))
            titlePart = "Projekt";

        var hash = ConvertedTrackPathService.CreateShortHash(sourceFolder ?? string.Empty);
        return $"{titlePart}_{hash}";
    }

    private static TimeSpan GetTotalDuration(IEnumerable<TrackInfo> tracks)
    {
        var count = 0;
        var totalTicks = 0L;

        foreach (var track in tracks)
        {
            count++;

            if (GetPreciseDuration(track) is { } duration)
                totalTicks += duration.Ticks;
        }

        return totalTicks <= 0
            ? TimeSpan.FromSeconds(count)
            : TimeSpan.FromTicks(totalTicks);
    }

    private static TimeSpan? GetPreciseDuration(TrackInfo track)
    {
        if (track.DurationTicks is > 0)
            return TimeSpan.FromTicks(track.DurationTicks.Value);

        return TryParseDuration(track.Duration);
    }

    private static TimeSpan? TryParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }

        if (parts.Length == 3 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }

        return null;
    }
}
