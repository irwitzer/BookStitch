using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class FfmpegRunnerService
{
    public async Task<FfmpegRunResult> RunAsync(
        string ffmpegPath,
        IReadOnlyList<string> arguments,
        CancellationToken token,
        Action<TimeSpan>? onProgress = null)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath))
            throw new InvalidOperationException("FFmpeg ist nicht eingerichtet.");

        using var process = new Process();
        var stderr = new StringBuilder();
        var stdout = new StringBuilder();
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

            if (stdout.Length < 40000)
                stdout.AppendLine(e.Data);

            var progress = TryParseProgressLine(e.Data);
            if (progress.HasValue)
                onProgress?.Invoke(progress.Value);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrClosed.TrySetResult(true);
                return;
            }

            if (stderr.Length < 40000)
                stderr.AppendLine(e.Data);

            var progress = TryParseProgressLine(e.Data);
            if (progress.HasValue)
                onProgress?.Invoke(progress.Value);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(token);
            await Task.WhenAll(stdoutClosed.Task, stderrClosed.Task);

            return new FfmpegRunResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (OperationCanceledException)
        {
            await StopProcessAfterCancellationAsync(process, stdoutClosed.Task, stderrClosed.Task);
            throw;
        }
    }

    private static TimeSpan? TryParseProgressLine(string line)
    {
        return TryParseFfmpegProgressTime(line) ?? TryParseFfmpegProgressKeyValue(line);
    }

    private static TimeSpan? TryParseFfmpegProgressTime(string line)
    {
        var match = Regex.Match(line, @"time=(\d+):(\d+):(\d+(?:\.\d+)?)");

        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups[1].Value, out var hours))
            return null;

        if (!int.TryParse(match.Groups[2].Value, out var minutes))
            return null;

        if (!double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return null;

        return TimeSpan.FromSeconds(hours * 3600 + minutes * 60 + seconds);
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
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds)
                ? TimeSpan.FromTicks(microseconds * 10)
                : null;
        }

        if (string.Equals(key, "out_time", StringComparison.OrdinalIgnoreCase))
            return TryParseFfmpegProgressTime("time=" + value);

        return null;
    }

    private static async Task StopProcessAfterCancellationAsync(
        Process process,
        Task stdoutClosed,
        Task stderrClosed)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Wenn der Prozess bereits weg ist, ist das okay.
        }

        try
        {
            if (!process.HasExited)
                await process.WaitForExitAsync(CancellationToken.None);
        }
        catch
        {
            // Der Abbruch bleibt maßgeblich; Cleanup-Fehler dürfen ihn nicht ersetzen.
        }

        try
        {
            await Task.WhenAll(stdoutClosed, stderrClosed).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Umgeleitete Streams können bei hartem Prozessabbruch verzögert schließen.
        }
    }
}

public sealed record FfmpegRunResult(int ExitCode, string Stdout, string Stderr)
{
    public string GetShortError()
    {
        var text = string.IsNullOrWhiteSpace(Stderr) ? Stdout : Stderr;

        if (string.IsNullOrWhiteSpace(text))
            return "FFmpeg hat keine Fehlermeldung ausgegeben.";

        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(18);

        return string.Join("\n", lines);
    }
}
