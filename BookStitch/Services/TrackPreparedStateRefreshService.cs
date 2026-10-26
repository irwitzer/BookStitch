using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

internal sealed record PresetPreparationProgress(
    int PreparedCount,
    int TotalCount,
    int PendingCount,
    int Percent);

public sealed class TrackPreparedStateRefreshService
{
    public void RefreshForCurrentPreset(IEnumerable<TrackInfo> tracks, string? projectWorkFolder,
        string? selectedPreset, PerformanceMeasurement? measurement = null, PerformanceTraceService? trace = null)
    {
        var trackList = tracks.ToArray();
        var entries = LoadPreparedEntries(projectWorkFolder, selectedPreset, measurement, trace)
            .GroupBy(entry => NormalizeTrackPath(entry.SourcePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.TrackIndex).ToArray(), StringComparer.OrdinalIgnoreCase);
        string[] targets = [];
        if (!string.IsNullOrWhiteSpace(projectWorkFolder) && !string.IsNullOrWhiteSpace(selectedPreset))
        {
            try
            {
                targets = ConvertedTrackPathService.GetConvertedTrackPaths(ProjectFolderLayout.GetConvertedPresetFolder(
                    projectWorkFolder, ExportPreset.Parse(selectedPreset).GetFolderName()), projectWorkFolder, trackList);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException)
            {
                // Invalid paths cannot establish occurrence-specific preparation evidence.
            }
        }
        var sourceCounts = trackList.GroupBy(track => NormalizeTrackPath(track.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < trackList.Length; index++)
        {
            var track = trackList[index];
            var sourceKey = NormalizeTrackPath(track.FilePath);
            entries.TryGetValue(sourceKey, out var candidates);
            var prepared = candidates?.FirstOrDefault(entry =>
                entry.TargetSampleRateHz == track.TargetSampleRateHz &&
                (track.SourceId == Guid.Empty || entry.SourceId == track.SourceId) &&
                (sourceCounts[sourceKey] == 1 || (targets.Length > index && TrackPathService.PathEquals(entry.ConvertedPath, targets[index]))));
            track.PreparedConvertedPath = prepared?.ConvertedPath ?? string.Empty;
            track.PreparedConvertedPreset = prepared?.Preset ?? string.Empty;
            track.HasReusableConvertedFile = prepared is not null;
            track.ConvertedSizeAvailable = prepared is not null;
        }
    }

    internal static IReadOnlyList<ExportWorkManifestTrack> LoadPreparedEntries(string? projectFolder, string? preset,
        PerformanceMeasurement? measurement = null, PerformanceTraceService? trace = null)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || string.IsNullOrWhiteSpace(preset)) return [];
        var store = trace is null ? new WorkManifestService() : new WorkManifestService(trace);
        var entries = new List<ExportWorkManifestTrack>();
        foreach (var path in new[] { ProjectFolderLayout.ResolveExportManifestPath(projectFolder),
                     ProjectFolderLayout.ResolveWorkManifestPath(projectFolder) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                measurement?.Add("ManifestReads");
                measurement?.Add("PreparedStateReads");
                var manifest = store.ReadOnly(path);
                entries.AddRange(manifest.Tracks?.Where(entry => entry is not null &&
                    WorkManifestService.IsReusableConvertedManifestEntry(entry, preset, measurement)) ?? []);
            }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or PersistentJsonSchemaException)
            {
                // Unreadable state cannot establish preparation; a refresh must remain safe.
            }
        }
        return entries.GroupBy(entry => NormalizeTrackPath(entry.ConvertedPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entry => entry.CompletedUtc).First()).ToArray();
    }

    public bool IsReusablePreparedConvertedTrack(TrackInfo track, string? selectedPreset) =>
        track.HasReusableConvertedFile && !string.IsNullOrWhiteSpace(track.PreparedConvertedPath) &&
        string.Equals(track.PreparedConvertedPreset, selectedPreset, StringComparison.OrdinalIgnoreCase);

    internal PresetPreparationProgress GetPreparationProgress(
        IReadOnlyCollection<TrackInfo> tracks,
        string? selectedPreset)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var total = tracks.Count;
        if (total == 0)
            return new PresetPreparationProgress(0, 0, 0, 0);

        var prepared = tracks.Count(track =>
            IsReusablePreparedConvertedTrack(track, selectedPreset));
        var pending = total - prepared;
        var percent = (int)Math.Round(prepared * 100d / total, MidpointRounding.AwayFromZero);
        return new PresetPreparationProgress(prepared, total, pending, percent);
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
