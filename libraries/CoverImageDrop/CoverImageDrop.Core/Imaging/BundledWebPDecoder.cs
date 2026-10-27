using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Core.Imaging;

// Native decoding is deliberately isolated from the WPF host. Only this bounded,
// versioned protocol crosses the process boundary; no installed WIC codec is used.
internal static class BundledWebPDecoder
{
    private const int MaxBytes = 32 * 1024 * 1024;
    private const int MaxMetadata = 1024 * 1024;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal sealed record Result(int Width, int Height, int Frames, byte[] Icc, byte[] Exif, byte[] Pixels)
    {
        public ushort? Orientation => ReadOrientation(Exif);
    }

    internal static Result Read(byte[] bytes, bool decode) => ReadAsync(bytes, decode).GetAwaiter().GetResult();

    private static async Task<Result> ReadAsync(byte[] bytes, bool decode)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new InvalidDataException("WebP input exceeds the resource limit.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var worker = Path.Combine(AppContext.BaseDirectory, "CoverCodecs", "cover-webp-worker.exe");
            using var process = new Process { StartInfo = new ProcessStartInfo(worker)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true
            }};
            process.StartInfo.ArgumentList.Add(decode ? "decode" : "inspect");
            Task<Result>? outputTask = null;
            try
            {
                if (!process.Start()) throw new IOException("WebP worker could not be started.");
                outputTask = ReadOutputAsync(process.StandardOutput.BaseStream, decode, timeout.Token);
                await process.StandardInput.BaseStream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
                var result = await outputTask.ConfigureAwait(false);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                if (process.ExitCode != 0) throw new InvalidDataException("WebP decoder rejected the image.");
                return result;
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                // Process resources must be reaped even when decoding/pipe IO fails.
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                catch (Exception ex) when (ex is InvalidOperationException or TimeoutException) { }
                if (outputTask is not null)
                    try { await outputTask.ConfigureAwait(false); } catch (Exception) { /* observe failed pipe task */ }
                throw;
            }
        }
        finally { Gate.Release(); }
    }

    private static async Task<Result> ReadOutputAsync(Stream stream, bool decode, CancellationToken token)
    {
        var header = new byte[24];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x31444943) throw new InvalidDataException("Invalid WebP worker protocol.");
        int Get(int offset) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset, 4)));
        var width = Get(4); var height = Get(8); var frames = Get(12); var iccLength = Get(16); var exifLength = Get(20);
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 24_000_000 ||
            frames <= 0 || iccLength > MaxMetadata || exifLength > MaxMetadata)
            throw new InvalidDataException("WebP output exceeds the resource limit.");
        var icc = new byte[iccLength]; var exif = new byte[exifLength];
        var pixels = decode ? new byte[checked(width * height * 4)] : [];
        await stream.ReadExactlyAsync(icc, token).ConfigureAwait(false);
        await stream.ReadExactlyAsync(exif, token).ConfigureAwait(false);
        await stream.ReadExactlyAsync(pixels, token).ConfigureAwait(false);
        var tail = new byte[1];
        if (await stream.ReadAsync(tail, token).ConfigureAwait(false) != 0) throw new InvalidDataException("Unexpected WebP worker output.");
        return new Result(width, height, frames, icc, exif, pixels);
    }

    internal static BitmapFrame CreateFrame(Result image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32,
            null, image.Pixels, checked(image.Width * 4));
        bitmap.Freeze();
        var metadata = new BitmapMetadata("jpg");
        if (image.Orientation is { } orientation) metadata.SetQuery("/app1/ifd/{ushort=274}", orientation);
        var frame = BitmapFrame.Create(bitmap, null, metadata, null);
        frame.Freeze();
        return frame;
    }

    internal static ColorContext? CreateColorContext(byte[] icc)
    {
        if (icc.Length == 0) return null;
        var path = Path.Combine(Path.GetTempPath(), $"cover-profile-{Guid.NewGuid():N}.icc");
        try
        {
            File.WriteAllBytes(path, icc);
            return new ColorContext(new Uri(path, UriKind.Absolute));
        }
        catch (Exception ex) when (ex is IOException or FileFormatException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException) { return null; }
        finally { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    internal static ushort? ReadOrientation(ReadOnlySpan<byte> exif)
    {
        if (exif.StartsWith("Exif\0\0"u8)) exif = exif[6..];
        if (exif.Length < 8) return null;
        bool little = exif[..2].SequenceEqual("II"u8);
        if (!little && !exif[..2].SequenceEqual("MM"u8)) return null;
        static ushort U16(ReadOnlySpan<byte> b, bool le) => le ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);
        static uint U32(ReadOnlySpan<byte> b, bool le) => le ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);
        if (U16(exif[2..], little) != 42) return null;
        var offset = U32(exif[4..], little);
        if (offset > exif.Length - 2) return null;
        var table = exif[(int)offset..];
        var count = U16(table, little);
        if (2L + 12L * count > table.Length) return null;
        for (int i = 0; i < count; i++)
        {
            var field = table.Slice(2 + 12 * i, 12);
            if (U16(field, little) == 274 && U16(field[2..], little) == 3 && U32(field[4..], little) == 1)
            {
                var value = U16(field[8..], little);
                return value is >= 1 and <= 8 ? value : null;
            }
        }
        return null;
    }
}
