using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BookStitch.Services;

public sealed record ProjectFolderLayoutDiagnosticContext(
    string EnvironmentName,
    string ProjectRootFolder);

public static class ProjectFolderLayoutDiagnostics
{
    private static readonly object Sync = new();
    private static Action<string, string>? _writeApplicationEvent;
    private static ProjectFolderLayoutDiagnosticContext? _context;
    private static readonly AsyncLocal<ProjectFolderLayoutDiagnosticConfiguration?> AmbientConfiguration = new();

    public static void Configure(
        Action<string, string>? writeApplicationEvent,
        ProjectFolderLayoutDiagnosticContext? context)
    {
        lock (Sync)
        {
            _writeApplicationEvent = writeApplicationEvent;
            _context = context;
        }

        AmbientConfiguration.Value = writeApplicationEvent is null
            ? null
            : new ProjectFolderLayoutDiagnosticConfiguration(writeApplicationEvent, context);
    }

    public static void Reset() => Configure(null, null);

    public static IDisposable BeginScope(
        Action<string, string> writeApplicationEvent,
        ProjectFolderLayoutDiagnosticContext? context)
    {
        ArgumentNullException.ThrowIfNull(writeApplicationEvent);

        var previous = AmbientConfiguration.Value;
        AmbientConfiguration.Value = new ProjectFolderLayoutDiagnosticConfiguration(
            writeApplicationEvent,
            context);

        return new AmbientConfigurationScope(previous);
    }

    public static void RecordEnsureProjectFolders(string projectFolder)
    {
        Action<string, string>? writer;
        ProjectFolderLayoutDiagnosticContext? context;
        var ambient = AmbientConfiguration.Value;
        if (ambient is not null)
        {
            writer = ambient.WriteApplicationEvent;
            context = ambient.Context;
        }
        else
        {
            lock (Sync)
            {
                writer = _writeApplicationEvent;
                context = _context;
            }
        }

        if (writer is null)
            return;

        try
        {
            var fullProjectFolder = NormalizePath(projectFolder);
            var projectRoot = NormalizePath(context?.ProjectRootFolder);
            if (string.IsNullOrWhiteSpace(projectRoot) ||
                string.IsNullOrWhiteSpace(fullProjectFolder) ||
                IsPathInside(fullProjectFolder, projectRoot))
            {
                return;
            }

            writer(
                "PROJECT LAYOUT OUTSIDE ROOT",
                "EnsureProjectFolders" +
                $" | ProjectFolder={fullProjectFolder}" +
                $" | Environment={context?.EnvironmentName ?? "Unknown"}" +
                $" | ProjectRoot={projectRoot}" +
                " | InsideProjectRoot=False" +
                $" | Caller={CreateCallerSummary()}");
        }
        catch
        {
            // Diagnostics must never change project layout behavior.
        }
    }

    private sealed record ProjectFolderLayoutDiagnosticConfiguration(
        Action<string, string> WriteApplicationEvent,
        ProjectFolderLayoutDiagnosticContext? Context);

    private sealed class AmbientConfigurationScope(
        ProjectFolderLayoutDiagnosticConfiguration? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            AmbientConfiguration.Value = previous;
        }
    }

    private static string CreateCallerSummary()
    {
        try
        {
            var frames = new StackTrace(skipFrames: 2, fNeedFileInfo: false)
                .GetFrames() ?? [];

            return string.Join(" <- ", frames
                .Select(frame => frame.GetMethod())
                .Where(method => method is not null)
                .Select(method => method!.DeclaringType?.Name + "." + method.Name)
                .Where(name =>
                    !string.IsNullOrWhiteSpace(name) &&
                    !name.StartsWith(nameof(ProjectFolderLayout), StringComparison.Ordinal) &&
                    !name.StartsWith(nameof(ProjectFolderLayoutDiagnostics), StringComparison.Ordinal))
                .Take(8));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try { return Path.GetFullPath(path); }
        catch { return path.Trim(); }
    }

    private static bool IsPathInside(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return false;

        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
