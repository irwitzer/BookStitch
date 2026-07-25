using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>
/// Reads the frame-accurate table of contents directly from a Windows CD-ROM device.
/// The returned sector offsets are suitable for MusicBrainz disc-ID calculation and
/// remain independent from the coarser MCI timing values used for the UI.
/// </summary>
public sealed class AudioDiscTocReaderService
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlCdromReadToc = 0x00024000;
    private const int TocBufferSize = 804;
    private const int TrackDescriptorSize = 8;
    private const byte LeadOutTrackNumber = 0xAA;
    private const int CdFramesPerSecond = 75;

    public AudioDiscToc? TryReadToc(string driveRoot)
    {

        var devicePath = CreateDevicePath(driveRoot);
        if (string.IsNullOrWhiteSpace(devicePath))
            return null;

        try
        {
            using var device = NativeCreateFile(
                devicePath,
                GenericRead,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (device.IsInvalid)
                return null;

            var buffer = new byte[TocBufferSize];
            if (!NativeDeviceIoControl(
                    device,
                    IoctlCdromReadToc,
                    IntPtr.Zero,
                    0,
                    buffer,
                    buffer.Length,
                    out var bytesReturned,
                    IntPtr.Zero))
            {
                return null;
            }

            return ParseTocBuffer(buffer.AsSpan(0, Math.Min(bytesReturned, buffer.Length)));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch
        {
            // Exact online metadata is optional. A TOC failure must never prevent
            // normal Audio-CD detection or the later ripping workflow.
            return null;
        }
    }

    public static AudioDiscToc? ParseTocBuffer(ReadOnlySpan<byte> buffer, int? expectedTrackCount = null)
    {
        if (buffer.Length < 4 + (2 * TrackDescriptorSize))
            return null;

        var firstTrackNumber = buffer[2];
        var lastTrackNumber = buffer[3];
        if (firstTrackNumber < 1 || lastTrackNumber < firstTrackNumber || lastTrackNumber > 99)
            return null;

        var declaredTrackCount = lastTrackNumber - firstTrackNumber + 1;
        if (expectedTrackCount is > 0 && declaredTrackCount != expectedTrackCount.Value)
            return null;

        var requiredDescriptorCount = declaredTrackCount + 1; // plus lead-out descriptor
        var requiredLength = 4 + (requiredDescriptorCount * TrackDescriptorSize);
        if (buffer.Length < requiredLength)
            return null;

        var offsets = new List<int>(declaredTrackCount);
        int? leadOutOffset = null;

        for (var index = 0; index < requiredDescriptorCount; index++)
        {
            var descriptorOffset = 4 + (index * TrackDescriptorSize);
            var trackNumber = buffer[descriptorOffset + 2];
            var sectorOffset = ConvertMsfToSectorOffset(
                buffer[descriptorOffset + 5],
                buffer[descriptorOffset + 6],
                buffer[descriptorOffset + 7]);

            if (trackNumber == LeadOutTrackNumber)
            {
                leadOutOffset = sectorOffset;
                continue;
            }

            var expectedTrackNumber = firstTrackNumber + offsets.Count;
            if (trackNumber != expectedTrackNumber)
                return null;

            offsets.Add(sectorOffset);
        }

        if (offsets.Count != declaredTrackCount || leadOutOffset is null)
            return null;

        if (offsets[0] < 150 || offsets.Zip(offsets.Skip(1), (left, right) => right > left).Any(increasing => !increasing))
            return null;

        if (leadOutOffset.Value <= offsets[^1])
            return null;

        var discId = AudioDiscReaderService.CreateMusicBrainzDiscId(
            firstTrackNumber,
            lastTrackNumber,
            leadOutOffset.Value,
            offsets);

        return new AudioDiscToc(
            firstTrackNumber,
            lastTrackNumber,
            leadOutOffset.Value,
            offsets,
            discId);
    }

    private static int ConvertMsfToSectorOffset(byte minute, byte second, byte frame)
    {
        if (second >= 60 || frame >= CdFramesPerSecond)
            throw new InvalidDataException("The CD-ROM TOC contains an invalid MSF address.");

        return ((minute * 60) + second) * CdFramesPerSecond + frame;
    }

    private static string CreateDevicePath(string driveRoot)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
            return string.Empty;

        try
        {
            var root = Path.GetPathRoot(driveRoot.Trim());
            if (string.IsNullOrWhiteSpace(root) || root.Length < 2 || root[1] != ':')
                return string.Empty;

            return $@"\\.\{char.ToUpperInvariant(root[0])}:";
        }
        catch
        {
            return string.Empty;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle NativeCreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeDeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        int inputBufferSize,
        [Out] byte[] outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
