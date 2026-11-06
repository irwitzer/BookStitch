using BookStitch.Models;
using System.IO;
using System.Globalization;

namespace BookStitch.Services;

public interface IAudioExportProcessingService
{
    Task<ConvertedAacTiming> AnalyzeConvertedTimingAsync(string path, string ffprobePath,
        ConvertedAacTiming? cached, CancellationToken token);
    Task PrepareTrackForExportAsync(
        TrackInfo track,
        string sourcePath,
        string convertedPath,
        ExportPreset preset,
        string ffmpegPath,
        CancellationToken token,
        Action<TimeSpan> onProgress,
        PerformanceExternalProcessContext? performanceContext = null,
        string? ffprobePath = null);

    Task ValidateMergeInputsAsync(IReadOnlyList<string> paths, int targetRate, string ffprobePath,
        CancellationToken token, ExportPreset? preset = null) => Task.CompletedTask;

    Task MergeConvertedTracksAsync(
        string concatListPath,
        string chapterMetadataPath,
        string finalPartPath,
        long totalTicks,
        string ffmpegPath,
        CancellationToken token,
        Action<TimeSpan> onProgress,
        PerformanceExternalProcessContext? performanceContext = null);
}

public sealed class AacExportProcessingService : IAudioExportProcessingService
{
    public Task<ConvertedAacTiming> AnalyzeConvertedTimingAsync(string path, string ffprobePath,
        ConvertedAacTiming? cached, CancellationToken token) =>
        ConvertedAacTimelineService.AnalyzeAsync(path, ffprobePath, cached, token);
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
        Action<TimeSpan> onProgress,
        PerformanceExternalProcessContext? performanceContext = null,
        string? ffprobePath = null)
    {
        var action = AudioProcessingService.NormalizeProcessingAction(track.ProcessingAction);

        if (track.AacPlanAction is not null && track.AacPlanPreset == preset.DisplayName)
        {
            if (action != track.AacPlanAction) throw new InvalidDataException(LocalizationText.Get("Aac.Plan.SourceError"));
            if (action == ProcessingActionIds.Reuse)
            {
                var probeTool = ffprobePath ?? Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe");
                if (Path.GetExtension(sourcePath).Equals(".aac", StringComparison.OrdinalIgnoreCase))
                {
                    await RemuxTrackForExportAsync(track, sourcePath, convertedPath, preset, ffmpegPath, probeTool,
                        token, onProgress, performanceContext, adts: true);
                }
                else
                {
                    var sourceProbe = await new AudioInfoService().ProbeAsync(sourcePath, probeTool, token: token);
                    if (!sourceProbe.SupportsAacStreamCopy || sourceProbe.SampleRateHz != track.TargetSampleRateHz ||
                        sourceProbe.Channels != preset.Channels || sourceProbe.AacConfigurationId != track.AacConfigurationId)
                        throw new InvalidDataException(LocalizationText.Get("Aac.Plan.SourceError"));
                    if (sourceProbe.StreamCount > 1 || sourceProbe.Chapters.Count > 0)
                        await RemuxTrackForExportAsync(track, sourcePath, convertedPath, preset, ffmpegPath, probeTool,
                            token, onProgress, performanceContext, adts: false);
                    else await CopyTrackForExportAsync(track, sourcePath, convertedPath, token, onProgress);
                }
                onProgress(TimeSpan.MaxValue);
                return;
            }
        }
        if (preset.IsPassThrough) throw new InvalidDataException(LocalizationText.Get("Aac.Plan.InvalidOriginal"));

        var sampleRate = track.SampleRateHz;
        if (action == ProcessingActionIds.Reuse && File.Exists(ffmpegPath))
        {
            // Restored tracks may predate the persisted sample-rate field.
            var probePath = ffprobePath ?? Path.Combine(Path.GetDirectoryName(ffmpegPath)!, Path.GetExtension(ffmpegPath).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? "ffprobe.exe" : "ffprobe");
            var probe = await new AudioInfoService().ProbeAsync(sourcePath, probePath, token: token);
            token.ThrowIfCancellationRequested();
            sampleRate = probe.Success ? probe.SampleRateHz : null;
            if (sampleRate is not null && sampleRate == track.TargetSampleRateHz &&
                (!probe.SupportsAacStreamCopy || !AudioProcessingService.AacMatchesPreset(probe.BitrateKbps, probe.Channels, preset)))
                throw new InvalidDataException(LocalizationText.Format("Audio.PreparedSampleRateMismatch", Path.GetFileName(sourcePath), track.TargetSampleRateHz));
        }
        if (action == ProcessingActionIds.Reuse && track.AllowAacReuse && FolderSampleRateService.CanVote(sampleRate) && sampleRate == track.TargetSampleRateHz)
        {
            await CopyTrackForExportAsync(track, sourcePath, convertedPath, token, onProgress);
            onProgress(TimeSpan.MaxValue);
            return;
        }

        await ConvertTrackToAacAsync(
            sourcePath,
            convertedPath,
            preset,
            track.TargetSampleRateHz,
            ffmpegPath,
            token,
            onProgress,
            performanceContext, strict: track.AacPlanAction is not null);
    }

    public async Task ValidateMergeInputsAsync(IReadOnlyList<string> paths, int targetRate, string ffprobePath,
        CancellationToken token, ExportPreset? preset = null)
    {
        FolderSampleRateService.RequireSupported(targetRate);
        var configurations = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(paths.Distinct(StringComparer.OrdinalIgnoreCase),
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (path, ct) =>
            {
                var probe = await new AudioInfoService().ProbeAsync(path, ffprobePath, token: ct);
                ct.ThrowIfCancellationRequested();
                if (!probe.SupportsAacStreamCopy || probe.SampleRateHz != targetRate ||
                    (preset is not null && probe.Channels != preset.Channels))
                    throw new InvalidDataException(LocalizationText.Format("Audio.PreparedSampleRateMismatch",
                        Path.GetFileName(path), targetRate));
                configurations.Add($"{probe.Channels}|{probe.AudioChannelLayout}|{probe.AacConfigurationId}");
            });
        if (configurations.Distinct(StringComparer.Ordinal).Skip(1).Any())
            throw new InvalidDataException(LocalizationText.Get("AacTimeline.Invalid"));
    }

    public async Task MergeConvertedTracksAsync(
        string concatListPath,
        string chapterMetadataPath,
        string finalPartPath,
        long totalTicks,
        string ffmpegPath,
        CancellationToken token,
        Action<TimeSpan> onProgress,
        PerformanceExternalProcessContext? performanceContext = null)
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
            "-movflags", "+faststart+disable_chpl",
            "-f", "ipod",
            finalPartPath
        };

        var result = await _ffmpegRunnerService.RunAsync(
            ffmpegPath,
            args,
            token,
            onProgress,
            performanceContext);

        if (result.ExitCode != 0 || !File.Exists(finalPartPath))
            throw new InvalidOperationException("FFmpeg konnte die finale Ausgabedatei nicht zusammenfügen.\n\n" + result.GetShortError());
    }

    private async Task RemuxTrackForExportAsync(TrackInfo track, string source, string target, ExportPreset preset,
        string ffmpeg, string ffprobe, CancellationToken token, Action<TimeSpan> progress,
        PerformanceExternalProcessContext? context, bool adts)
    {
        var part = target + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var args = new List<string> { "-v", "error", "-xerror", "-y", "-i", source, "-map", "0:a:0", "-vn", "-sn", "-dn",
            "-map_metadata", "-1", "-map_chapters", "-1", "-c:a", "copy" };
        if (adts) args.AddRange(["-bsf:a", "aac_adtstoasc"]);
        args.AddRange(["-movflags", "+faststart", "-f", "ipod", part]);
        var result = await _ffmpegRunnerService.RunAsync(ffmpeg, args, token, progress, context);
        if (result.ExitCode != 0) throw new InvalidDataException(result.GetShortError());
        await ValidateMergeInputsAsync([part], track.TargetSampleRateHz, ffprobe, token, preset);
        var probe = await new AudioInfoService().ProbeAsync(part, ffprobe, token: token);
        if (probe.StreamCount != 1 || probe.AacConfigurationId != track.AacConfigurationId)
            throw new InvalidDataException(LocalizationText.Get("Aac.Plan.SourceError"));
        ProjectExtensionRollbackService.PreserveActiveConvertedFile(target);
        File.Move(part, target, true);
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

        ProjectExtensionRollbackService.PreserveActiveConvertedFile(convertedPath);
        TryDeleteFile(convertedPath);
        File.Move(partPath, convertedPath);
    }

    private async Task ConvertTrackToAacAsync(
        string sourcePath,
        string convertedPath,
        ExportPreset preset,
        int targetSampleRateHz,
        string ffmpegPath,
        CancellationToken token,
        Action<TimeSpan> onProgress,
        PerformanceExternalProcessContext? performanceContext, bool strict = false)
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
            "-ar", FolderSampleRateService.RequireSupported(targetSampleRateHz).ToString(CultureInfo.InvariantCulture),
            "-movflags", "+faststart",
            "-f", "ipod",
            partPath
        };
        if (strict) args.InsertRange(args.IndexOf("-vn"), ["-map", "0:a:0"]);

        var result = await _ffmpegRunnerService.RunAsync(
            ffmpegPath,
            strict ? new[] { "-xerror" }.Concat(args).ToArray() : args,
            token,
            onProgress,
            performanceContext);

        if (result.ExitCode != 0 || !File.Exists(partPath))
            throw new InvalidOperationException("FFmpeg konnte eine Zwischendatei nicht erzeugen.\n\n" + result.GetShortError());

        ProjectExtensionRollbackService.PreserveActiveConvertedFile(convertedPath);
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
