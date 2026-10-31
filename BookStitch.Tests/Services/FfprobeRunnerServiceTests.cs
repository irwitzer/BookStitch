using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Threading.Channels;
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
        await using var probe = await ControlledProbeProcess.StartAsync(onOutput: line =>
        { Assert.Equal("packet-record", line); Interlocked.Increment(ref count); });
        await probe.SendAsync("out|5000|packet-record");
        await probe.SendAsync("exit|0");
        var result = await probe.ResultAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(5000, count);
        Assert.Empty(result.Stdout);
    }

    [Fact]
    public async Task Streaming_parser_failure_stops_process_and_reports_error()
    {
        await using var probe = await ControlledProbeProcess.StartAsync(onOutput: _ =>
            throw new InvalidDataException("Invalid packet"));
        await probe.SendAsync("out|1|invalid");
        await Assert.ThrowsAsync<InvalidDataException>(() => probe.ResultAsync());
    }

    [Fact]
    public async Task Productive_stream_can_exceed_the_inactivity_timeout()
    {
        var clock = new DeadlineTestTimeProvider();
        var received = Channel.CreateUnbounded<string>();
        await using var probe = await ControlledProbeProcess.StartAsync(clock, TimeSpan.FromSeconds(2),
            onOutput: line => received.Writer.TryWrite(line), inactivity: true);
        for (var i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await probe.SendAsync("out|1|packet");
            Assert.Equal("packet", await received.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)));
        }
        // Four seconds of controlled time have passed against a two-second inactivity deadline.
        Assert.False(probe.Execution.IsCompleted);
        await probe.SendAsync("exit|0");
        var result = await probe.ResultAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task RunAsync_ReturnsStdoutAndExitCode()
    {
        await using var probe = await ControlledProbeProcess.StartAsync();
        await probe.SendAsync("out|1|ffprobe-ok");
        await probe.SendAsync("exit|0");
        var result = await probe.ResultAsync();
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ffprobe-ok", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ReturnsStdoutBeyondLegacyCaptureLimit()
    {
        await using var probe = await ControlledProbeProcess.StartAsync();
        await probe.SendAsync("out|5000|01234567890123456789");
        await probe.SendAsync("exit|0");
        var result = await probe.ResultAsync();
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Stdout.Length > 100000);
        Assert.Equal(5000, CountNonEmptyLines(result.Stdout));
    }

    [Fact]
    public async Task RunAsync_KeepsStderrBounded()
    {
        await using var probe = await ControlledProbeProcess.StartAsync();
        await probe.SendAsync("err|5000|01234567890123456789");
        await probe.SendAsync("exit|0");
        var result = await probe.ResultAsync();
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Stderr.Length >= 40000);
        Assert.True(result.Stderr.Length < 41000);
    }

    [Fact]
    public async Task RunAsync_ReturnsNonZeroExitAndStderr()
    {
        await using var probe = await ControlledProbeProcess.StartAsync();
        await probe.SendAsync("err|1|ffprobe-error");
        await probe.SendAsync("exit|7");
        var result = await probe.ResultAsync();
        Assert.False(result.TimedOut);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("ffprobe-error", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_WhenTimeout_KillsProcessAndReturnsTimeout()
    {
        var clock = new DeadlineTestTimeProvider();
        await using var probe = await ControlledProbeProcess.StartAsync(clock, TimeSpan.FromMilliseconds(100));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var result = await probe.ResultAsync();
        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_WhenExternallyCanceled_StopsProcessAndReturnsCanceledResult()
    {
        await using var probe = await ControlledProbeProcess.StartAsync();
        probe.Cancel(); // Startup handshake has completed; this cancels an actually running child.
        var result = await probe.ResultAsync();
        Assert.True(result.Canceled);
        Assert.False(result.TimedOut);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task Output_reader_honors_cancellation_before_consuming_records()
    {
        using var input = new StringReader("must not be consumed");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FfprobeRunnerService.ReadOutputLinesAsync(
            input, _ => Assert.Fail("A canceled reader consumed output."), 65536, cancellation.Token));
    }

    [Fact]
    public async Task RunAsync_DrainsBothPipesAndTheirFinalUnterminatedRecordsBeforeReturning()
    {
        await using var probe = await ControlledProbeProcess.StartAsync();
        await probe.SendAsync("both|2000|record");
        await probe.SendAsync("raw|1|last-record");
        await probe.SendAsync("exit|0");
        var result = await probe.ResultAsync();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2001, CountNonEmptyLines(result.Stdout));
        Assert.Equal(2001, CountNonEmptyLines(result.Stderr));
        Assert.EndsWith("last-record" + Environment.NewLine, result.Stdout);
        Assert.EndsWith("last-record" + Environment.NewLine, result.Stderr);
    }

    [Fact]
    public async Task RunAsync_CancellationWhileFinalOutputIsConsumedCannotReturnSuccess()
    {
        ControlledProbeProcess? owner = null;
        await using var probe = await ControlledProbeProcess.StartAsync(onOutput: _ => owner!.Cancel());
        owner = probe;
        await probe.SendAsync("out-exit|0|final-record");
        var result = await probe.ResultAsync();
        Assert.True(result.Canceled);
        Assert.False(result.TimedOut);
        Assert.Null(result.ExitCode);
    }

    private static int CountNonEmptyLines(string value) =>
        value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length;
}
