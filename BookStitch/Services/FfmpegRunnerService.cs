using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class FfmpegRunnerService
{
    private const int CapturedOutputLimit = 40000;
    private static readonly Regex ProgressTimeRegex = new(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<FfmpegRunResult> RunAsync(
        string ffmpegPath,
        IReadOnlyList<string> arguments,
        CancellationToken token,
        Action<TimeSpan>? onProgress = null,
        PerformanceExternalProcessContext? performanceContext = null)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
            throw new InvalidOperationException(LocalizationText.Get("Tools.Ffmpeg.NotConfigured"));

        using var process = new Process();
        var stderr = new StringBuilder();
        var stdout = new StringBuilder();
        Exception? progressError = null;
        var stderrClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdoutClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutClosed.TrySetResult(true);
                return;
            }

            AppendBounded(stdout, e.Data);
            ReportProgress(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrClosed.TrySetResult(true);
                return;
            }

            AppendBounded(stderr, e.Data);
            ReportProgress(e.Data);
        };

        void ReportProgress(string line)
        {
            if (onProgress is null || Volatile.Read(ref progressError) is not null)
                return;

            try
            {
                var progress = TryParseProgressLine(line);
                if (progress.HasValue)
                    onProgress(progress.Value);
            }
            catch (Exception error)
            {
                // Process events run on background threads; report failures through RunAsync.
                Interlocked.CompareExchange(ref progressError, error, null);
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception cleanupError) when (cleanupError is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Trace.TraceWarning("FFmpeg stop after progress failure failed: {0}", cleanupError.Message);
                }
            }
        }

        var performanceScope =
            performanceContext?.Start("ffmpeg", "ffmpeg.run") ?? default;

        try
        {
            token.ThrowIfCancellationRequested();
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(token);
            await ExternalProcessCleanup.DrainStreamsBoundedAsync(
                    stdoutClosed.Task,
                    stderrClosed.Task,
                    ExternalProcessCleanup.StreamDrainTimeout)
                .ConfigureAwait(false);

            if (progressError is not null)
                throw new InvalidOperationException("FFmpeg progress could not be processed.", progressError);
            performanceScope.Complete(process.ExitCode);
            return new FfmpegRunResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (OperationCanceledException)
        {
            performanceScope.Cancel();
            await ExternalProcessCleanup.StopAfterCancellationAsync(
                    process,
                    stdoutClosed.Task,
                    stderrClosed.Task)
                .ConfigureAwait(false);
            throw;
        }
        catch
        {
            performanceScope.Fail();
            throw;
        }
    }

    private static TimeSpan? TryParseProgressLine(string line)
    {
        return TryParseFfmpegProgressTime(line) ?? TryParseFfmpegProgressKeyValue(line);
    }

    private static TimeSpan? TryParseFfmpegProgressTime(string line)
    {
        var match = ProgressTimeRegex.Match(line);

        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups[1].Value, out var hours))
            return null;

        if (!int.TryParse(match.Groups[2].Value, out var minutes))
            return null;

        if (!double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return null;

        try
        {
            return TimeSpan.FromSeconds(hours * 3600d + minutes * 60d + seconds);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static TimeSpan? TryParseFfmpegProgressKeyValue(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
            return null;

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();

        if (string.Equals(key, "out_time_ms", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "out_time_us", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds) &&
                   microseconds >= long.MinValue / 10 && microseconds <= long.MaxValue / 10
                ? TimeSpan.FromTicks(microseconds * 10)
                : null;
        }

        if (string.Equals(key, "out_time", StringComparison.OrdinalIgnoreCase))
            return TryParseFfmpegProgressTime("time=" + value);

        return null;
    }

    private static void AppendBounded(StringBuilder builder, string line)
    {
        var remaining = CapturedOutputLimit - builder.Length;
        if (remaining <= 0) return;
        builder.Append(line.AsSpan(0, Math.Min(remaining, line.Length)));
        remaining = CapturedOutputLimit - builder.Length;
        if (remaining > 0)
            builder.Append(Environment.NewLine.AsSpan(0, Math.Min(remaining, Environment.NewLine.Length)));
    }

}

public sealed record FfmpegRunResult(int ExitCode, string Stdout, string Stderr)
{
    public string GetShortError()
    {
        var text = string.IsNullOrWhiteSpace(Stderr) ? Stdout : Stderr;

        if (string.IsNullOrWhiteSpace(text))
            return LocalizationText.Get("Tools.Ffmpeg.NoErrorOutput");

        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(18);

        return string.Join("\n", lines);
    }
}
