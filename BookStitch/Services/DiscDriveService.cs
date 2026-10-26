using BookStitch.Models;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BookStitch.Services;

public sealed class DiscDriveService
{
    private readonly DiscDriveIdentityService _identityService = new();
    private readonly DiscDriveMeasurementStore _measurementStore;
    private Func<PerformanceTraceService?>? _performanceTraceProvider;

    public DiscDriveService(DiscDriveMeasurementStore? measurementStore = null)
    {
        _measurementStore = measurementStore ?? new DiscDriveMeasurementStore();
    }

    public DiscDriveMeasurementStore MeasurementStore => _measurementStore;

    public void SetPerformanceTraceProvider(Func<PerformanceTraceService?>? provider) =>
        _performanceTraceProvider = provider;

    public Task<IReadOnlyList<DiscDriveInfo>> GetCdDriveShellsAsync(CancellationToken cancellationToken = default) =>
        MeasureAsync(
            string.Empty,
            "drive.enumerate",
            () => (IReadOnlyList<DiscDriveInfo>)GetCdDriveShells(),
            static _ => null,
            static _ => null,
            cancellationToken);

    public async Task<IReadOnlyList<DiscDriveInfo>> GetCdDrivesAsync(CancellationToken cancellationToken = default)
    {
        var shells = await GetCdDriveShellsAsync(cancellationToken).ConfigureAwait(false);
        var probes = shells
            .Select(shell => ProbeDriveAsync(shell.RootPath, "drive.scan", cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(probes).ConfigureAwait(false);
        return results
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.DriveLetter, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DiscDriveInfo?> ProbeDriveAsync(
        string? path,
        string operationName = "drive.probe",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
            return null;

        var totalStartedUtc = DateTimeOffset.UtcNow;
        var totalStopwatch = Stopwatch.StartNew();
        DriveInfo? drive = null;
        var ready = await MeasureAsync(
            root,
            operationName + ".ready",
            () =>
            {
                var candidate = new DriveInfo(root);
                if (candidate.DriveType != DriveType.CDRom)
                    return (Drive: (DriveInfo?)null, IsReady: false, VolumeLabel: string.Empty);

                var isReady = false;
                var volumeLabel = string.Empty;
                try
                {
                    isReady = candidate.IsReady;
                    if (isReady)
                        volumeLabel = candidate.VolumeLabel?.Trim() ?? string.Empty;
                }
                catch
                {
                    isReady = false;
                }

                return (Drive: (DriveInfo?)candidate, IsReady: isReady, VolumeLabel: volumeLabel);
            },
            static result => result.IsReady,
            static result => result.IsReady,
            cancellationToken).ConfigureAwait(false);

        drive = ready.Drive;
        if (drive is null)
        {
            totalStopwatch.Stop();
            RecordProbeTotalMeasurement(root, operationName, totalStartedUtc, totalStopwatch.Elapsed, null);
            return null;
        }

        var mediaKind = await MeasureAsync(
            root,
            operationName + ".classification",
            () => ClassifyMedia(root, ready.IsReady),
            kind => ready.IsReady,
            kind => kind != DiscMediaKind.Empty,
            cancellationToken).ConfigureAwait(false);

        var diagnostics = await MeasureAsync(
            root,
            operationName + ".diagnostics",
            () => ReadDriveDiagnostics(root),
            _ => ready.IsReady,
            _ => ready.IsReady,
            cancellationToken).ConfigureAwait(false);

        var info = new DiscDriveInfo(
            root,
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            ready.IsReady,
            ready.VolumeLabel,
            mediaKind,
            diagnostics.DriveName,
            diagnostics.DevicePath,
            IsChecking: false,
            StableId: diagnostics.StableId,
            IdentityKind: diagnostics.IdentityKind,
            StorageSerialNumber: diagnostics.StorageSerialNumber,
            PnpDeviceInstanceId: diagnostics.PnpDeviceInstanceId,
            Vendor: diagnostics.Vendor,
            Product: diagnostics.Product,
            Revision: diagnostics.Revision,
            HardwareFingerprint: diagnostics.HardwareFingerprint);
        totalStopwatch.Stop();
        RecordProbeTotalMeasurement(root, operationName, totalStartedUtc, totalStopwatch.Elapsed, info);
        return info;
    }

    private void RecordProbeTotalMeasurement(
        string root,
        string operationName,
        DateTimeOffset startedUtc,
        TimeSpan elapsed,
        DiscDriveInfo? info)
    {
        var normalizedRoot = DiscDriveConfigurationService.NormalizeRootPath(root);
        var firstTouch = _measurementStore.Snapshot().Any(item =>
            string.Equals(item.DriveRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
            item.StartedUtc >= startedUtc &&
            item.IsFirstTouchInSession);
        var measurement = new DiscDriveMeasurement(
            normalizedRoot,
            CreateDriveReference(normalizedRoot),
            operationName + ".total",
            startedUtc,
            elapsed.TotalMilliseconds,
            info?.StatusText ?? "Nicht verfügbar",
            info is not null,
            false,
            false,
            info?.IsReady,
            info is null ? null : info.IsReady,
            firstTouch);
        _measurementStore.Record(measurement);
        RecordMeasurement(measurement);
    }

    public Task<bool> IsCdDrivePathAsync(string? sourceFolder, CancellationToken cancellationToken = default) =>
        MeasureAsync(
            sourceFolder ?? string.Empty,
            "drive.path-type",
            () => IsCdDrivePath(sourceFolder),
            static _ => null,
            static _ => null,
            cancellationToken);

    public Task<bool> IsDiscSourceReadyAsync(string? sourceFolder, CancellationToken cancellationToken = default) =>
        MeasureAsync(
            sourceFolder ?? string.Empty,
            "drive.source-ready",
            () => IsDiscSourceReady(sourceFolder),
            static ready => ready,
            static ready => ready,
            cancellationToken);

    public Task<bool> TryEjectDiscAsync(string? sourceFolder, CancellationToken cancellationToken = default) =>
        MeasureAsync(
            sourceFolder ?? string.Empty,
            "drive.eject",
            () => TryEjectDisc(sourceFolder),
            static _ => null,
            static _ => null,
            cancellationToken);

    public Task<DiscDriveInfo?> GetDriveDiagnosticsForPathAsync(string? path, CancellationToken cancellationToken = default) =>
        MeasureAsync(
            path ?? string.Empty,
            "drive.diagnostics",
            () => GetDriveDiagnosticsForPath(path),
            static info => info?.IsReady,
            static info => info is null ? null : info.IsReady,
            cancellationToken);

    public Task<DiscMediaKind> GetMediaKindForPathAsync(string? path, CancellationToken cancellationToken = default) =>
        MeasureAsync(
            path ?? string.Empty,
            "drive.media-classification",
            () => GetMediaKindForPath(path),
            static kind => kind == DiscMediaKind.Empty ? false : null,
            static kind => kind == DiscMediaKind.Empty ? false : true,
            cancellationToken);

    public Task<T> MeasureOperationAsync<T>(
        string? driveRoot,
        string method,
        Func<T> action,
        CancellationToken cancellationToken = default) =>
        MeasureAsync(
            driveRoot ?? string.Empty,
            method,
            action,
            static _ => null,
            static _ => null,
            cancellationToken);

    public void RecordMeasurement(DiscDriveMeasurement measurement)
    {
        try
        {
            _performanceTraceProvider?.Invoke()?.RecordDriveMeasurement(measurement);
        }
        catch
        {
            // Performance diagnostics are observational and must never alter drive behavior.
        }
    }

    private async Task<T> MeasureAsync<T>(
        string driveRoot,
        string method,
        Func<T> action,
        Func<T, bool?> readySelector,
        Func<T, bool?> mediumSelector,
        CancellationToken cancellationToken)
    {
        var normalizedRoot = DiscDriveConfigurationService.NormalizeRootPath(driveRoot);
        var startedUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var firstTouch = string.IsNullOrWhiteSpace(normalizedRoot)
            ? false
            : _measurementStore.MarkAndCheckFirstTouch(normalizedRoot);

        try
        {
            var result = await DiscDriveOperationService.ExecuteAsync(driveRoot, action, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            var measurement = new DiscDriveMeasurement(
                normalizedRoot,
                CreateDriveReference(normalizedRoot),
                method,
                startedUtc,
                stopwatch.Elapsed.TotalMilliseconds,
                "OK",
                true,
                false,
                false,
                readySelector(result),
                mediumSelector(result),
                firstTouch);
            _measurementStore.Record(measurement);
            RecordMeasurement(measurement);
            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            var measurement = new DiscDriveMeasurement(
                normalizedRoot,
                CreateDriveReference(normalizedRoot),
                method,
                startedUtc,
                stopwatch.Elapsed.TotalMilliseconds,
                "Abgebrochen",
                false,
                false,
                true,
                null,
                null,
                firstTouch);
            _measurementStore.Record(measurement);
            RecordMeasurement(measurement);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var measurement = new DiscDriveMeasurement(
                normalizedRoot,
                CreateDriveReference(normalizedRoot),
                method,
                startedUtc,
                stopwatch.Elapsed.TotalMilliseconds,
                ex.GetType().Name,
                false,
                false,
                false,
                null,
                null,
                firstTouch);
            _measurementStore.Record(measurement);
            RecordMeasurement(measurement);
            throw;
        }
    }

    private static string CreateDriveReference(string? driveRoot)
    {
        var root = DiscDriveConfigurationService.NormalizeRootPath(driveRoot);
        return string.IsNullOrWhiteSpace(root) ? "optical" : root.TrimEnd('\\');
    }

    public IReadOnlyList<DiscDriveInfo> GetCdDriveShells()
    {
        var result = new List<DiscDriveInfo>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(item => item.DriveType == DriveType.CDRom))
            {
                var rootPath = drive.RootDirectory.FullName;
                result.Add(new DiscDriveInfo(
                    rootPath,
                    rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    IsReady: false,
                    VolumeLabel: string.Empty,
                    MediaKind: DiscMediaKind.Unknown,
                    IsChecking: true));
            }
        }
        catch
        {
            // Der Dialog darf auch dann sofort erscheinen, wenn Windows die Laufwerksliste kurz nicht liefert.
        }

        return result.OrderBy(item => item.DriveLetter, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<DiscDriveInfo> GetCdDrives()
    {
        var result = new List<DiscDriveInfo>();

        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(item => item.DriveType == DriveType.CDRom))
            {
                var rootPath = drive.RootDirectory.FullName;
                var info = GetDriveInfoForPath(rootPath);
                if (info is not null) result.Add(info);
            }
        }
        catch
        {
            // Eine leere Liste ist ein gueltiges Ergebnis, wenn Windows keine Laufwerksinfos liefert.
        }

        return result.OrderBy(item => item.DriveLetter, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public DiscDriveInfo? GetDriveDiagnosticsForPath(string? path) =>
        DiscDriveOperationService.Execute(path, () => GetDriveDiagnosticsForPathCore(path));

    private DiscDriveInfo? GetDriveDiagnosticsForPathCore(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
            return null;

        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.CDRom)
                return null;

            var isReady = false;
            var volumeLabel = string.Empty;
            try
            {
                isReady = drive.IsReady;
                if (isReady)
                    volumeLabel = drive.VolumeLabel?.Trim() ?? string.Empty;
            }
            catch
            {
                isReady = false;
            }

            var diagnostics = ReadDriveDiagnostics(root);
            return new DiscDriveInfo(
                root,
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                isReady,
                volumeLabel,
                isReady ? DiscMediaKind.Unknown : DiscMediaKind.Empty,
                diagnostics.DriveName,
                diagnostics.DevicePath,
                StableId: diagnostics.StableId,
                IdentityKind: diagnostics.IdentityKind,
                StorageSerialNumber: diagnostics.StorageSerialNumber,
                PnpDeviceInstanceId: diagnostics.PnpDeviceInstanceId,
                Vendor: diagnostics.Vendor,
                Product: diagnostics.Product,
                Revision: diagnostics.Revision,
                HardwareFingerprint: diagnostics.HardwareFingerprint);
        }
        catch
        {
            return null;
        }
    }

    public DiscMediaKind GetMediaKindForPath(string? path) =>
        DiscDriveOperationService.Execute(path, () => GetMediaKindForPathCore(path));

    private DiscMediaKind GetMediaKindForPathCore(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DiscMediaKind.Unknown;

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
            return DiscMediaKind.Unknown;

        try
        {
            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.CDRom)
                return DiscMediaKind.Unknown;

            bool isReady;
            try { isReady = drive.IsReady; }
            catch { isReady = false; }

            return ClassifyMedia(root, isReady);
        }
        catch
        {
            return DiscMediaKind.Unknown;
        }
    }

    public DiscDriveInfo? GetDriveInfoForPath(string? path) =>
        DiscDriveOperationService.Execute(path, () => GetDriveInfoForPathCore(path));

    private DiscDriveInfo? GetDriveInfoForPathCore(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
            return null;

        var drive = GetDriveDiagnosticsForPath(root);
        return drive is null ? null : drive with { MediaKind = ClassifyMedia(root, drive.IsReady) };
    }

    private DiscDriveHardwareIdentity ReadDriveDiagnostics(string rootPath) => _identityService.Probe(rootPath);

    public static DiscDriveInfo? SelectPreferredDrive(
        IReadOnlyList<DiscDriveInfo> drives,
        string? preferredRootPath)
    {
        ArgumentNullException.ThrowIfNull(drives);

        if (!string.IsNullOrWhiteSpace(preferredRootPath))
        {
            var preferredRoot = NormalizeRootPath(preferredRootPath);
            var match = drives.FirstOrDefault(drive =>
                string.Equals(NormalizeRootPath(drive.RootPath), preferredRoot, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return drives.FirstOrDefault(drive => drive.IsReady) ?? drives.FirstOrDefault();
    }

    internal static DiscMediaKind ClassifyMedia(string rootPath, bool isReady)
    {
        if (!isReady) return DiscMediaKind.Empty;
        var token = DiscDriveOperationService.CurrentCancellationToken;
        var incomplete = false;
        void OnError(string path, Exception error) => incomplete = true;
        if (SafeFileSystemEnumeration.FilesInDirectory(rootPath, token, OnError, "*.cda")
            .Any(path => Path.GetExtension(path).Equals(".cda", StringComparison.OrdinalIgnoreCase)))
            return DiscMediaKind.AudioCd;

        const int maximumFilesToInspect = 300;
        const int maximumFoldersToInspect = 300;
        var pending = new Stack<string>();
        pending.Push(rootPath);
        var inspected = 0;
        var folders = 0;
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            if (++folders > maximumFoldersToInspect) return DiscMediaKind.Unknown;
            foreach (var file in SafeFileSystemEnumeration.FilesInDirectory(folder, token, OnError))
            {
                if (IsSupportedAudioExtension(Path.GetExtension(file))) return DiscMediaKind.Mp3Disc;
                if (++inspected >= maximumFilesToInspect) return DiscMediaKind.Unknown;
            }
            var children = SafeFileSystemEnumeration.DirectoriesInDirectory(folder, token, OnError).Take(25).ToList();
            if (children.Count > 24) incomplete = true;
            foreach (var child in children.Take(24)) pending.Push(child);
        }
        // A bounded or unreadable scan cannot establish the absence of supported audio.
        return incomplete ? DiscMediaKind.Unknown : DiscMediaKind.DataDisc;
    }

    private static bool IsSupportedAudioExtension(string extension) =>
        AudioInputFormatCatalog.IsDataDiscImportExtension(extension);

    private static string NormalizeRootPath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path.Trim()) ?? path.Trim();
            return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    public DiscDriveInfo? FindReadyAlternativeDrive(string selectedPath)
    {
        var selectedRoot = Path.GetPathRoot(selectedPath) ?? selectedPath;
        return GetCdDrives().FirstOrDefault(drive =>
            drive.IsReady &&
            !string.Equals(drive.RootPath, selectedRoot, StringComparison.OrdinalIgnoreCase));
    }

    public string GetPreferredDiscInitialDirectory()
    {
        try
        {
            var readyCdDrive = DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.CDRom)
                .FirstOrDefault(drive => drive.IsReady);

            if (readyCdDrive is not null)
                return readyCdDrive.RootDirectory.FullName;

            var cdDrive = DriveInfo.GetDrives()
                .FirstOrDefault(drive => drive.DriveType == DriveType.CDRom);

            if (cdDrive is not null)
                return cdDrive.RootDirectory.FullName;
        }
        catch
        {
            // Falls Windows keine Laufwerksinfos liefert, fällt der Dialog auf seinen Standard zurück.
        }

        return "";
    }

    public Task<string> ResolveResumeDiscSourceAsync(
        string projectFolder,
        CancellationToken cancellationToken = default,
        params string?[] candidates) =>
        Task.Run(() => ResolveResumeDiscSource(projectFolder, candidates), cancellationToken);

    public Task<bool> IsValidResumeDiscSourceAsync(
        string? sourceFolder,
        string projectFolder,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => IsValidResumeDiscSource(sourceFolder, projectFolder), cancellationToken);

    public string ResolveResumeDiscSource(string projectFolder, params string?[] candidates)
    {
        foreach (var candidate in candidates ?? [])
        {
            var candidatePath = candidate?.Trim() ?? string.Empty;
            if (IsValidResumeDiscSource(candidatePath, projectFolder))
                return candidatePath;
        }

        return string.Empty;
    }

    public bool IsValidResumeDiscSource(string? sourceFolder, string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder))
            return false;

        var candidate = sourceFolder.Trim();

        if (!string.IsNullOrWhiteSpace(projectFolder) && IsSameOrChildPath(candidate, projectFolder))
            return false;

        if (Directory.Exists(candidate))
            return true;

        return IsCdDrivePath(candidate);
    }

    public bool IsDiscSourceReady(string? sourceFolder) =>
        DiscDriveOperationService.Execute(sourceFolder, () => IsDiscSourceReadyCore(sourceFolder));

    private bool IsDiscSourceReadyCore(string? sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder))
            return false;

        try
        {
            var root = Path.GetPathRoot(sourceFolder);
            if (string.IsNullOrWhiteSpace(root))
                return Directory.Exists(sourceFolder);

            var drive = new DriveInfo(root);
            if (drive.DriveType == DriveType.CDRom)
                return drive.IsReady && Directory.Exists(sourceFolder);

            return Directory.Exists(sourceFolder);
        }
        catch
        {
            return false;
        }
    }

    public bool IsCdDrivePath(string? sourceFolder) =>
        DiscDriveOperationService.Execute(sourceFolder, () => IsCdDrivePathCore(sourceFolder));

    private bool IsCdDrivePathCore(string? sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder))
            return false;

        try
        {
            var root = Path.GetPathRoot(sourceFolder);
            if (string.IsNullOrWhiteSpace(root))
                return false;

            var drive = new DriveInfo(root);
            return drive.DriveType == DriveType.CDRom;
        }
        catch
        {
            return false;
        }
    }

    public bool TryEjectDisc(string? sourceFolder) =>
        DiscDriveOperationService.Execute(sourceFolder, () => TryEjectDiscCore(sourceFolder));

    private bool TryEjectDiscCore(string? sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder))
            return false;

        DiscDriveOperationService.CurrentCancellationToken.ThrowIfCancellationRequested();
        if (TryEjectDiscWithDeviceIoControl(sourceFolder)) return true;
        DiscDriveOperationService.CurrentCancellationToken.ThrowIfCancellationRequested();
        return TryEjectDiscWithMci(sourceFolder);
    }

    internal static bool IsSameOrChildPath(string path, string possibleParent)
    {
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullParent = Path.GetFullPath(possibleParent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return fullPath.Equals(fullParent, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(fullParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryEjectDiscWithMci(string sourceFolder)
    {
        try
        {
            var root = Path.GetPathRoot(sourceFolder);
            if (string.IsNullOrWhiteSpace(root))
                return false;

            var driveName = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(driveName))
                return false;

            var alias = $"BookStitchCdDrive_{Guid.NewGuid():N}";
            NativeMciSendString($"open {driveName} type CDAudio alias {alias}", null, 0, IntPtr.Zero);
            try
            {
                DiscDriveOperationService.CurrentCancellationToken.ThrowIfCancellationRequested();
                return NativeMciSendString($"set {alias} door open", null, 0, IntPtr.Zero) == 0;
            }
            finally { NativeMciSendString($"close {alias}", null, 0, IntPtr.Zero); }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }

    private static bool TryEjectDiscWithDeviceIoControl(string sourceFolder)
    {
        try
        {
            var root = Path.GetPathRoot(sourceFolder);
            if (string.IsNullOrWhiteSpace(root))
                return false;

            var driveLetter = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (driveLetter.Length < 2)
                return false;

            using var handle = CreateFile(
                $@"\\.\{driveLetter}",
                GenericRead,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
                return false;

            DiscDriveOperationService.CurrentCancellationToken.ThrowIfCancellationRequested();
            return DeviceIoControl(
                handle,
                IoctlStorageEjectMedia,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int NativeMciSendString(
        string command,
        StringBuilder? returnValue,
        int returnLength,
        IntPtr windowHandle);

    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlStorageEjectMedia = 0x002D4808;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle deviceHandle,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
