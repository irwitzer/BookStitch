using BookStitch.Models;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BookStitch.Services;

/// <summary>
/// Reads CD-DA sectors through the Windows CD-ROM interface and streams the
/// resulting 44.1 kHz, 16-bit stereo PCM directly into FFmpeg. No intermediate
/// WAV file is created. Version 1 deliberately performs a simple sequential read
/// and leaves advanced secure-ripping strategies for a later version.
/// </summary>
public sealed record AudioDiscRippedTrack(
    int DiscNumber,
    int GlobalIndex,
    int TrackNumber,
    string FilePath,
    TimeSpan Duration);

public sealed class AudioDiscRipService
{
    private readonly AudioDiscSectorReadRetryService _sectorReadRetryService;

    public AudioDiscRipService(AudioDiscSectorReadRetryService? sectorReadRetryService = null)
    {
        _sectorReadRetryService = sectorReadRetryService ?? new AudioDiscSectorReadRetryService();
    }

    private const int RawSectorSize = 2352;
    private const int CookedSectorSize = 2048;
    private const int SectorsPerRead = 16;
    private const int CdLeadInSectors = 150;
    private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromMilliseconds(200);
    private const uint IoctlCdromRawRead = 0x0002403E;

    public async Task<AudioDiscRipResult> RipDiscToFlacAsync(
        AudioDiscProjectManifest manifest,
        AudioDiscProjectManifestDisc disc,
        string ffmpegPath,
        CancellationToken cancellationToken,
        IProgress<AudioDiscRipProgress>? progress = null,
        Func<AudioDiscRippedTrack, CancellationToken, Task>? trackRipped = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(disc);
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);

        if (!File.Exists(ffmpegPath))
            return AudioDiscRipResult.Failed("FFmpeg wurde nicht gefunden.", 0);

        if (disc.Tracks.Count == 0)
            return AudioDiscRipResult.Failed("Die Audio-CD enthält keine vorbereiteten Tracks.", 0);

        ResetLegacyShiftedRipOutputs(manifest);

        var drivePath = BuildDevicePath(disc.SourceDriveRoot);
        using var driveHandle = NativeMethods.CreateFile(
            drivePath,
            NativeMethods.GenericRead,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            0,
            IntPtr.Zero);

        if (driveHandle.IsInvalid)
            return AudioDiscRipResult.Failed(
                $"Das CD-Laufwerk {disc.SourceDriveRoot} konnte nicht zum Lesen geöffnet werden. " +
                new Win32Exception(Marshal.GetLastWin32Error()).Message,
                0);

        var completedTracks = 0;
        var started = Stopwatch.StartNew();

        foreach (var track in disc.Tracks.OrderBy(item => item.TrackNumber))
        {
            if (track.Status == AudioDiscTrackStatus.Ripped &&
                File.Exists(Path.Combine(manifest.ProjectFolder, track.RelativePath)))
            {
                completedTracks++;
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!track.SectorOffset.HasValue || track.SectorCount <= 0)
            {
                return AudioDiscRipResult.Failed(
                    $"Audio-CD {disc.DiscNumber}, Track {track.TrackNumber:00} besitzt keine exakten CD-Sektorinformationen.",
                    completedTracks);
            }

            var finalPath = Path.Combine(manifest.ProjectFolder, track.RelativePath);
            var partPath = finalPath + ".part";
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            TryDelete(partPath);

            try
            {
                track.Status = AudioDiscTrackStatus.Pending;
                track.CompletedUtc = null;
                track.OutputSizeBytes = null;
                track.ErrorMessage = string.Empty;

                await RipTrackAsync(
                    driveHandle,
                    ffmpegPath,
                    ConvertTocSectorToReadLba(track.SectorOffset.Value),
                    track.SectorCount,
                    partPath,
                    cancellationToken,
                    fraction => progress?.Report(new AudioDiscRipProgress(
                        completedTracks,
                        disc.Tracks.Count,
                        track.TrackNumber,
                        fraction,
                        started.Elapsed)),
                    _sectorReadRetryService);

                File.Move(partPath, finalPath, overwrite: true);
                track.Status = AudioDiscTrackStatus.Ripped;
                track.CompletedUtc = DateTime.UtcNow;
                track.OutputSizeBytes = new FileInfo(finalPath).Length;
                track.ErrorMessage = string.Empty;
                completedTracks++;

                progress?.Report(new AudioDiscRipProgress(
                    completedTracks,
                    disc.Tracks.Count,
                    track.TrackNumber,
                    1,
                    started.Elapsed));

                if (trackRipped is not null)
                {
                    await trackRipped(
                        new AudioDiscRippedTrack(
                            disc.DiscNumber,
                            track.GlobalIndex,
                            track.TrackNumber,
                            finalPath,
                            track.Duration),
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                TryDelete(partPath);
                return AudioDiscRipResult.Canceled(completedTracks);
            }
            catch (Exception ex)
            {
                TryDelete(partPath);
                var errorMessage =
                    $"Audio-CD {disc.DiscNumber}, Track {track.TrackNumber:00} konnte nicht gerippt werden: {ex.Message}";
                track.Status = AudioDiscTrackStatus.Failed;
                track.CompletedUtc = null;
                track.OutputSizeBytes = null;
                track.ErrorMessage = errorMessage;
                return AudioDiscRipResult.Failed(errorMessage, completedTracks);
            }
        }

        return AudioDiscRipResult.Success(completedTracks);
    }

    private static async Task RipTrackAsync(
        SafeFileHandle driveHandle,
        string ffmpegPath,
        int startSector,
        int sectorCount,
        string outputPath,
        CancellationToken cancellationToken,
        Action<double> reportFraction,
        AudioDiscSectorReadRetryService sectorReadRetryService)
    {
        using var process = CreateFfmpegProcess(ffmpegPath, outputPath);
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null && stderr.Length < 40_000)
                stderr.AppendLine(args.Data);
        };

        process.Start();
        process.BeginErrorReadLine();

        try
        {
            var sectorsRead = 0;
            var lastProgressReport = Stopwatch.StartNew();
            while (sectorsRead < sectorCount)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var count = Math.Min(SectorsPerRead, sectorCount - sectorsRead);
                var buffer = await sectorReadRetryService.ReadAsync(
                    startSector + sectorsRead,
                    count,
                    (sector, sectors) => ReadSectors(driveHandle, sector, sectors),
                    cancellationToken);
                await process.StandardInput.BaseStream.WriteAsync(buffer, cancellationToken);
                sectorsRead += count;

                if (sectorsRead >= sectorCount || lastProgressReport.Elapsed >= ProgressReportInterval)
                {
                    reportFraction((double)sectorsRead / sectorCount);
                    lastProgressReport.Restart();
                }
            }

            await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var details = stderr.ToString().Trim();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(details)
                    ? $"FFmpeg wurde mit Fehlercode {process.ExitCode} beendet."
                    : details.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(12).Aggregate((a, b) => a + Environment.NewLine + b));
            }
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }


    public static int ConvertTocSectorToReadLba(int tocSectorOffset)
    {
        if (tocSectorOffset < CdLeadInSectors)
            throw new ArgumentOutOfRangeException(
                nameof(tocSectorOffset),
                tocSectorOffset,
                $"Der TOC-Sektor muss mindestens {CdLeadInSectors} betragen.");

        return tocSectorOffset - CdLeadInSectors;
    }

    private static void ResetLegacyShiftedRipOutputs(AudioDiscProjectManifest manifest)
    {
        if (manifest.RawReadAddressingVersion >= AudioDiscRawReadAddressingVersions.Current)
            return;

        foreach (var track in manifest.Discs.SelectMany(item => item.Tracks))
        {
            var finalPath = Path.Combine(manifest.ProjectFolder, track.RelativePath);
            TryDelete(finalPath);
            TryDelete(finalPath + ".part");
            track.Status = AudioDiscTrackStatus.Pending;
            track.CompletedUtc = null;
            track.OutputSizeBytes = null;
            track.ErrorMessage = string.Empty;
        }

        manifest.RawReadAddressingVersion = AudioDiscRawReadAddressingVersions.Current;
        manifest.RipDuration = null;
    }

    private static byte[] ReadSectors(SafeFileHandle driveHandle, int startSector, int sectorCount)
    {
        var request = new RawReadInfo
        {
            DiskOffset = (long)startSector * CookedSectorSize,
            SectorCount = (uint)sectorCount,
            TrackMode = TrackModeType.Cdda
        };

        var buffer = new byte[sectorCount * RawSectorSize];
        if (!NativeMethods.DeviceIoControl(
                driveHandle,
                IoctlCdromRawRead,
                ref request,
                Marshal.SizeOf<RawReadInfo>(),
                buffer,
                buffer.Length,
                out var bytesReturned,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Die CD-Sektoren ab Position {startSector} konnten nicht gelesen werden.");
        }

        if (bytesReturned != buffer.Length)
            throw new IOException($"Das Laufwerk lieferte {bytesReturned} statt {buffer.Length} Bytes.");

        return buffer;
    }

    private static Process CreateFfmpegProcess(string ffmpegPath, string outputPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "warning", "-y",
            "-f", "s16le", "-ar", "44100", "-ac", "2", "-i", "pipe:0",
            "-map", "0:a:0", "-c:a", "flac", "-compression_level", "1",
            "-f", "flac", outputPath
        })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        return process;
    }

    private static string BuildDevicePath(string driveRoot)
    {
        var root = Path.GetPathRoot(driveRoot)?.TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(root) || root.Length < 2 || root[1] != ':')
            throw new ArgumentException("Das Audio-CD-Laufwerk ist ungültig.", nameof(driveRoot));

        return $@"\\.\{char.ToUpperInvariant(root[0])}:";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup is best effort; the caller reports the primary failure.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawReadInfo
    {
        public long DiskOffset;
        public uint SectorCount;
        public TrackModeType TrackMode;
    }

    private enum TrackModeType
    {
        YellowMode2 = 0,
        XaForm2 = 1,
        Cdda = 2,
        RawWithC2AndSubCode = 3,
        RawWithC2 = 4,
        RawWithSubCode = 5
    }

    private static class NativeMethods
    {
        public const uint GenericRead = 0x80000000;
        public const uint FileShareRead = 0x00000001;
        public const uint FileShareWrite = 0x00000002;
        public const uint OpenExisting = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint controlCode,
            ref RawReadInfo inputBuffer,
            int inputBufferSize,
            [Out] byte[] outputBuffer,
            int outputBufferSize,
            out int bytesReturned,
            IntPtr overlapped);
    }
}
