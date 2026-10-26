using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FfmpegRunnerServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_ProgressFailureStopsProcessAndIsReturnedToCaller(bool stderr)
    {
        var expected = new InvalidOperationException("Progress consumer failed");
        var task = Assert.ThrowsAsync<InvalidOperationException>(() => new FfmpegRunnerService().RunAsync(
            GetCommandProcessor(),
            ["/c", "echo out_time_us=1000000" + (stderr ? " 1>&2" : "") + " & ping -n 20 127.0.0.1 > nul"],
            CancellationToken.None,
            _ => throw expected));

        await AwaitWithGuardAsync(task);
        Assert.Same(expected, (await task).InnerException);
    }

    [Theory]
    [InlineData("out_time_us=9223372036854775807")]
    [InlineData("out_time_ms=-9223372036854775808")]
    [InlineData("out_time=2147483647:59:59.9")]
    [InlineData("out_time=00:00:999999999999999999999")]
    public async Task RunAsync_IgnoresOutOfRangeProgressAndContinuesWithValidRecords(string invalid)
    {
        var progress = new List<TimeSpan>();
        var result = await new FfmpegRunnerService().RunAsync(GetCommandProcessor(),
            ["/c", "echo " + invalid + "& echo out_time_us=1500000"],
            CancellationToken.None, progress.Add);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([TimeSpan.FromSeconds(1.5)], progress);
    }

    [Theory]
    [InlineData("out_time_us=1500000", 1.5)]
    [InlineData("out_time_ms=1500000", 1.5)]
    [InlineData("out_time=01:02:03.5", 3723.5)]
    [InlineData("time=600000:00:00.0", 2160000000d)]
    public async Task RunAsync_PreservesValidProgressTimes(string line, double seconds)
    {
        var progress = new List<TimeSpan>();
        var result = await new FfmpegRunnerService().RunAsync(GetCommandProcessor(),
            ["/c", "echo " + line], CancellationToken.None, progress.Add);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal([TimeSpan.FromSeconds(seconds)], progress);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_CapsDiagnosticOutputWithoutExceedingLimit(bool stderr)
    {
        var result = await new FfmpegRunnerService().RunAsync(GetCommandProcessor(),
            ["/c", "for /L %i in (1,1,5000) do @echo 01234567890123456789012345678901234567890123456789" + (stderr ? " 1>&2" : "")],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(40000, (stderr ? result.Stderr : result.Stdout).Length);
    }

    [Fact]
    public async Task RunAsync_ReturnsStdoutAndExitCode()
    {
        var runner = new FfmpegRunnerService();

        var result = await runner.RunAsync(
            GetCommandProcessor(),
            ["/c", "echo", "ffmpeg-ok"],
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ffmpeg-ok", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ReturnsNonZeroExitAndStderr()
    {
        var runner = new FfmpegRunnerService();

        var result = await runner.RunAsync(
            GetCommandProcessor(),
            ["/c", "echo ffmpeg-error 1>&2 & exit /b 7"],
            CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("ffmpeg-error", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_WhenExternallyCanceled_ReturnsWithinBoundedTime()
    {
        var runner = new FfmpegRunnerService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var task = Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(
                GetCommandProcessor(),
                ["/c", "ping -n 6 127.0.0.1 > nul"],
                cancellation.Token));

        await AwaitWithGuardAsync(task);
    }

    private static async Task AwaitWithGuardAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(task, completed);
        await task;
    }

    private static string GetCommandProcessor() =>
        Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
}
