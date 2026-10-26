using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace BookStitch.Services;

public sealed record DiscDriveHardwareIdentity(
    string StableId,
    DiscDriveIdentityKind IdentityKind,
    string DevicePath,
    string DriveName,
    string StorageSerialNumber,
    string PnpDeviceInstanceId,
    string Vendor,
    string Product,
    string Revision,
    string HardwareFingerprint);

/// <summary>Windows storage/PnP probe. It never enumerates logical drives; it enriches a drive found by DiscDriveService.</summary>
public sealed class DiscDriveIdentityService
{
    public DiscDriveHardwareIdentity Probe(string rootPath)
    {
        if (!OperatingSystem.IsWindows())
            return Empty();

        var driveLetter = rootPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (driveLetter.Length < 2)
            return Empty();

        var devicePath = QueryDevicePath(driveLetter);
        var storage = QueryStorageDescriptor(driveLetter);
        var pnpInstanceId = QueryPnpDeviceInstanceId(driveLetter);
        var serial = NormalizeIdentityValue(storage.SerialNumber);
        var pnp = NormalizeIdentityValue(pnpInstanceId);
        var fingerprint = BuildHardwareFingerprint(storage.Vendor, storage.Product, storage.Revision);

        var (stableId, kind) = IsUsableSerialNumber(serial)
            ? ($"serial:{serial}", DiscDriveIdentityKind.StorageSerialNumber)
            : !string.IsNullOrWhiteSpace(pnp)
                ? ($"pnp:{pnp}", DiscDriveIdentityKind.PnpDeviceInstance)
                : !string.IsNullOrWhiteSpace(fingerprint)
                    ? ($"fingerprint:{fingerprint}", DiscDriveIdentityKind.HardwareFingerprint)
                    : (string.Empty, DiscDriveIdentityKind.Unavailable);
        var driveName = string.Join(" ", new[] { storage.Vendor, storage.Product, storage.Revision }
            .Where(value => !string.IsNullOrWhiteSpace(value))).Trim();

        return new DiscDriveHardwareIdentity(
            stableId, kind, devicePath, driveName, serial, pnp,
            storage.Vendor, storage.Product, storage.Revision, fingerprint);
    }

    internal static bool IsUsableSerialNumber(string? value)
    {
        var serial = NormalizeIdentityValue(value);
        if (serial.Length < 4 || serial.All(character => character == serial[0]))
            return false;

        return serial is not "UNKNOWN" and not "NONE" and not "N/A" and not "123456789" &&
               !serial.Contains("TO BE FILLED", StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildHardwareFingerprint(string? vendor, string? product, string? revision)
    {
        var source = string.Join("|", new[] { vendor, product, revision }.Select(NormalizeIdentityValue));
        if (source.All(character => character == '|'))
            return string.Empty;

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..20];
    }

    private static string NormalizeIdentityValue(string? value) =>
        string.Join(" ", (value ?? string.Empty).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private static DiscDriveHardwareIdentity Empty() => new(
        string.Empty, DiscDriveIdentityKind.Unavailable, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    private static string QueryDevicePath(string driveLetter)
    {
        try
        {
            var buffer = new StringBuilder(1024);
            return QueryDosDevice(driveLetter, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
        }
        catch { return string.Empty; }
    }

    private static StorageDescriptor QueryStorageDescriptor(string driveLetter)
    {
        try
        {
            using var handle = OpenDevice($@"\\.\{driveLetter}");
            if (handle.IsInvalid)
                return default;

            var querySize = Marshal.SizeOf<StoragePropertyQuery>();
            var queryBuffer = Marshal.AllocHGlobal(querySize);
            var outputBuffer = Marshal.AllocHGlobal((int)StorageDescriptorBufferSize);
            try
            {
                Marshal.StructureToPtr(new StoragePropertyQuery(), queryBuffer, false);
                if (!DeviceIoControl(handle, IoctlStorageQueryProperty, queryBuffer, (uint)querySize,
                        outputBuffer, StorageDescriptorBufferSize, out var bytesReturned, IntPtr.Zero))
                {
                    return default;
                }

                var descriptor = Marshal.PtrToStructure<StorageDeviceDescriptorHeader>(outputBuffer);
                return new StorageDescriptor(
                    ReadAnsiString(outputBuffer, descriptor.VendorIdOffset, bytesReturned),
                    ReadAnsiString(outputBuffer, descriptor.ProductIdOffset, bytesReturned),
                    ReadAnsiString(outputBuffer, descriptor.ProductRevisionOffset, bytesReturned),
                    ReadAnsiString(outputBuffer, descriptor.SerialNumberOffset, bytesReturned));
            }
            finally
            {
                Marshal.FreeHGlobal(outputBuffer);
                Marshal.FreeHGlobal(queryBuffer);
            }
        }
        catch { return default; }
    }

    private static string QueryPnpDeviceInstanceId(string driveLetter)
    {
        var driveNumber = GetStorageDeviceNumber($@"\\.\{driveLetter}");
        if (driveNumber is null)
            return string.Empty;

        var classGuid = GuidDevInterfaceCdRom;
        var deviceInfoSet = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == InvalidHandleValue)
            return string.Empty;

        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData { Size = (uint)Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref classGuid, index, ref interfaceData))
                    break;

                SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                if (requiredSize == 0)
                    continue;

                var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    var deviceInfoData = new SpDevInfoData { Size = (uint)Marshal.SizeOf<SpDevInfoData>() };
                    if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, requiredSize,
                            out _, ref deviceInfoData))
                    {
                        continue;
                    }

                    var pathOffset = IntPtr.Size == 8 ? 8 : 4;
                    var interfacePath = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, pathOffset));
                    if (string.IsNullOrWhiteSpace(interfacePath) || GetStorageDeviceNumber(interfacePath) is not { } candidate ||
                        candidate.DeviceType != driveNumber.Value.DeviceType || candidate.DeviceNumber != driveNumber.Value.DeviceNumber)
                    {
                        continue;
                    }

                    var instanceId = new StringBuilder(1024);
                    return SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfoData, instanceId, instanceId.Capacity, out _)
                        ? instanceId.ToString()
                        : string.Empty;
                }
                finally { Marshal.FreeHGlobal(detailBuffer); }
            }
        }
        catch { }
        finally { SetupDiDestroyDeviceInfoList(deviceInfoSet); }
        return string.Empty;
    }

    private static StorageDeviceNumber? GetStorageDeviceNumber(string path)
    {
        try
        {
            using var handle = OpenDevice(path);
            if (handle.IsInvalid)
                return null;
            var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<StorageDeviceNumber>());
            try
            {
                return DeviceIoControl(handle, IoctlStorageGetDeviceNumber, IntPtr.Zero, 0, buffer,
                        (uint)Marshal.SizeOf<StorageDeviceNumber>(), out _, IntPtr.Zero)
                    ? Marshal.PtrToStructure<StorageDeviceNumber>(buffer)
                    : null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        catch { return null; }
    }

    private static SafeFileHandle OpenDevice(string path) => CreateFile(
        path, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    private static string ReadAnsiString(IntPtr buffer, uint offset, uint bufferLength)
    {
        if (offset == 0 || offset >= bufferLength)
            return string.Empty;

        var byteCount = checked((int)(bufferLength - offset));
        var bytes = new byte[byteCount];
        Marshal.Copy(IntPtr.Add(buffer, checked((int)offset)), bytes, 0, byteCount);
        var terminator = Array.IndexOf(bytes, (byte)0);
        return Encoding.ASCII.GetString(bytes, 0, terminator < 0 ? byteCount : terminator).Trim();
    }

    private readonly record struct StorageDescriptor(string Vendor, string Product, string Revision, string SerialNumber);

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery { public int PropertyId; public int QueryType; public byte AdditionalParameters; }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageDeviceDescriptorHeader
    {
        public uint Version; public uint Size; public byte DeviceType; public byte DeviceTypeModifier;
        [MarshalAs(UnmanagedType.U1)] public bool RemovableMedia;
        [MarshalAs(UnmanagedType.U1)] public bool CommandQueueing;
        public uint VendorIdOffset; public uint ProductIdOffset; public uint ProductRevisionOffset;
        public uint SerialNumberOffset; public int BusType; public uint RawPropertiesLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StorageDeviceNumber { public uint DeviceType; public uint DeviceNumber; public uint PartitionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData { public uint Size; public Guid InterfaceClassGuid; public uint Flags; public UIntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData { public uint Size; public Guid ClassGuid; public uint DevInst; public UIntPtr Reserved; }

    private static readonly Guid GuidDevInterfaceCdRom = new("53F56308-B6BF-11D0-94F2-00A0C91EFB8B");
    private static readonly IntPtr InvalidHandleValue = new(-1);
    private const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;
    private const uint IoctlStorageQueryProperty = 0x002D1400, IoctlStorageGetDeviceNumber = 0x002D1080;
    private const uint StorageDescriptorBufferSize = 4096, DigcfPresent = 2, DigcfDeviceInterface = 16;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int maximumLength);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle deviceHandle, uint ioControlCode, IntPtr inBuffer, uint inBufferSize, IntPtr outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData interfaceData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData, IntPtr detailData, uint detailDataSize, out uint requiredSize, IntPtr deviceInfoData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData, IntPtr detailData, uint detailDataSize, out uint requiredSize, ref SpDevInfoData deviceInfoData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
