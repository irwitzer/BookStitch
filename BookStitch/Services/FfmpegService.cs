using System.Diagnostics;
using System.IO;
using BookStitch.Models;

namespace BookStitch.Services;

public class FfmpegService
{
    public FfmpegToolStatus DetectTools(AppSettings settings)
    {
        var ffmpegPath = FindTool("ffmpeg.exe", settings.FfmpegPath);
        var ffprobePath = FindTool("ffprobe.exe", settings.FfprobePath);

        return new FfmpegToolStatus
        {
            FfmpegAvailable = !string.IsNullOrWhiteSpace(ffmpegPath),
            FfmpegPath = ffmpegPath,
            FfprobeAvailable = !string.IsNullOrWhiteSpace(ffprobePath),
            FfprobePath = ffprobePath
        };
    }

    public FfmpegToolStatus DetectToolsFromFfmpegPath(string ffmpegPath)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            return new FfmpegToolStatus();

        var folder = Path.GetDirectoryName(ffmpegPath);

        if (string.IsNullOrWhiteSpace(folder))
            return new FfmpegToolStatus();

        var ffprobePath = Path.Combine(folder, "ffprobe.exe");

        return new FfmpegToolStatus
        {
            FfmpegAvailable = CanRunTool(ffmpegPath),
            FfmpegPath = ffmpegPath,
            FfprobeAvailable = File.Exists(ffprobePath) && CanRunTool(ffprobePath),
            FfprobePath = File.Exists(ffprobePath) ? ffprobePath : null
        };
    }

    public async Task<int?> InstallWithWingetAsync()
    {
        try
        {
            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c winget uninstall --id Gyan.FFmpeg -e & winget install --id Gyan.FFmpeg -e --source winget --force",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            process.Start();

            await process.WaitForExitAsync();

            return process.ExitCode;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindTool(string fileName, string? configuredPath)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredPath))
            candidates.Add(configuredPath);

        candidates.Add(Path.Combine(AppContext.BaseDirectory, fileName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Tools", fileName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg", fileName));

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ffmpeg",
            "bin",
            fileName));

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "ffmpeg",
            "bin",
            fileName));

        AddWingetPackageCandidates(candidates, fileName);

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var pathFolder in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                candidates.Add(Path.Combine(pathFolder.Trim(), fileName));
            }
            catch
            {
                // Ungültige PATH Einträge ignorieren.
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (!File.Exists(candidate))
                continue;

            if (CanRunTool(candidate))
                return candidate;
        }

        return null;
    }

    private static void AddWingetPackageCandidates(List<string> candidates, string fileName)
    {
        try
        {
            var packagesFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WinGet",
                "Packages");

            if (!Directory.Exists(packagesFolder))
                return;

            foreach (var packageFolder in Directory.EnumerateDirectories(packagesFolder, "Gyan.FFmpeg*"))
            {
                foreach (var candidate in Directory.EnumerateFiles(
                             packageFolder,
                             fileName,
                             SearchOption.AllDirectories))
                {
                    candidates.Add(candidate);
                }
            }
        }
        catch
        {
            // Ein unzugänglicher oder noch nicht vollständig angelegter WinGet-Ordner
            // darf die normale Werkzeugerkennung nicht blockieren.
        }
    }

    private static bool CanRunTool(string path)
    {
        try
        {
            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();

            if (!process.WaitForExit(2500))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignorieren.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

public class FfmpegToolStatus
{
    public bool FfmpegAvailable { get; set; }

    public string? FfmpegPath { get; set; }

    public bool FfprobeAvailable { get; set; }

    public string? FfprobePath { get; set; }

    public bool IsComplete => FfmpegAvailable && FfprobeAvailable;

    public string StatusText
    {
        get
        {
            if (IsComplete)
                return "FFmpeg bereit.";

            if (!FfmpegAvailable && !FfprobeAvailable)
                return "FFmpeg nicht gefunden. ffmpeg.exe und ffprobe.exe fehlen.";

            if (!FfmpegAvailable)
                return "FFmpeg unvollständig. ffmpeg.exe fehlt.";

            return "FFmpeg unvollständig. ffprobe.exe fehlt.";
        }
    }
}