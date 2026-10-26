using BookStitch.Services;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PerformanceToolContextServiceTests
{
    [Fact]
    public void ExtractVersion_ParsesFfmpegVersionLine()
    {
        const string output =
            "ffmpeg version 7.1.1-full_build-www.gyan.dev Copyright (c) 2000-2026 the FFmpeg developers\r\n" +
            "built with gcc";

        var version = PerformanceToolContextService.ExtractVersion(output);

        Assert.Equal("7.1.1-full_build-www.gyan.dev", version);
    }

    [Fact]
    public void ExtractVersion_ParsesFfprobeVersionLine()
    {
        const string output =
            "ffprobe version N-117777-gabcdef Copyright (c) 2007-2026 the FFmpeg developers";

        var version = PerformanceToolContextService.ExtractVersion(output);

        Assert.Equal("N-117777-gabcdef", version);
    }

    [Fact]
    public async Task CaptureAsync_MissingExecutableIsUnavailableAndDoesNotPersistFullPath()
    {
        var service = new PerformanceToolContextService();
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "private-user-folder",
            "ffprobe.exe");

        var snapshot = await service.CaptureAsync(missingPath);

        Assert.False(snapshot.Available);
        Assert.Equal("ffprobe.exe", snapshot.PathHint);
        Assert.Null(snapshot.Version);
        Assert.Null(snapshot.FileVersion);
        Assert.Null(snapshot.Architecture);
    }

    [Fact]
    public async Task CaptureAsync_InvalidExecutableIsFailureIsolated()
    {
        var root = CreateTemporaryRoot();
        var fakeExecutable = Path.Combine(root, "ffmpeg.exe");

        try
        {
            await File.WriteAllTextAsync(fakeExecutable, "not a Windows executable");

            var service = new PerformanceToolContextService(TimeSpan.FromMilliseconds(250));
            var snapshot = await service.CaptureAsync(fakeExecutable);

            Assert.True(snapshot.Available);
            Assert.Equal("ffmpeg.exe", snapshot.PathHint);
            Assert.Null(snapshot.Version);
            Assert.Null(snapshot.Architecture);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task CaptureAsync_PreCancelledRequestDoesNotStartVersionProbe()
    {
        var root = CreateTemporaryRoot();
        var fakeExecutable = Path.Combine(root, "ffprobe.exe");

        try
        {
            File.Copy(GetCommandProcessor(), fakeExecutable);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var service = new PerformanceToolContextService(
                null,
                (_, _) => throw new InvalidOperationException("Version probe must not run."));
            var snapshot = await service.CaptureAsync(fakeExecutable, cancellation.Token);

            Assert.True(snapshot.Available);
            Assert.Equal("ffprobe.exe", snapshot.PathHint);
            Assert.Null(snapshot.Version);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task CaptureAsync_ExistingPathIsCachedWithinServiceInstance()
    {
        var root = CreateTemporaryRoot();
        var fakeExecutable = Path.Combine(root, "ffmpeg.exe");

        try
        {
            File.Copy(GetCommandProcessor(), fakeExecutable);
            var probeCalls = 0;

            var service = new PerformanceToolContextService(
                TimeSpan.FromMilliseconds(250),
                (_, _) =>
                {
                    probeCalls++;
                    return Task.FromResult<string?>("ffmpeg version cached-test");
                });
            var first = await service.CaptureAsync(fakeExecutable);

            File.Delete(fakeExecutable);

            var second = await service.CaptureAsync(fakeExecutable);

            Assert.Same(first, second);
            Assert.Equal(1, probeCalls);
            Assert.True(second.Available);
            Assert.Equal("ffmpeg.exe", second.PathHint);
            Assert.Equal("cached-test", second.Version);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task CaptureAsync_PreCancelledRequestDoesNotPoisonCachedSnapshot()
    {
        var root = CreateTemporaryRoot();
        var fakeExecutable = Path.Combine(root, "ffmpeg.exe");

        try
        {
            File.Copy(GetCommandProcessor(), fakeExecutable);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var probeCalls = 0;
            var service = new PerformanceToolContextService(
                TimeSpan.FromMilliseconds(250),
                (_, _) =>
                {
                    probeCalls++;
                    return Task.FromResult<string?>("ffmpeg version later-call");
                });

            var canceledCallerSnapshot = await service.CaptureAsync(fakeExecutable, cancellation.Token);
            var cachedSnapshot = await service.CaptureAsync(fakeExecutable);

            Assert.Null(canceledCallerSnapshot.Version);
            Assert.Equal("later-call", cachedSnapshot.Version);
            Assert.Equal(1, probeCalls);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public void Constructor_RejectsNonPositiveProbeTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PerformanceToolContextService(TimeSpan.Zero));
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "BookStitch.Tests",
            nameof(PerformanceToolContextServiceTests),
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);
        return root;
    }

    private static string GetCommandProcessor() =>
        Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

    private static void DeleteTemporaryRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Test cleanup is best effort.
        }
    }
}
