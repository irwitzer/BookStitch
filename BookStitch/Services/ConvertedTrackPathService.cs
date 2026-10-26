using BookStitch.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BookStitch.Services;

public static class ConvertedTrackPathService
{
    public static string CreateShortHash(string? value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""));
        return Convert.ToHexString(bytes).Substring(0, 8).ToLowerInvariant();
    }

    public static string GetConvertedTrackPath(string convertedFolder, string sourcePath, TrackInfo track)
    {
        var baseName = Path.GetFileNameWithoutExtension(track.FileName);
        var safeBaseName = FileNameTemplateService.CleanWindowsFileName(baseName);

        if (string.IsNullOrWhiteSpace(safeBaseName))
            safeBaseName = "Track";

        var sourceKey = sourcePath;

        try
        {
            if (!string.IsNullOrWhiteSpace(sourcePath))
                sourceKey = Path.GetFullPath(sourcePath);
        }
        catch
        {
            sourceKey = sourcePath ?? "";
        }

        var stableId = CreateShortHash(sourceKey);
        return Path.Combine(convertedFolder, $"{safeBaseName}_{stableId}.m4a");
    }

    public static string[] GetConvertedTrackPaths(string convertedFolder, string sourceFolder, IReadOnlyList<TrackInfo> tracks)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return tracks.Select(track =>
        {
            var source = Path.GetFullPath(TrackPathService.GetTrackPath(sourceFolder, track));
            var target = GetConvertedTrackPath(convertedFolder, source, track);
            occurrences.TryGetValue(source, out var occurrence);
            occurrences[source] = ++occurrence;
            return occurrence == 1 ? target : Path.Combine(convertedFolder,
                Path.GetFileNameWithoutExtension(target) + $"_occurrence-{occurrence}.m4a");
        }).ToArray();
    }

    public static string GetMergePartFilePath(string projectFolder, string outputPath) =>
        Path.Combine(ProjectFolderLayout.GetMergeFolder(projectFolder), Path.GetFileName(GetPartFilePath(outputPath)));

    public static string GetPartFilePath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? "";
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);

        return Path.Combine(directory, nameWithoutExtension + ".part" + extension);
    }
}
