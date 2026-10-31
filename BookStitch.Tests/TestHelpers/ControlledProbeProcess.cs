using BookStitch.Services;
using System.IO.Pipes;
using System.Text;

namespace BookStitch.Tests.TestHelpers;

/// <summary>A real child process driven by pipe handshakes, without sleeps or shell AutoRun.</summary>
internal sealed class ControlledProbeProcess : IAsyncDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);
    private readonly CancellationTokenSource _stop = new();
    private readonly NamedPipeClientStream _pipe;
    private StreamWriter? _writer;
    public Task<FfprobeRunResult> Execution { get; }

    private ControlledProbeProcess(DeadlineTestTimeProvider clock, TimeSpan timeout,
        Action<string>? onOutput, bool inactivity)
    {
        var name = "BookStitch-process-test-" + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var script = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ProcessProbe.ps1");
        Execution = new FfprobeRunnerService(clock).RunAsync(powershell,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-PipeName", name],
            timeout, _stop.Token, stdoutLineHandler: onOutput, useInactivityTimeout: inactivity);
    }

    public static async Task<ControlledProbeProcess> StartAsync(DeadlineTestTimeProvider? clock = null,
        TimeSpan? timeout = null, Action<string>? onOutput = null, bool inactivity = false)
    {
        var process = new ControlledProbeProcess(clock ?? new DeadlineTestTimeProvider(),
            timeout ?? TimeSpan.FromSeconds(30), onOutput, inactivity);
        try
        {
            var connected = process._pipe.ConnectAsync();
            var startup = await Task.WhenAny(connected, process.Execution).WaitAsync(Guard);
            if (startup == process.Execution)
            {
                var result = await process.Execution;
                throw new InvalidOperationException($"Probe exited before its handshake: {result.ExitCode}; {result.Stderr}");
            }
            await connected;
            process._writer = new StreamWriter(process._pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            return process;
        }
        catch { await process.DisposeAsync(); throw; }
    }

    public Task SendAsync(string command) => _writer!.WriteLineAsync(command).WaitAsync(Guard);
    public Task<FfprobeRunResult> ResultAsync() => Execution.WaitAsync(Guard);
    public void Cancel() => _stop.Cancel();

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _writer?.Dispose();
        _pipe.Dispose();
        try { await Execution.WaitAsync(Guard); }
        catch when (Execution.IsCompleted) { /* The test observes expected parser failures. */ }
        finally { _stop.Dispose(); }
    }
}
