using BookStitch.Services;
using System.Diagnostics;
using System.Text;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SingleInstanceServiceTests
{
    [Fact]
    public void Second_instance_is_rejected_and_normal_disposal_allows_a_new_instance()
    {
        var name = @"Local\BookStitch-Test-" + Guid.NewGuid().ToString("N");
        using (var first = SingleInstanceService.TryAcquire(name))
        {
            Assert.NotNull(first);
            Assert.Null(SingleInstanceService.TryAcquire(name));
        }
        using var next = SingleInstanceService.TryAcquire(name);
        Assert.NotNull(next);
    }

    [Fact]
    public async Task Separate_process_holds_gate_and_crash_does_not_leave_a_stale_lock()
    {
        var name = @"Local\BookStitch-Test-" + Guid.NewGuid().ToString("N");
        var script = "$mutex = [System.Threading.Mutex]::new($true, '" + name + "'); [Console]::WriteLine('ready'); [Console]::Out.Flush(); [System.Threading.Thread]::Sleep(300000); $mutex.Dispose()";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardInput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var child = Process.Start(start)!;
        try
        {
            Assert.Equal("ready", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Null(SingleInstanceService.TryAcquire(name));
            child.Kill();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            // The process handle can signal exit before kernel mutex abandonment is observable.
            // Poll on this same thread so a successful acquisition is also disposed by its owner.
            SingleInstanceService? recovered = null;
            var timer = Stopwatch.StartNew();
            while (recovered is null && timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                recovered = SingleInstanceService.TryAcquire(name);
                if (recovered is null) Thread.Sleep(20);
            }
            using (recovered) Assert.NotNull(recovered);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            child.WaitForExit();
        }
    }
}
