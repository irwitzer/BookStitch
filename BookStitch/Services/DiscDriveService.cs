using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BookStitch.Services;

public enum DiscMediaKind
{
    Empty,
    AudioCd,
    Mp3Disc,
    DataDisc,
    Unknown
}

public sealed record DiscDriveInfo(
    string RootPath,
    string DriveLetter,
    bool IsReady,
    string VolumeLabel,
    DiscMediaKind MediaKind = DiscMediaKind.Unknown,
    string DriveName = "",
    string DevicePath = "",
    bool IsChecking = false)
{
    public string DisplayName => IsChecking
        ? "Wird geprüft …"
        : IsReady
            ? string.IsNullOrWhiteSpace(VolumeLabel) ? "Datenträger eingelegt" : VolumeLabel
            : "Kein Datenträger eingelegt";

    public string StatusText => IsChecking
        ? "Datenträger wird gelesen …"
        : MediaKind switch
        {
            DiscMediaKind.Empty => "Laufwerk ist leer",
            DiscMediaKind.AudioCd => "Audio-CD",
            DiscMediaKind.Mp3Disc => "MP3-CD",
            DiscMediaKind.DataDisc => "Daten-CD",
            _ => IsReady ? "Bereit" : "Laufwerk ist leer"
        };

    public string DiagnosticDriveName => string.IsNullOrWhiteSpace(DriveName)
        ? "Optisches Laufwerk"
        : DriveName;
}


public sealed class DiscDriveService
{
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

                var diagnostics = ReadDriveDiagnostics(rootPath);
                result.Add(new DiscDriveInfo(
                    rootPath,
                    rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    isReady,
                    volumeLabel,
                    ClassifyMedia(rootPath, isReady),
                    diagnostics.DriveName,
                    diagnostics.DevicePath));
            }
        }
        catch
        {
            // Eine leere Liste ist ein gueltiges Ergebnis, wenn Windows keine Laufwerksinfos liefert.
        }

        return result.OrderBy(item => item.DriveLetter, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public DiscDriveInfo? GetDriveDiagnosticsForPath(string? path)
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
                diagnostics.DevicePath);
        }
        catch
        {
            return null;
        }
    }

    public DiscMediaKind GetMediaKindForPath(string? path)
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

    public DiscDriveInfo? GetDriveInfoForPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root))
            return null;

        return GetCdDrives().FirstOrDefault(drive =>
            string.Equals(NormalizeRootPath(drive.RootPath), NormalizeRootPath(root), StringComparison.OrdinalIgnoreCase));
    }

    private static (string DriveName, string DevicePath) ReadDriveDiagnostics(string rootPath)
    {
        var driveLetter = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (driveLetter.Length < 2)
            return (string.Empty, string.Empty);

        var devicePath = QueryDevicePath(driveLetter);
        var driveName = QueryStorageDeviceName(driveLetter);
        return (driveName, devicePath);
    }

    private static string QueryDevicePath(string driveLetter)
    {
        try
        {
            var buffer = new StringBuilder(1024);
            return QueryDosDevice(driveLetter, buffer, buffer.Capacity) > 0
                ? buffer.ToString()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string QueryStorageDeviceName(string driveLetter)
    {
        try
        {
            using var handle = CreateFile(
                $@"\\.\{driveLetter}",
                0,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
                return string.Empty;

            var querySize = Marshal.SizeOf<StoragePropertyQuery>();
            var queryBuffer = Marshal.AllocHGlobal(querySize);
            var outputBuffer = Marshal.AllocHGlobal(checked((int)StorageDescriptorBufferSize));
            try
            {
                Marshal.StructureToPtr(new StoragePropertyQuery(), queryBuffer, false);
                if (!DeviceIoControl(
                        handle,
                        IoctlStorageQueryProperty,
                        queryBuffer,
                        (uint)querySize,
                        outputBuffer,
                        StorageDescriptorBufferSize,
                        out var bytesReturned,
                        IntPtr.Zero))
                {
                    return string.Empty;
                }

                var descriptor = Marshal.PtrToStructure<StorageDeviceDescriptorHeader>(outputBuffer);
                var vendor = ReadAnsiString(outputBuffer, descriptor.VendorIdOffset, bytesReturned);
                var product = ReadAnsiString(outputBuffer, descriptor.ProductIdOffset, bytesReturned);
                var revision = ReadAnsiString(outputBuffer, descriptor.ProductRevisionOffset, bytesReturned);

                return string.Join(" ", new[] { vendor, product, revision }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                    .Trim();
            }
            finally
            {
                Marshal.FreeHGlobal(outputBuffer);
                Marshal.FreeHGlobal(queryBuffer);
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadAnsiString(IntPtr buffer, uint offset, uint bufferLength)
    {
        if (offset == 0 || offset >= bufferLength)
            return string.Empty;

        return Marshal.PtrToStringAnsi(IntPtr.Add(buffer, checked((int)offset)))?.Trim() ?? string.Empty;
    }

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

    private static DiscMediaKind ClassifyMedia(string rootPath, bool isReady)
    {
        if (!isReady)
            return DiscMediaKind.Empty;

        try
        {
            if (Directory.EnumerateFiles(rootPath, "*.cda", SearchOption.TopDirectoryOnly).Any())
                return DiscMediaKind.AudioCd;

            const int maximumFilesToInspect = 300;
            var pending = new Stack<string>();
            pending.Push(rootPath);
            var inspected = 0;

            while (pending.Count > 0 && inspected < maximumFilesToInspect)
            {
                var folder = pending.Pop();

                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(folder); }
                catch { continue; }

                foreach (var file in files)
                {
                    inspected++;
                    if (IsSupportedAudioExtension(Path.GetExtension(file)))
                        return DiscMediaKind.Mp3Disc;
                    if (inspected >= maximumFilesToInspect)
                        break;
                }

                if (inspected >= maximumFilesToInspect)
                    break;

                try
                {
                    foreach (var child in Directory.EnumerateDirectories(folder).Take(24))
                        pending.Push(child);
                }
                catch { }
            }

            return DiscMediaKind.DataDisc;
        }
        catch
        {
            return DiscMediaKind.Unknown;
        }
    }

    private static bool IsSupportedAudioExtension(string extension) =>
        extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".m4b", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".aac", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".wma", StringComparison.OrdinalIgnoreCase);

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

    public bool IsDiscSourceReady(string? sourceFolder)
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

    public bool IsCdDrivePath(string? sourceFolder)
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

    public bool TryEjectDisc(string? sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder))
            return false;

        return TryEjectDiscWithDeviceIoControl(sourceFolder) || TryEjectDiscWithMci(sourceFolder);
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

            var alias = "BookStitchCdDrive";
            NativeMciSendString($"open {driveName} type CDAudio alias {alias}", null, 0, IntPtr.Zero);
            var result = NativeMciSendString($"set {alias} door open", null, 0, IntPtr.Zero);
            NativeMciSendString($"close {alias}", null, 0, IntPtr.Zero);
            return result == 0;
        }
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
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint StorageDescriptorBufferSize = 4096;

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageDeviceDescriptorHeader
    {
        public uint Version;
        public uint Size;
        public byte DeviceType;
        public byte DeviceTypeModifier;
        [MarshalAs(UnmanagedType.U1)]
        public bool RemovableMedia;
        [MarshalAs(UnmanagedType.U1)]
        public bool CommandQueueing;
        public uint VendorIdOffset;
        public uint ProductIdOffset;
        public uint ProductRevisionOffset;
        public uint SerialNumberOffset;
        public int BusType;
        public uint RawPropertiesLength;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int maximumLength);

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
