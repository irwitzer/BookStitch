using BookStitch.Models;
using System.IO;
using System.Globalization;

namespace BookStitch.Services;

public sealed class AacExportProcessingService
{
    private readonly FfmpegRunnerService _ffmpegRunnerService;

    public AacExportProcessingService()
        : this(new FfmpegRunnerService())
    {
    }

    public AacExportProcessingService(FfmpegRunnerService ffmpegRunnerService)
    {
        _ffmpegRunnerService = ffmpegRunnerService;
    }

    public async Task PrepareTrackForExportAsync(
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset,
        string ffmpegPath,
        CancellationToken token,
        Action<TimeSpan> onProgress)
    {
        var action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction);

        if (action == "Übernehmen")
        {
            await CopyTrackForExportAsync(track, sourcePath, convertedPath, token, onProgress);
            onProgress(TimeSpan.MaxValue);
            return;
        }

        await ConvertTrackToAacAsync(sourcePath, convertedPath, preset, ffmpegPath, token, onProgress);
    }

    public async Task MergeConvertedTracksAsync(
        string concatListPath,
        string chapterMetadataPath,
        string finalPartPath,
        long totalTicks,
        string ffmpegPath,
        CancellationToken token,
        Action<TimeSpan> onProgress)
    {
        TryDeleteFile(finalPartPath);

        var args = new List<string>
        {
            "-hide_banner",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", concatListPath,
            "-i", chapterMetadataPath,
            "-map_metadata", "1",
            "-map_chapters", "1",
            "-vn",
            "-c", "copy",
            "-movflags", "+faststart",
            "-f", "ipod",
            finalPartPath
        };

        var result = await _ffmpegRunnerService.RunAsync(ffmpegPath, args, token, onProgress);

        if (result.ExitCode != 0 || !File.Exists(finalPartPath))
            throw new InvalidOperationException("FFmpeg konnte die finale Ausgabedatei nicht zusammenfügen.\n\n" + result.GetShortError());
    }

    private static async Task CopyTrackForExportAsync(
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        CancellationToken token,
        Action<TimeSpan> onProgress)
    {
        var partPath = convertedPath + ".part";

        TryDeleteFile(partPath);

        Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);

        var sourceInfo = new FileInfo(sourcePath);
        var sourceLength = Math.Max(1L, sourceInfo.Length);
        var durationTicks = TrackDurationService.GetEffectiveDurationTicks(track);
        var copiedBytes = 0L;
        var buffer = new byte[1024 * 1024];

        await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true))
        await using (var targetStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
        {
            while (true)
            {
                var read = await sourceStream.ReadAsync(buffer, token);
                if (read == 0)
                    break;

                await targetStream.WriteAsync(buffer.AsMemory(0, read), token);
                copiedBytes += read;

                if (durationTicks > 0)
                {
                    var progressTicks = (long)Math.Clamp(
                        copiedBytes / (double)sourceLength * durationTicks,
                        0d,
                        (double)durationTicks);
                    onProgress(TimeSpan.FromTicks(progressTicks));
                }
            }
        }

        TryDeleteFile(convertedPath);
        File.Move(partPath, convertedPath);
    }

    private async Task ConvertTrackToAacAsync(
        string sourcePath,
        string convertedPath,
        ExportPreset preset,
        string ffmpegPath,
        CancellationToken token,
        Action<TimeSpan> onProgress)
    {
        var partPath = convertedPath + ".part";

        TryDeleteFile(partPath);

        Directory.CreateDirectory(Path.GetDirectoryName(convertedPath)!);

        var args = new List<string>
        {
            "-hide_banner",
            "-progress", "pipe:1",
            "-nostats",
            "-y",
            "-i", sourcePath,
            "-vn",
            "-c:a", "aac",
            "-b:a", $"{preset.BitrateKbps}k",
            "-ac", preset.Channels.ToString(CultureInfo.InvariantCulture),
            "-movflags", "+faststart",
            "-f", "ipod",
            partPath
        };

        var result = await _ffmpegRunnerService.RunAsync(ffmpegPath, args, token, onProgress);

        if (result.ExitCode != 0 || !File.Exists(partPath))
            throw new InvalidOperationException("FFmpeg konnte eine Zwischendatei nicht erzeugen.\n\n" + result.GetShortError());

        TryDeleteFile(convertedPath);
        File.Move(partPath, convertedPath);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch
        {
            // Cleanup darf den eigentlichen Export nicht blockieren.
        }
    }
}
