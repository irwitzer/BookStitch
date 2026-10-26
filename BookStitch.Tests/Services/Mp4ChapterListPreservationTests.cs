using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Xunit;

namespace BookStitch.Tests.Services;

[Trait("Category", "NeroChplPreservationGuard")]
public sealed class Mp4ChapterListPreservationTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void ReadChapters_decodes_precise_starts_and_unicode_titles(int version, bool extended)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "chapters.m4b");
        var title = Encoding.UTF8.GetBytes("Chapter 東京");
        var header = version == 0 ? 5 : 9;
        var payload = new byte[header + 9 + title.Length];
        payload[0] = (byte)version; payload[header - 1] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(header), 12345678);
        payload[header + 8] = (byte)title.Length;
        title.CopyTo(payload, header + 9);
        File.WriteAllBytes(path, Box("moov", Box("udta", Box("chpl", payload, extended))));
        var chapter = Assert.Single(Mp4ChapterListPreservation.ReadChapters(path)!);
        Assert.Equal(12345678, chapter.StartTicks); Assert.Equal("Chapter 東京", chapter.Title);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void ReadChapters_rejects_unknown_versions_and_truncated_entries(int version)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "chapters.m4b");
        var payload = new byte[9]; payload[0] = (byte)version; payload[8] = 1;
        File.WriteAllBytes(path, Box("moov", Box("udta", Box("chpl", payload))));
        Assert.Throws<InvalidDataException>(() => Mp4ChapterListPreservation.ReadChapters(path));
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 80)]
    [InlineData(true, true, 80)]
    [InlineData(false, false, 80)]
    [InlineData(false, true, 80)]
    [InlineData(true, false, 24)]
    public void RestoreAndVerify_RestoresMissingResizedOrCorruptedBlockAndKeepsMediaAndChunkOffsets(
        bool fastStart, bool wideOffsets, int replacementLength)
    {
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "final.m4b");
        var chapterList = Box("chpl", Enumerable.Range(0, 24).Select(value => (byte)value).ToArray());
        // More than one copy buffer checks actual file-tail movement in both directions.
        var audio = Enumerable.Range(0, 2 * 1024 * 1024 + 127).Select(index => (byte)(index % 251)).ToArray();
        var original = Container(chapterList, audio, fastStart, wideOffsets);
        File.WriteAllBytes(path, original);
        var preservation = Mp4ChapterListPreservation.Capture(path);
        var replacement = replacementLength == 0 ? null : Box("chpl", new byte[replacementLength]);
        File.WriteAllBytes(path, Container(replacement, audio, fastStart, wideOffsets));
        preservation.RestoreAndVerify();
        Assert.Equal(original, File.ReadAllBytes(path));
        preservation.RestoreAndVerify();
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void RestoreAndVerify_RecreatesMissingUserDataContainer()
    {
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "missing-udta.m4b");
        var chapterList = Box("chpl", new byte[] { 1, 2, 3, 4 });
        File.WriteAllBytes(path, Box("moov", Box("udta", chapterList)));
        var preservation = Mp4ChapterListPreservation.Capture(path);
        File.WriteAllBytes(path, Box("moov", []));
        preservation.RestoreAndVerify();
        Assert.Equal(Box("moov", Box("udta", chapterList)), File.ReadAllBytes(path));
    }

    [Fact]
    public void Capture_NoChapterListNeverInjectsBytesFromEarlierFinalization()
    {
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "final.m4b");
        File.WriteAllBytes(path, Box("moov", Box("udta", Box("chpl", new byte[] { 1 }))));
        Mp4ChapterListPreservation.Capture(path).RestoreAndVerify();
        var current = Box("moov", []);
        File.WriteAllBytes(path, current);
        Mp4ChapterListPreservation.Capture(path).RestoreAndVerify();
        Assert.Equal(current, File.ReadAllBytes(path));
    }

    [Fact]
    public void Capture_OnlyReadsStructuralChapterAtomsAndIgnoresFakeSignatureInMedia()
    {
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "no-chapters.m4b");
        var original = Box("moov", []).Concat(Box("mdat", Box("chpl", new byte[] { 1, 2 }))).ToArray();
        File.WriteAllBytes(path, original);
        Mp4ChapterListPreservation.Capture(path).RestoreAndVerify();
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void Capture_ExtendedSizeMovieAndChapterHeadersArePreserved()
    {
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "extended.m4b");
        var chapters = Box("chpl", new byte[] { 1, 2, 3 }, extended: true);
        var original = Box("moov", Box("udta", chapters, extended: true), extended: true);
        File.WriteAllBytes(path, original);
        var preservation = Mp4ChapterListPreservation.Capture(path);
        File.WriteAllBytes(path, Box("moov", Box("udta", [], extended: true), extended: true));
        preservation.RestoreAndVerify();
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void RestoreAndVerify_UnsafeFragmentedResizeFailsBeforeModifyingFile()
    {
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "fragmented.m4b");
        File.WriteAllBytes(path, Box("moov", Box("udta", Box("chpl", new byte[] { 1 }))));
        var preservation = Mp4ChapterListPreservation.Capture(path);
        var current = Box("moov", []).Concat(Box("moof", [])).ToArray();
        File.WriteAllBytes(path, current);
        Assert.Throws<InvalidDataException>(preservation.RestoreAndVerify);
        Assert.Equal(current, File.ReadAllBytes(path));
    }

    [Fact]
    public void Capture_DuplicateChapterBlocksAndBrokenBoundsFailExplicitly()
    {
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "invalid.m4b");
        var chapterList = Box("chpl", new byte[] { 1 });
        File.WriteAllBytes(path, Box("moov", Box("udta", chapterList.Concat(chapterList).ToArray())));
        Assert.Throws<InvalidDataException>(() => Mp4ChapterListPreservation.Capture(path));
        File.WriteAllBytes(path, new byte[] { 0, 0, 0, 255, (byte)'m', (byte)'o', (byte)'o', (byte)'v' });
        Assert.Throws<InvalidDataException>(() => Mp4ChapterListPreservation.Capture(path));
    }

    private static byte[] Container(byte[]? chapters, byte[] audio, bool fastStart, bool wideOffsets)
    {
        byte[] Movie(ulong offset)
        {
            var offsets = new byte[wideOffsets ? 16 : 12];
            BinaryPrimitives.WriteUInt32BigEndian(offsets.AsSpan(4), 1);
            if (wideOffsets)
                BinaryPrimitives.WriteUInt64BigEndian(offsets.AsSpan(8), offset);
            else
                BinaryPrimitives.WriteUInt32BigEndian(offsets.AsSpan(8), checked((uint)offset));
            var track = Box("trak", Box("mdia", Box("minf", Box("stbl", Box(wideOffsets ? "co64" : "stco", offsets)))));
            return Box("moov", track.Concat(Box("udta", chapters ?? [])).ToArray());
        }
        var movieLength = Movie(0).Length;
        var movie = Movie((ulong)(fastStart ? movieLength + 8 : 8));
        return fastStart ? movie.Concat(Box("mdat", audio)).ToArray() : Box("mdat", audio).Concat(movie).ToArray();
    }

    private static byte[] Box(string type, byte[] payload, bool extended = false)
    {
        var headerSize = extended ? 16 : 8;
        var result = new byte[headerSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result, extended ? 1U : (uint)result.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(result, 4);
        if (extended)
            BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(8), (ulong)result.Length);
        payload.CopyTo(result, headerSize);
        return result;
    }
}
