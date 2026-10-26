using BookStitch.Models;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace BookStitch.Services;

/// <summary>
/// Captures stable machine/runtime context once for an enabled performance session.
/// It performs no background polling and keeps optional Windows probes failure-isolated.
/// </summary>
public sealed class PerformanceEnvironmentContextService
{
    private const string CpuRegistryPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";

    public PerformanceEnvironmentSnapshot Capture()
    {
        var memory = TryGetMemorySnapshot();

        return new PerformanceEnvironmentSnapshot
        {
            OperatingSystem = NormalizeOptional(RuntimeInformation.OSDescription),
            OperatingSystemVersion = NormalizeOptional(Environment.OSVersion.VersionString),
            OperatingSystemArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            DotNetRuntime = NormalizeOptional(RuntimeInformation.FrameworkDescription),
            LogicalProcessorCount = Math.Max(1, Environment.ProcessorCount),
            PhysicalCoreCount = TryGetPhysicalCoreCount(),
            CpuModel = TryGetCpuModel(),
            TotalPhysicalMemoryBytes = memory?.TotalPhysicalBytes,
            AvailablePhysicalMemoryBytes = memory?.AvailablePhysicalBytes,
            ProcessWorkingSetBytes = Math.Max(0L, Environment.WorkingSet)
        };
    }

    private static string? TryGetCpuModel()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CpuRegistryPath, writable: false);
            return NormalizeOptional(key?.GetValue("ProcessorNameString") as string);
        }
        catch
        {
            return null;
        }
    }

    private static MemorySnapshot? TryGetMemorySnapshot()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var status = new MemoryStatusEx
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };

            if (!GlobalMemoryStatusEx(ref status))
                return null;

            return new MemorySnapshot(
                ToInt64(status.TotalPhysical),
                ToInt64(status.AvailablePhysical));
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetPhysicalCoreCount()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        IntPtr buffer = IntPtr.Zero;

        try
        {
            uint byteLength = 0;
            _ = GetLogicalProcessorInformationEx(
                LogicalProcessorRelationship.RelationProcessorCore,
                IntPtr.Zero,
                ref byteLength);

            if (byteLength < 8 || byteLength > int.MaxValue)
                return null;

            buffer = Marshal.AllocHGlobal((int)byteLength);

            if (!GetLogicalProcessorInformationEx(
                    LogicalProcessorRelationship.RelationProcessorCore,
                    buffer,
                    ref byteLength))
            {
                return null;
            }

            var limit = checked((int)byteLength);
            var offset = 0;
            var coreCount = 0;

            while (offset <= limit - 8)
            {
                var relationship = Marshal.ReadInt32(buffer, offset);
                var recordSize = Marshal.ReadInt32(buffer, offset + 4);

                if (recordSize < 8 || recordSize > limit - offset)
                    return null;

                if (relationship == (int)LogicalProcessorRelationship.RelationProcessorCore)
                    coreCount++;

                offset += recordSize;
            }

            return offset == limit && coreCount > 0
                ? coreCount
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    private static long ToInt64(ulong value)
    {
        return value > long.MaxValue
            ? long.MaxValue
            : (long)value;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed record MemorySnapshot(
        long TotalPhysicalBytes,
        long AvailablePhysicalBytes);

    private enum LogicalProcessorRelationship
    {
        RelationProcessorCore = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(
        LogicalProcessorRelationship relationshipType,
        IntPtr buffer,
        ref uint returnedLength);
}