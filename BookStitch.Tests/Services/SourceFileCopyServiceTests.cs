using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Security.Cryptography;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SourceFileCopyServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(300000)]
    public async Task Source_read_notification_occurs_once_only_after_nonempty_data_is_read(int size)
    {
        using var folder = new TemporaryFolder();
        var target = Path.Combine(folder.Path, "target.mp3");
        var notifications = 0;
        var source = new MemoryStream(new byte[size]);
        await SourceFileCopyService.CopyAtomicallyCoreAsync("fixture", target, CancellationToken.None, _ => source,
            sourceReadStarted: () =>
            {
                Assert.True(source.Position > 0);
                Assert.True(File.Exists(target + ".copying"));
                Assert.False(File.Exists(target));
                notifications++;
            });
        Assert.Equal(size == 0 ? 0 : 1, notifications);
        Assert.Equal(size, new FileInfo(target).Length);
    }

    [Fact]
    public async Task Pause_after_successful_first_read_keeps_the_read_notification_but_preserves_the_previous_target()
    {
        using var folder = new TemporaryFolder();
        using var cancellation = new CancellationTokenSource();
        var target = Path.Combine(folder.Path, "target.mp3");
        File.WriteAllBytes(target, [9]);
        var notifications = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SourceFileCopyService.CopyAtomicallyCoreAsync(
            "fixture", target, cancellation.Token, _ => new CancelAfterReadSource(cancellation),
            sourceReadStarted: () => notifications++));
        Assert.Equal(1, notifications);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(target));
        Assert.False(File.Exists(target + ".copying"));
    }

    private sealed class CancelAfterReadSource(CancellationTokenSource cancellation) : MemoryStream(new byte[] { 1 })
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            cancellation.Cancel();
            return read;
        }
    }

    [Fact]
    public async Task Failed_source_open_does_not_announce_a_read()
    {
        using var folder = new TemporaryFolder();
        var notifications = 0;
        await Assert.ThrowsAsync<IOException>(() => SourceFileCopyService.CopyAtomicallyCoreAsync("fixture",
            Path.Combine(folder.Path, "target.mp3"), CancellationToken.None, _ => throw new IOException("fixture"),
            sourceReadStarted: () => notifications++));
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task Locked_temporary_cleanup_does_not_mask_cancellation_and_resume_retries_cleanup()
    {
        using var folder = new TemporaryFolder();
        var target = Path.Combine(folder.Path, "target.mp3");
        var sourcePath = Path.Combine(folder.Path, "source.mp3");
        File.WriteAllBytes(target, [9]);
        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        using var cancellation = new CancellationTokenSource();
        var source = new PausingSource();
        var copy = SourceFileCopyService.CopyAtomicallyCoreAsync(sourcePath, target,
            cancellation.Token, _ => source, _ => throw new IOException("temporary delete lock"));
        await source.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copy);
        Assert.True(File.Exists(target + ".copying"));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(target));
        await SourceFileCopyService.CopyAtomicallyAsync(sourcePath, target, CancellationToken.None);
        Assert.False(File.Exists(target + ".copying"));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
    }

    [Fact]
    public async Task Replacing_a_read_only_project_copy_does_not_change_source_attributes()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.mp3");
        var target = Path.Combine(folder.Path, "target.mp3");
        File.WriteAllBytes(source, [1, 2, 3]);
        File.WriteAllBytes(target, [9, 8, 7]);
        File.SetAttributes(source, FileAttributes.ReadOnly);
        File.SetAttributes(target, FileAttributes.ReadOnly);
        try
        {
            await SourceFileCopyService.CopyAtomicallyAsync(source, target, CancellationToken.None);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
            Assert.True((File.GetAttributes(source) & FileAttributes.ReadOnly) != 0);
            Assert.False(File.Exists(target + ".copying"));
        }
        finally { File.SetAttributes(source, FileAttributes.Normal); File.SetAttributes(target, FileAttributes.Normal); }
    }

    [Fact]
    public async Task Copy_computes_the_persisted_fingerprint_in_the_source_read_pass()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.mp3");
        var target = Path.Combine(folder.Path, "target.mp3");
        byte[] content = [1, 2, 3, 4];
        File.WriteAllBytes(source, content);
        var hash = await SourceFileCopyService.CopyAtomicallyAsync(source, target, CancellationToken.None);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), hash);
        Assert.Equal(content, File.ReadAllBytes(target));
        Assert.False(File.Exists(target + ".copying"));
    }

    [Fact]
    public async Task Cancellation_during_a_file_cleans_the_partial_and_preserves_the_previous_target()
    {
        using var folder = new TemporaryFolder();
        var target = Path.Combine(folder.Path, "target.mp3");
        File.WriteAllBytes(target, [9, 8, 7]);
        using var cancellation = new CancellationTokenSource();
        var source = new PausingSource();
        var copy = SourceFileCopyService.CopyAtomicallyCoreAsync("source", target, cancellation.Token, _ => source);
        await source.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(File.Exists(target + ".copying"));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copy);
        Assert.False(File.Exists(target + ".copying"));
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(target));
    }

    private sealed class PausingSource : MemoryStream
    {
        private bool _readOnce;
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_readOnce)
            {
                _readOnce = true;
                buffer.Span[0] = 1;
                return 1;
            }
            Waiting.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
