using System.Diagnostics;
using System.IO;
using System.Text;

namespace BookStitch.Services;

public sealed class FfprobeRunnerService
{
    private const int CapturedOutputLimit = 40000;
    private const int MaximumJsonOutput = 16 * 1024 * 1024;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<FfprobeRunResult> RunAsync(
        string ffprobePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken token,
        PerformanceExternalProcessContext? performanceContext = null,
        Action<string>? stdoutLineHandler = null,
        bool useInactivityTimeout = false,
        bool runBelowNormalPriority = false)
    {
        if (string.IsNullOrWhiteSpace(ffprobePath))
            throw new InvalidOperationException(LocalizationText.Get("Tools.Ffprobe.NotConfigured"));

        using var process = new Process();
        var stderr = new StringBuilder();
        var stdout = new StringBuilder();
        Exception? outputError = null;
        using var timeoutSource = new CancellationTokenSource();
        var stderrClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdoutClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        async Task ReadOutputAsync()
        {
            try
            {
                await ReadOutputLinesAsync(process.StandardOutput, line =>
                {
                    if (useInactivityTimeout) timeoutSource.CancelAfter(timeout);
                    if (stdoutLineHandler is not null) stdoutLineHandler(line);
                    else
                    {
                        if (stdout.Length + (long)line.Length + Environment.NewLine.Length > MaximumJsonOutput)
                            throw new InvalidDataException("FFprobe metadata output exceeds the supported size; packet scans require streaming.");
                        stdout.AppendLine(line);
                    }
                }, stdoutLineHandler is null ? MaximumJsonOutput : 64 * 1024).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref outputError, ex, null);
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception cleanupError) when (cleanupError is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Trace.TraceWarning("FFprobe stop after output failure failed: {0}", cleanupError.Message);
                }
            }
            finally { stdoutClosed.TrySetResult(true); }
        }

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrClosed.TrySetResult(true);
                return;
            }

            AppendBounded(stderr, e.Data);
        };

        var performanceScope =
            performanceContext?.Start("ffprobe", "audio.probe") ?? default;

        try
        {
            process.Start();
            if (runBelowNormalPriority)
            {
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
                {
                    Trace.TraceWarning("FFprobe background priority could not be applied: {0}", error.Message);
                }
            }
            _ = ReadOutputAsync();
            process.BeginErrorReadLine();

            timeoutSource.CancelAfter(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutSource.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && timeoutSource.IsCancellationRequested)
            {
                performanceScope.Cancel();
                await ExternalProcessCleanup.StopAfterCancellationAsync(
                        process,
                        stdoutClosed.Task,
                        stderrClosed.Task)
                    .ConfigureAwait(false);
                return new FfprobeRunResult(
                    ExitCode: null,
                    Stdout: stdout.ToString(),
                    Stderr: stderr.ToString(),
                    TimedOut: true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                performanceScope.Cancel();
                await ExternalProcessCleanup.StopAfterCancellationAsync(
                        process,
                        stdoutClosed.Task,
                        stderrClosed.Task)
                    .ConfigureAwait(false);
                return new FfprobeRunResult(
                    ExitCode: null,
                    Stdout: stdout.ToString(),
                    Stderr: stderr.ToString(),
                    TimedOut: false,
                    Canceled: true);
            }

            await ExternalProcessCleanup.DrainStreamsBoundedAsync(
                    stdoutClosed.Task,
                    stderrClosed.Task,
                    ExternalProcessCleanup.StreamDrainTimeout)
                .ConfigureAwait(false);

            if (!stdoutClosed.Task.IsCompletedSuccessfully || !stderrClosed.Task.IsCompletedSuccessfully)
                throw new IOException("FFprobe output streams did not finish; partial output cannot be accepted.");
            if (outputError is not null) throw new InvalidDataException("FFprobe output could not be processed.", outputError);
            performanceScope.Complete(process.ExitCode);
            return new FfprobeRunResult(
                process.ExitCode,
                stdout.ToString(),
                stderr.ToString(),
                TimedOut: false);
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

    internal static async Task ReadOutputLinesAsync(TextReader reader, Action<string> consume, int maximumLineLength)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
        {
            var start = 0;
            for (var index = 0; index < count; index++)
            {
                if (buffer[index] != '\n') continue;
                AppendSegment(start, index - start);
                if (line.Length > 0 && line[^1] == '\r') line.Length--;
                consume(line.ToString());
                line.Clear();
                start = index + 1;
            }
            AppendSegment(start, count - start);
        }
        if (line.Length > 0) consume(line.ToString());

        void AppendSegment(int start, int count)
        {
            if (line.Length + (long)count > maximumLineLength)
                throw new InvalidDataException("FFprobe output record exceeds the supported size.");
            line.Append(buffer, start, count);
        }
    }

    private static void AppendBounded(StringBuilder builder, string line)
    {
        var remaining = CapturedOutputLimit - builder.Length;
        if (remaining <= 0) return;
        builder.Append(line.AsSpan(0, Math.Min(remaining, line.Length)));
        remaining = CapturedOutputLimit - builder.Length;
        if (remaining > 0) builder.Append(Environment.NewLine.AsSpan(0, Math.Min(remaining, Environment.NewLine.Length)));
    }

}

public sealed record FfprobeRunResult(
    int? ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut,
    bool Canceled = false);
