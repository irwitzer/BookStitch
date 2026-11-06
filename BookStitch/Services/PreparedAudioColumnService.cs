using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed record PreparedAudioColumnResult(TrackInfo Track, string Path, string Preset, long Length,
    long Modified, AudioStreamDetails Audio);

/// <summary>Presentation evidence only. Uses existing timing metadata, or a cached stream probe for older artifacts.</summary>
public sealed class PreparedAudioColumnService
{
    public async Task<IReadOnlyList<PreparedAudioColumnResult>> ReadAsync(IReadOnlyList<TrackInfo> tracks,
        string project, string preset, string ffprobe, AudioEvidenceCache cache, CancellationToken token)
    {
        var entries = TrackPreparedStateRefreshService.LoadPreparedEntries(project, preset)
            .GroupBy(entry => Path.GetFullPath(entry.ConvertedPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var requests = tracks.Where(track => track.HasReusableConvertedFile && track.PreparedAudio is null &&
            track.PreparedConvertedPreset == preset && File.Exists(track.PreparedConvertedPath))
            .Select(track => (Track: track, Path: Path.GetFullPath(track.PreparedConvertedPath))).ToArray();
        var results = new System.Collections.Concurrent.ConcurrentBag<PreparedAudioColumnResult>();
        await Parallel.ForEachAsync(requests, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
            async (request, cancellation) =>
            {
                var file = new FileInfo(request.Path); var length = file.Length; var modified = file.LastWriteTimeUtc.Ticks;
                entries.TryGetValue(request.Path, out var entry);
                var details = entry?.AacTiming is { IsValid: true, Audio: not null } timing &&
                    timing.Length == length && timing.LastWriteUtcTicks == modified ? timing.Audio : null;
                if (details is null)
                {
                    var probe = await new AudioInfoService().ProbeAsync(request.Path, ffprobe, token: cancellation, evidenceCache: cache);
                    details = AudioStreamDetails.FromProbe(probe);
                }
                cancellation.ThrowIfCancellationRequested(); file.Refresh();
                if (details is not null && file.Exists && file.Length == length && file.LastWriteTimeUtc.Ticks == modified)
                    results.Add(new(request.Track, request.Path, preset, length, modified, details));
            });
        return results.ToArray();
    }
}
