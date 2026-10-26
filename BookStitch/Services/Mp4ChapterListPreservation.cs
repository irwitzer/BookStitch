using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace BookStitch.Services;

public sealed record NeroChapterInfo(string Title, long StartTicks);

// One finalization, one current file. No chapter bytes are retained between exports.
public sealed class Mp4ChapterListPreservation
{
    private const int MaximumMoovSize = 256 * 1024 * 1024;
    private readonly string _path;
    private readonly byte[]? _chapterList;

    private Mp4ChapterListPreservation(string path, byte[]? chapterList)
    {
        _path = path;
        _chapterList = chapterList;
    }

    public static Mp4ChapterListPreservation Capture(string path)
    {
        using var file = File.OpenRead(path);
        var moov = ReadMoov(file);
        return new Mp4ChapterListPreservation(path, ReadChapterList(moov.Bytes));
    }

    internal static IReadOnlyList<NeroChapterInfo>? ReadChapters(string path)
    {
        using var file = File.OpenRead(path);
        var bytes = ReadChapterList(ReadMoov(file).Bytes);
        if (bytes is null) return null;
        var box = Children(bytes, 0, bytes.Length).Single();
        var position = box.HeaderSize;
        if (bytes.Length - position < 5) throw Invalid("Truncated Nero chapter header.");
        var version = bytes[position];
        if (version > 1) throw Invalid("Unsupported Nero chapter version.");
        position += version == 0 ? 4 : 8;
        if (position >= bytes.Length) throw Invalid("Truncated Nero chapter header.");
        var count = bytes[position++];
        var chapters = new List<NeroChapterInfo>(count);
        for (var index = 0; index < count; index++)
        {
            if (bytes.Length - position < 9) throw Invalid("Truncated Nero chapter entry.");
            var start = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(position, 8));
            position += 8;
            var length = bytes[position++];
            if (start > long.MaxValue || bytes.Length - position < length)
                throw Invalid("Invalid Nero chapter entry bounds.");
            chapters.Add(new NeroChapterInfo(Encoding.UTF8.GetString(bytes, position, length), (long)start));
            position += length;
        }
        if (position != bytes.Length) throw Invalid("Unexpected Nero chapter trailing data.");
        return chapters;
    }

    public void RestoreAndVerify()
    {
        using var file = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var moov = ReadMoov(file);
        var current = ReadChapterList(moov.Bytes);
        if (_chapterList is null)
        {
            if (current is not null)
                throw Invalid("Tagging introduced an unexpected Nero chapter list.");
            return;
        }
        if (current is null || !current.AsSpan().SequenceEqual(_chapterList))
        {
            var restored = RestoreMoov(moov.Bytes, _chapterList);
            var delta = (long)restored.Length - moov.Bytes.Length;
            if (delta != 0)
            {
                if (moov.HasOffsetSensitiveBoxes)
                    throw Invalid("Cannot resize metadata in a fragmented/indexed MP4.");
                AdjustChunkOffsets(restored, 0, restored.Length, moov.Offset, moov.Offset + moov.Bytes.Length, delta);
                MoveTail(file, moov.Offset + moov.Bytes.Length, delta);
            }
            file.Position = moov.Offset;
            file.Write(restored);
            file.Flush(flushToDisk: true);
        }
        var verified = ReadChapterList(ReadMoov(file).Bytes);
        if (verified is null || !verified.AsSpan().SequenceEqual(_chapterList))
            throw Invalid("Nero chapter list verification failed after tagging.");
    }

    private static InvalidDataException Invalid(string detail) =>
        new(LocalizationText.Get("FinalTags.ChapterPreservationFailed"), new InvalidDataException(detail));

    private static (long Offset, byte[] Bytes, bool HasOffsetSensitiveBoxes) ReadMoov(Stream file)
    {
        file.Position = 0;
        (long Offset, byte[] Bytes)? result = null;
        var sensitive = false;
        Span<byte> header = stackalloc byte[16];
        while (file.Position < file.Length)
        {
            var offset = file.Position;
            file.ReadExactly(header[..8]);
            var size32 = BinaryPrimitives.ReadUInt32BigEndian(header);
            var headerSize = size32 == 1 ? 16 : 8;
            if (headerSize == 16)
                file.ReadExactly(header[8..]);
            var size = size32 switch
            {
                0 => file.Length - offset,
                1 => checked((long)BinaryPrimitives.ReadUInt64BigEndian(header[8..])),
                _ => size32
            };
            if (size < headerSize || size > file.Length - offset)
                throw Invalid("Invalid MP4 top-level box bounds.");
            var type = Encoding.ASCII.GetString(header.Slice(4, 4));
            sensitive |= type is "moof" or "sidx" or "mfra";
            if (type == "moov")
            {
                if (result is not null || size > MaximumMoovSize)
                    throw Invalid("Invalid or excessively large MP4 movie metadata.");
                var bytes = new byte[checked((int)size)];
                header[..headerSize].CopyTo(bytes);
                file.ReadExactly(bytes.AsSpan(headerSize));
                result = (offset, bytes);
            }
            file.Position = checked(offset + size);
        }
        if (result is null)
            throw Invalid("MP4 movie metadata is missing.");
        return (result.Value.Offset, result.Value.Bytes, sensitive);
    }

    private readonly record struct Box(int Offset, int Size, int HeaderSize, string Type);

    private static List<Box> Children(byte[] bytes, int start, int end)
    {
        var result = new List<Box>();
        while (start < end)
        {
            if (end - start < 8)
                throw Invalid("Truncated MP4 box header.");
            var size32 = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(start, 4));
            var headerSize = size32 == 1 ? 16 : 8;
            if (end - start < headerSize)
                throw Invalid("Truncated extended MP4 box header.");
            var size = size32 switch
            {
                0 => end - start,
                1 => checked((int)BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(start + 8, 8))),
                _ => checked((int)size32)
            };
            if (size < headerSize || size > end - start)
                throw Invalid("Invalid MP4 child box bounds.");
            result.Add(new Box(start, size, headerSize, Encoding.ASCII.GetString(bytes, start + 4, 4)));
            start += size;
        }
        return result;
    }

    private static byte[]? ReadChapterList(byte[] moov)
    {
        var root = Children(moov, 0, moov.Length).Single();
        byte[]? result = null;
        foreach (var udta in Children(moov, root.HeaderSize, moov.Length).Where(box => box.Type == "udta"))
            foreach (var child in Children(moov, udta.Offset + udta.HeaderSize, udta.Offset + udta.Size)
                .Where(box => box.Type == "chpl"))
            {
                if (result is not null || child.Size > 1024 * 1024)
                    throw Invalid("Ambiguous or excessively large Nero chapter list.");
                result = moov.AsSpan(child.Offset, child.Size).ToArray();
            }
        return result;
    }

    private static byte[] RestoreMoov(byte[] moov, byte[] original)
    {
        var root = Children(moov, 0, moov.Length).Single();
        var children = Children(moov, root.HeaderSize, moov.Length);
        var userData = children.Where(box => box.Type == "udta").ToArray();
        var target = userData.FirstOrDefault(box => Children(moov, box.Offset + box.HeaderSize, box.Offset + box.Size)
            .Any(child => child.Type == "chpl"));
        if (target.Size == 0)
            target = userData.FirstOrDefault();
        var payload = new List<byte[]>();
        foreach (var child in children)
        {
            if (target.Size > 0 && child.Offset == target.Offset)
            {
                var items = new List<byte[]>();
                var inserted = false;
                foreach (var item in Children(moov, child.Offset + child.HeaderSize, child.Offset + child.Size))
                {
                    if (item.Type == "chpl")
                    {
                        items.Add(original);
                        inserted = true;
                    }
                    else
                        items.Add(moov.AsSpan(item.Offset, item.Size).ToArray());
                }
                if (!inserted)
                    items.Add(original);
                payload.Add(RebuildBox(moov.AsSpan(child.Offset, child.HeaderSize), items));
            }
            else
                payload.Add(moov.AsSpan(child.Offset, child.Size).ToArray());
        }
        if (target.Size == 0)
            payload.Add(RebuildBox(new byte[] { 0, 0, 0, 8, (byte)'u', (byte)'d', (byte)'t', (byte)'a' }, [original]));
        return RebuildBox(moov.AsSpan(0, root.HeaderSize), payload);
    }

    private static byte[] RebuildBox(ReadOnlySpan<byte> header, IReadOnlyList<byte[]> children)
    {
        var length = checked(header.Length + children.Sum(child => child.Length));
        var result = new byte[length];
        header.CopyTo(result);
        if (header.Length == 16)
            BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(8, 8), (ulong)length);
        else
            BinaryPrimitives.WriteUInt32BigEndian(result, (uint)length);
        var offset = header.Length;
        foreach (var child in children)
        {
            child.CopyTo(result, offset);
            offset += child.Length;
        }
        return result;
    }

    private static void AdjustChunkOffsets(byte[] bytes, int start, int end, long moovStart, long moovEnd, long delta)
    {
        foreach (var box in Children(bytes, start, end))
        {
            if (box.Type is "moov" or "trak" or "mdia" or "minf" or "stbl")
                AdjustChunkOffsets(bytes, box.Offset + box.HeaderSize, box.Offset + box.Size, moovStart, moovEnd, delta);
            else if (box.Type is "stco" or "co64")
            {
                var payload = box.Offset + box.HeaderSize;
                var width = box.Type == "stco" ? 4 : 8;
                if (box.Size - box.HeaderSize < 8)
                    throw Invalid("Truncated MP4 chunk offset table.");
                var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(payload + 4, 4));
                if (8L + count * (long)width != box.Size - box.HeaderSize)
                    throw Invalid("Invalid MP4 chunk offset table size.");
                for (var index = 0U; index < count; index++)
                {
                    var field = bytes.AsSpan(checked(payload + 8 + (int)index * width), width);
                    var value = width == 4 ? BinaryPrimitives.ReadUInt32BigEndian(field) : BinaryPrimitives.ReadUInt64BigEndian(field);
                    if (value >= (ulong)moovStart && value < (ulong)moovEnd)
                        throw Invalid("MP4 sample points inside movie metadata.");
                    if (value >= (ulong)moovEnd)
                    {
                        var adjusted = checked((ulong)(checked((long)value + delta)));
                        if (width == 4)
                            BinaryPrimitives.WriteUInt32BigEndian(field, checked((uint)adjusted));
                        else
                            BinaryPrimitives.WriteUInt64BigEndian(field, adjusted);
                    }
                }
            }
        }
    }

    private static void MoveTail(FileStream file, long start, long delta)
    {
        var oldLength = file.Length;
        var buffer = new byte[1024 * 1024];
        if (delta > 0)
        {
            file.SetLength(checked(oldLength + delta));
            var end = oldLength;
            while (end > start)
            {
                var count = (int)Math.Min(buffer.Length, end - start);
                file.Position = end - count;
                file.ReadExactly(buffer.AsSpan(0, count));
                file.Position = end - count + delta;
                file.Write(buffer, 0, count);
                end -= count;
            }
        }
        else
        {
            var position = start;
            while (position < oldLength)
            {
                var count = (int)Math.Min(buffer.Length, oldLength - position);
                file.Position = position;
                file.ReadExactly(buffer.AsSpan(0, count));
                file.Position = position + delta;
                file.Write(buffer, 0, count);
                position += count;
            }
            file.SetLength(checked(oldLength + delta));
        }
    }
}
