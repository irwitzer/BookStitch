using BookStitch.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection.PortableExecutable;

namespace BookStitch.Services;

/// <summary>
/// Captures one-shot metadata for a configured FFmpeg/FFprobe executable.
/// The caller is expected to invoke it only while performance diagnostics are enabled.
/// No full executable path is persisted; PathHint contains only the file name.
/// </summary>
public sealed class PerformanceToolContextService
{
    private static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _probeTimeout;
    private readonly Func<string, CancellationToken, Task<string?>> _versionProbe;
    private readonly ConcurrentDictionary<string, Lazy<Task<PerformanceToolSnapshot>>> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public PerformanceToolContextService(TimeSpan? probeTimeout = null)
        : this(probeTimeout, null)
    {
    }

    internal PerformanceToolContextService(
        TimeSpan? probeTimeout,
        Func<string, CancellationToken, Task<string?>>? versionProbe)
    {
        _probeTimeout = probeTimeout ?? DefaultProbeTimeout;

        if (_probeTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(probeTimeout));

        _versionProbe = versionProbe ?? CreateDefaultVersionProbe();
    }

    public Task<PerformanceToolSnapshot> CaptureAsync(
        string? executablePath,
        CancellationToken cancellationToken = default)
    {
        var pathHint = GetPathHint(executablePath);

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return Task.FromResult(new PerformanceToolSnapshot
            {
                Available = false,
                PathHint = pathHint
            });
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(executablePath);
        }
        catch
        {
            return Task.FromResult(new PerformanceToolSnapshot
            {
                Available = false,
                PathHint = pathHint
            });
        }

        if (_cache.TryGetValue(fullPath, out var cached))
            return cached.Value;

        if (!File.Exists(fullPath))
        {
            return Task.FromResult(new PerformanceToolSnapshot
            {
                Available = false,
                PathHint = GetPathHint(fullPath)
            });
        }

        if (cancellationToken.IsCancellationRequested)
            return CaptureCoreAsync(fullPath, cancellationToken);

        return _cache.GetOrAdd(
                fullPath,
                path => new Lazy<Task<PerformanceToolSnapshot>>(
                    () => CaptureCoreAsync(path, CancellationToken.None),
                    LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
    }

    private async Task<PerformanceToolSnapshot> CaptureCoreAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        var fileVersion = TryGetFileVersion(fullPath);
        var architecture = TryGetPeArchitecture(fullPath);
        string? version = null;

        if (!cancellationToken.IsCancellationRequested && architecture is not null)
            version = await TryReadCommandVersionAsync(fullPath, cancellationToken).ConfigureAwait(false);

        return new PerformanceToolSnapshot
        {
            Available = true,
            Version = version,
            FileVersion = fileVersion,
            Architecture = architecture,
            PathHint = GetPathHint(fullPath)
        };
    }

    public static string? ExtractVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var lines = output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
            return null;

        var line = lines.FirstOrDefault(candidate =>
                candidate.Contains(" version ", StringComparison.OrdinalIgnoreCase))
            ?? lines[0];

        var versionMarker = line.IndexOf(" version ", StringComparison.OrdinalIgnoreCase);
        var value = versionMarker >= 0
            ? line[(versionMarker + " version ".Length)..].Trim()
            : line.Trim();

        var copyrightMarker = value.IndexOf(" Copyright", StringComparison.OrdinalIgnoreCase);
        if (copyrightMarker > 0)
            value = value[..copyrightMarker].Trim();

        if (value.Length > 160)
            value = value[..160].Trim();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private async Task<string?> TryReadCommandVersionAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_probeTimeout);

            var versionText = await _versionProbe(executablePath, timeout.Token)
                .ConfigureAwait(false);

            return ExtractVersion(versionText);
        }
        catch
        {
            // Tool context is diagnostic metadata only. A missing, blocked or hanging
            // version probe must never affect the BookStitch workflow.
            return null;
        }
    }

    private static string? TryGetFileVersion(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return NormalizeOptional(info.FileVersion);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetPeArchitecture(string executablePath)
    {
        try
        {
            using var stream = new FileStream(
                executablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            using var peReader = new PEReader(stream);
            return peReader.PEHeaders.CoffHeader.Machine.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetPathHint(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        try
        {
            return NormalizeOptional(Path.GetFileName(executablePath.Trim()));
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static Func<string, CancellationToken, Task<string?>> CreateDefaultVersionProbe()
    {
        var runner = new FfmpegRunnerService();

        return async (executablePath, cancellationToken) =>
        {
            var result = await runner.RunAsync(
                    executablePath,
                    ["-version"],
                    cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(result.Stdout)
                ? result.Stderr
                : result.Stdout;
        };
    }
}
