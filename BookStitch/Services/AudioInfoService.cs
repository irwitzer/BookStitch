using System.IO;
using BookStitch.Models;

namespace BookStitch.Services;

public class AudioInfoService
{
    private readonly FfprobeRunnerService _ffprobeRunnerService;
    private readonly FfmpegRunnerService _ffmpegRunnerService;
    private readonly AudioProbeJsonParser _audioProbeJsonParser = new();

    public AudioInfoService()
        : this(new FfprobeRunnerService(), new FfmpegRunnerService())
    {
    }

    public AudioInfoService(
        FfprobeRunnerService ffprobeRunnerService,
        FfmpegRunnerService ffmpegRunnerService)
    {
        _ffprobeRunnerService = ffprobeRunnerService;
        _ffmpegRunnerService = ffmpegRunnerService;
    }

    public async Task<AudioProbeInfo> ProbeAsync(
        string filePath,
        string? ffprobePath,
        PerformanceExternalProcessContext? performanceContext = null,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new AudioProbeInfo
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = LocalizationText.Get("Probe.SourceMissing")
            };
        }

        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
        {
            return new AudioProbeInfo
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = LocalizationText.Get("Probe.ToolMissing")
            };
        }

        try
        {
            var result = await _ffprobeRunnerService.RunAsync(
                    ffprobePath,
                    [
                        "-v",
                        "error",
                        "-print_format",
                        "json",
                        "-show_format",
                        "-show_streams",
                        "-show_data_hash", "sha256",
                        "-show_chapters",
                        filePath
                    ],
                    FfprobeRunnerService.DefaultTimeout,
                    token,
                    performanceContext)
                .ConfigureAwait(false);

            if (result.TimedOut)
            {
                return new AudioProbeInfo
                {
                    FilePath = filePath,
                    Success = false,
                    TimedOut = true,
                    ErrorMessage = LocalizationText.Get("Probe.Timeout")
                };
            }

            if (result.Canceled)
            {
                return new AudioProbeInfo
                {
                    FilePath = filePath,
                    Success = false,
                    ErrorMessage = LocalizationText.Get("Probe.Canceled")
                };
            }

            if (result.ExitCode != 0)
            {
                return new AudioProbeInfo
                {
                    FilePath = filePath,
                    Success = false,
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Stderr)
                        ? LocalizationText.Format("Probe.ExitCode", result.ExitCode)
                        : result.Stderr.Trim()
                };
            }

            return _audioProbeJsonParser.Parse(filePath, result.Stdout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AudioProbeInfo
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> CanDecodeAudioAsync(
        string filePath,
        string? ffmpegPath,
        CancellationToken token = default,
        PerformanceExternalProcessContext? performanceContext = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) ||
            string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            return false;
        }

        var performanceScope =
            performanceContext?.Start("ffmpeg", "audio.decode-check") ?? default;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                var result = await _ffmpegRunnerService.RunAsync(
                        ffmpegPath,
                        [
                            "-nostdin",
                            "-v",
                            "error",
                            "-xerror",
                            "-i",
                            filePath,
                            "-map",
                            "0:a:0",
                            "-t",
                            "0.5",
                            "-f",
                            "null",
                            "-"
                        ],
                        timeout.Token,
                        performanceContext: null)
                    .ConfigureAwait(false);

                performanceScope.Complete(result.ExitCode);
                return result.ExitCode == 0;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                performanceScope.Cancel();
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            performanceScope.Cancel();
            throw;
        }
        catch
        {
            performanceScope.Fail();
            return false;
        }
    }

}
