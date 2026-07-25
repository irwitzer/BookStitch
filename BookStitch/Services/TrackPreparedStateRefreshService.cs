using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class TrackPreparedStateRefreshService
{
    private readonly ProjectResumePlanService _projectResumePlanService;

    public TrackPreparedStateRefreshService()
        : this(new ProjectResumePlanService())
    {
    }

    public TrackPreparedStateRefreshService(ProjectResumePlanService projectResumePlanService)
    {
        _projectResumePlanService = projectResumePlanService;
    }

    public void RefreshForCurrentPreset(
        IEnumerable<TrackInfo> tracks,
        string? projectWorkFolder,
        string? selectedPreset)
    {
        if (string.IsNullOrWhiteSpace(projectWorkFolder) ||
            !Directory.Exists(projectWorkFolder))
        {
            return;
        }

        var plan = _projectResumePlanService.BuildFromProjectFolder(
            projectWorkFolder,
            selectedPreset);
        if (plan is null)
            return;

        var preparedBySourcePath = plan.Tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.SourcePath))
            .GroupBy(track => NormalizeTrackPath(track.SourcePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var track in tracks)
        {
            var sourceKey = NormalizeTrackPath(track.FilePath);
            if (!string.IsNullOrWhiteSpace(sourceKey) &&
                preparedBySourcePath.TryGetValue(sourceKey, out var preparedTrack))
            {
                track.PreparedConvertedPath = preparedTrack.ConvertedPath ?? string.Empty;
                track.PreparedConvertedPreset = preparedTrack.Preset ?? string.Empty;
                track.HasReusableConvertedFile = IsReusablePreparedConvertedTrack(track, selectedPreset);
                if (track.HasReusableConvertedFile)
                {
                    var convertedInfo = new FileInfo(track.PreparedConvertedPath);
                    track.ConvertedSizeMb = convertedInfo.Length / 1024d / 1024d;
                    track.ConvertedSizeAvailable = true;
                }
                else
                {
                    ClearPreparedConvertedSize(track);
                }

                continue;
            }

            track.PreparedConvertedPath = string.Empty;
            track.PreparedConvertedPreset = string.Empty;
            track.HasReusableConvertedFile = false;
            ClearPreparedConvertedSize(track);
        }
    }

    public bool IsReusablePreparedConvertedTrack(TrackInfo track, string? selectedPreset)
    {
        if (string.IsNullOrWhiteSpace(track.PreparedConvertedPath) ||
            !string.Equals(track.PreparedConvertedPreset, selectedPreset, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!File.Exists(track.FilePath) || !File.Exists(track.PreparedConvertedPath))
            return false;

        if (track.PreparedConvertedPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
            track.PreparedConvertedPath.EndsWith(".copying", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sourceInfo = new FileInfo(track.FilePath);
        var convertedInfo = new FileInfo(track.PreparedConvertedPath);
        return sourceInfo.Length > 0 &&
               convertedInfo.Length > 0 &&
               convertedInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc;
    }

    private static void ClearPreparedConvertedSize(TrackInfo track)
    {
        track.ConvertedSizeMb = 0;
        track.ConvertedSizeAvailable = false;
    }

    private static string NormalizeTrackPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }
}
