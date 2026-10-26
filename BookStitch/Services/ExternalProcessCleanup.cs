using System.Diagnostics;

namespace BookStitch.Services;

internal static class ExternalProcessCleanup
{
    public static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan StreamDrainTimeout = TimeSpan.FromSeconds(2);

    public static async Task WaitForExitBoundedAsync(Process process, TimeSpan timeout)
    {
        try
        {
            using var timeoutSource = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch
        {
            // Cleanup must never replace the original timeout/cancellation outcome.
        }
    }

    public static async Task DrainStreamsBoundedAsync(
        Task stdoutClosed,
        Task stderrClosed,
        TimeSpan timeout)
    {
        try
        {
            await Task.WhenAll(stdoutClosed, stderrClosed)
                .WaitAsync(timeout)
                .ConfigureAwait(false);
        }
        catch
        {
            // DataReceived end markers can be delayed or absent after a hard kill.
        }
    }

    public static async Task StopAfterCancellationAsync(
        Process process,
        Task stdoutClosed,
        Task stderrClosed)
    {
        TryKillProcessTree(process);

        if (!HasExited(process))
            await WaitForExitBoundedAsync(process, ExitTimeout).ConfigureAwait(false);

        await DrainStreamsBoundedAsync(
                stdoutClosed,
                stderrClosed,
                StreamDrainTimeout)
            .ConfigureAwait(false);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process may already be gone or inaccessible.
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }
}
