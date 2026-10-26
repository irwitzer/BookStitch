using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FfprobeRunnerServiceTests
{
    [Fact]
    public async Task Output_reader_rejects_oversized_records_before_building_an_unbounded_line()
    {
        using var input = new StringReader(new string('x', 65537));
        var called = false;
        await Assert.ThrowsAsync<InvalidDataException>(() => FfprobeRunnerService.ReadOutputLinesAsync(input,
            _ => called = true, 65536));
        Assert.False(called);
    }

    [Fact]
    public async Task Output_reader_preserves_records_across_buffer_boundaries_and_final_line_without_newline()
    {
        var first = new string('x', 5000);
        using var input = new StringReader(first + "\r\nlast");
        var lines = new List<string>();
        await FfprobeRunnerService.ReadOutputLinesAsync(input, lines.Add, 65536);
        Assert.Equal(new[] { first, "last" }, lines);
    }

    [Fact]
    public async Task Streaming_output_is_consumed_without_retaining_stdout()
    {
        var count = 0;
        var result = await new FfprobeRunnerService().RunAsync(GetCommandProcessor(),
            ["/c", "for /L %i in (1,1,5000) do @echo packet-record"], TimeSpan.FromSeconds(5),
            CancellationToken.None, stdoutLineHandler: line => { Assert.Equal("packet-record", line); count++; });
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(5000, count);
        Assert.Empty(result.Stdout);
    }

    [Fact]
    public async Task Streaming_parser_failure_stops_process_and_reports_error()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => AwaitWithGuardAsync(new FfprobeRunnerService().RunAsync(
            GetCommandProcessor(), ["/c", "echo invalid & ping -n 6 127.0.0.1 > nul"],
            TimeSpan.FromSeconds(10), CancellationToken.None,
            stdoutLineHandler: _ => throw new InvalidDataException("Invalid packet"))));
    }

    [Fact]
    public async Task Productive_stream_can_exceed_the_inactivity_timeout()
    {
        var count = 0;
        var result = await new FfprobeRunnerService().RunAsync(GetCommandProcessor(),
            ["/c", "for /L %i in (1,1,4) do @(echo packet & ping -n 2 127.0.0.1 > nul)"],
            TimeSpan.FromSeconds(2), CancellationToken.None,
            stdoutLineHandler: _ => count++, useInactivityTimeout: true);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task RunAsync_ReturnsStdoutAndExitCode()
    {
        var runner = new FfprobeRunnerService();

        var result = await runner.RunAsync(
            GetCommandProcessor(),
            ["/c", "echo", "ffprobe-ok"],
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ffprobe-ok", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ReturnsStdoutBeyondLegacyCaptureLimit()
    {
        var runner = new FfprobeRunnerService();

        var result = await runner.RunAsync(
            GetCommandProcessor(),
            ["/c", "for /L %i in (1,1,5000) do @echo 01234567890123456789"],
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Stdout.Length > 100000);
        Assert.Equal(5000, CountNonEmptyLines(result.Stdout));
    }

    [Fact]
    public async Task RunAsync_KeepsStderrBounded()
    {
        var runner = new FfprobeRunnerService();

        var result = await runner.RunAsync(
            GetCommandProcessor(),
            ["/c", "for /L %i in (1,1,5000) do @echo 01234567890123456789 1>&2"],
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Stderr.Length >= 40000);
        Assert.True(result.Stderr.Length < 41000);
    }

    [Fact]
    public async Task RunAsync_ReturnsNonZeroExitAndStderr()
    {
        var runner = new FfprobeRunnerService();

        var result = await runner.RunAsync(
            GetCommandProcessor(),
            ["/c", "echo ffprobe-error 1>&2 & exit /b 7"],
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.False(result.TimedOut);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("ffprobe-error", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_WhenTimeout_KillsProcessAndReturnsTimeout()
    {
        var runner = new FfprobeRunnerService();

        var result = await AwaitWithGuardAsync(runner.RunAsync(
                GetCommandProcessor(),
                ["/c", "ping -n 6 127.0.0.1 > nul"],
                TimeSpan.FromMilliseconds(100),
                CancellationToken.None));

        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_WhenExternallyCanceled_StopsProcessAndReturnsCanceledResult()
    {
        var runner = new FfprobeRunnerService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await AwaitWithGuardAsync(
            runner.RunAsync(
                GetCommandProcessor(),
                ["/c", "ping -n 6 127.0.0.1 > nul"],
                TimeSpan.FromSeconds(10),
                cancellation.Token));

        Assert.True(result.Canceled);
        Assert.False(result.TimedOut);
        Assert.Null(result.ExitCode);
    }

    private static async Task<T> AwaitWithGuardAsync<T>(Task<T> task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(task, completed);
        return await task;
    }

    private static async Task AwaitWithGuardAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(task, completed);
        await task;
    }

    private static int CountNonEmptyLines(string value) =>
        value
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Length;

    private static string GetCommandProcessor() =>
        Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
}
