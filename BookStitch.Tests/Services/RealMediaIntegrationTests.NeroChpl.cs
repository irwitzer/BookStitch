using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed partial class RealMediaIntegrationTests
{
    [Theory]
    [InlineData(3, ".m4a")]
    [InlineData(254, ".m4a")]
    [InlineData(255, ".m4a")]
    [InlineData(300, ".m4a")]
    [InlineData(3, ".m4b")]
    [InlineData(254, ".m4b")]
    [InlineData(255, ".m4b")]
    [InlineData(300, ".m4b")]
    public async Task Final_validation_preserves_all_QuickTime_chapters_without_Nero_after_tagging(int count, string extension)
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var temp = new TemporaryFolder();
        var source = Path.Combine(temp.Path, "source.m4a");
        await CreateSilentAacAsync(tools, source, 3);
        var track = new TrackInfo { SourceId = Guid.NewGuid(), Index = 1, FilePath = source,
            FileName = "source.m4a", DurationTicks = TimeSpan.FromSeconds(3).Ticks,
            EmbeddedChapters = Enumerable.Range(0, count).Select(index => new TrackEmbeddedChapterInfo
            { Title = index == 0 ? new string('界', 100) : "Chapter " + index,
              StartTicks = TimeSpan.FromMilliseconds(index * 3000d / count).Ticks,
              EndTicks = TimeSpan.FromMilliseconds((index + 1) * 3000d / count).Ticks }).ToList() };
        var processing = new AacExportProcessingService();
        var timing = await processing.AnalyzeConvertedTimingAsync(source, tools.FfprobePath, null, CancellationToken.None);
        var segments = ConvertedAacTimelineService.BuildSegments([timing]);
        var chapters = ConvertedAacTimelineService.MapChapters(new ChapterTimelineService().BuildTimeline([track]), [track], segments);
        var concat = Path.Combine(temp.Path, "concat.txt");
        var metadata = Path.Combine(temp.Path, "chapters.txt");
        var output = Path.Combine(temp.Path, "final" + extension);
        await File.WriteAllTextAsync(concat, "file '" + source.Replace('\\', '/') + "'\nduration " + segments[0].ConcatDuration + "\n");
        await File.WriteAllTextAsync(metadata, new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(chapters));
        await processing.MergeConvertedTracksAsync(concat, metadata, output, segments[0].EndMicroseconds * 10,
            tools.FfmpegPath, CancellationToken.None, _ => { });
        var tags = new FinalAudioTagData { Title = "Book", Album = "Book", Author = "Author", Narrator = "Narrator",
            Publisher = "Publisher", Comment = "Comment", Copyright = "Copyright", Genre = "Audiobook", Year = 2026,
            CoverPath = WriteTinyJpeg(temp, "cover.jpg") };
        Assert.Null(ReadNeroForTest(output));
        var quickTimeBefore = ReadQuickTimeSampleHashes(output);
        Assert.Equal(count + 1, quickTimeBefore.Count);
        new FinalTagService().WriteFinalTags(output, tags);
        Assert.Null(ReadNeroForTest(output));
        Assert.Equal(quickTimeBefore, ReadQuickTimeSampleHashes(output));
        var validator = new FinalizedOutputValidationService();
        var inspection = await validator.InspectAsync(output, tools.FfprobePath, null, CancellationToken.None);
        Assert.Equal(count, inspection.ProbeInfo!.Chapters.Count);
        Assert.Null(inspection.NeroChapters);
        var snapshot = new FinalizationSnapshot(null, tags, new Dictionary<ProjectMetadataField, OutputFieldPolicy>(), null,
            "en", true, tags.CoverPath)
        { ExpectedDuration = TimeSpan.FromTicks(segments[0].EndMicroseconds * 10),
          ExpectedChapters = chapters.Select(chapter => new TrackEmbeddedChapterInfo
          { Title = chapter.Title, StartTicks = chapter.StartTicks, EndTicks = chapter.EndTicks }).ToArray() };
        validator.Validate(snapshot, inspection);
    }

    [Fact]
    public async Task NeroChpl_RealFfmpegExportsProtectTheCurrentBlockAcrossMetadataChaptersSourcesAndPresetChanges()
    {
        var tools = ResolveFfmpegToolchain();
        Assert.NotNull(tools);
        using var temp = new TemporaryFolder();
        var processing = new AacExportProcessingService();
        var tagger = new FinalTagService();
        var source = Path.Combine(temp.Path, "source.wav");
        await RunProcessAsync(tools.FfmpegPath,
            ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=700:sample_rate=44100:duration=1.251", source]);
        var tracks = new List<TrackInfo>
        {
            new() { SourceId = Guid.NewGuid(), Index = 1, FilePath = source, FileName = "one.wav",
                ChapterTitle = "First ÄÖÜ", DurationTicks = 12510000, ProcessingAction = ProcessingActionIds.Convert }
        };
        var preset = Stereo96;
        var paths = new List<string> { Path.Combine(temp.Path, "one.m4a") };
        await processing.PrepareTrackForExportAsync(tracks[0], source, paths[0], preset,
            tools.FfmpegPath, CancellationToken.None, _ => { });
        byte[]? previous = null;
        var output = Path.Combine(temp.Path, "final.m4b");
        var concat = Path.Combine(temp.Path, "concat.txt");
        var metadata = Path.Combine(temp.Path, "chapters.txt");
        for (var scenario = 0; scenario < 5; scenario++)
        {
            if (scenario == 2)
                tracks[0].EmbeddedChapters =
                [new TrackEmbeddedChapterInfo { Title = "New chapter 東京", StartTicks = 400000, EndTicks = 2100000 }];
            if (scenario == 3)
            {
                var second = new TrackInfo { SourceId = Guid.NewGuid(), Index = 2, FilePath = source,
                    FileName = "two.wav", ChapterTitle = "Added source", DurationTicks = 12510000,
                    ProcessingAction = ProcessingActionIds.Convert };
                tracks.Add(second);
                paths.Add(Path.Combine(temp.Path, "two.m4a"));
                await processing.PrepareTrackForExportAsync(second, source, paths[1], preset,
                    tools.FfmpegPath, CancellationToken.None, _ => { });
            }
            if (scenario == 4)
            {
                preset = new ExportPreset("AAC Mono 64 kbps", 64, 1);
                tracks[0].EmbeddedChapters[0].Title = "Reconverted preset chapter";
                for (var index = 0; index < tracks.Count; index++)
                    await processing.PrepareTrackForExportAsync(tracks[index], source, paths[index], preset,
                        tools.FfmpegPath, CancellationToken.None, _ => { });
            }
            var timings = new List<ConvertedAacTiming>();
            foreach (var path in paths)
                timings.Add(await processing.AnalyzeConvertedTimingAsync(path, tools.FfprobePath, null, CancellationToken.None));
            var segments = ConvertedAacTimelineService.BuildSegments(timings);
            var chapters = ConvertedAacTimelineService.MapChapters(
                new ChapterTimelineService().BuildTimeline(tracks), tracks, segments);
            await File.WriteAllLinesAsync(concat, paths.SelectMany((path, index) => new[]
                { "file '" + path.Replace('\\', '/') + "'", "duration " + segments[index].ConcatDuration }), new UTF8Encoding(false));
            await File.WriteAllTextAsync(metadata, new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(chapters));
            await processing.MergeConvertedTracksAsync(concat, metadata, output, segments[^1].EndMicroseconds * 10,
                tools.FfmpegPath, CancellationToken.None, _ => { });
            // Deliberately create a legacy Nero container for this preservation regression.
            var legacy = Path.Combine(temp.Path, "legacy.m4b");
            await RunProcessAsync(tools.FfmpegPath,
                ["-v", "error", "-y", "-i", output, "-i", metadata, "-map", "0:a", "-map_chapters", "1",
                 "-c", "copy", "-movflags", "+faststart", legacy]);
            File.Move(legacy, output, overwrite: true);
            var expected = ReadNeroForTest(output);
            Assert.NotNull(expected);
            AssertExpectedNeroChapters(expected, chapters);
            if (scenario == 1)
                Assert.Equal(previous, expected);
            else if (previous is not null)
                Assert.False(previous.AsSpan().SequenceEqual(expected), "A changed chapter model must produce a fresh Nero block.");
            var audioBefore = await ProbePacketsAsync(output, tools.FfprobePath);
            var quickTimeBefore = ReadQuickTimeSampleHashes(output);
            Assert.NotEmpty(quickTimeBefore);
            var chaptersBefore = await new AudioInfoService().ProbeAsync(output, tools.FfprobePath);
            Assert.True(chaptersBefore.Success, chaptersBefore.ErrorMessage);
            Assert.NotNull(Mp4ChapterListPreservation.ReadChapters(output));
            for (var update = 0; update < 3; update++)
            {
                tagger.WriteFinalTags(output, new FinalAudioTagData
                { Title = new string('T', 2000 * update + 1), Album = "Metadata update " + update,
                  Author = "One author", Narrator = "One narrator", Genre = "Audiobook", Year = 2026 });
                Assert.Equal(expected, ReadNeroForTest(output));
                Assert.Equal(audioBefore, await ProbePacketsAsync(output, tools.FfprobePath));
                Assert.Equal(quickTimeBefore, ReadQuickTimeSampleHashes(output));
                var probe = await new AudioInfoService().ProbeAsync(output, tools.FfprobePath);
                Assert.True(probe.Success, probe.ErrorMessage);
                Assert.Equal(chapters.Select(chapter => chapter.Title), probe.Chapters.Select(chapter => chapter.Title));
                Assert.Equal(chaptersBefore.Chapters.Select(chapter => (chapter.StartTicks, chapter.EndTicks)),
                    probe.Chapters.Select(chapter => (chapter.StartTicks, chapter.EndTicks)));
            }
            previous = expected;
        }

        // Exercise the missing-block repair against an actual faststart file, including
        // the offsets of the QuickTime chapter track and the unchanged audio payload.
        var preservation = Mp4ChapterListPreservation.Capture(output);
        var packetsBeforeRepair = await ProbePacketsAsync(output, tools.FfprobePath);
        var quickTimeBeforeRepair = ReadQuickTimeSampleHashes(output);
        var originalBytes = File.ReadAllBytes(output);
        var position = originalBytes.AsSpan().IndexOf(previous);
        Assert.True(position >= 0);
        Encoding.ASCII.GetBytes("free").CopyTo(originalBytes, position + 4);
        File.WriteAllBytes(output, originalBytes);
        Assert.Null(ReadNeroForTest(output));
        preservation.RestoreAndVerify();
        Assert.Equal(previous, ReadNeroForTest(output));
        Assert.Equal(packetsBeforeRepair, await ProbePacketsAsync(output, tools.FfprobePath));
        Assert.Equal(quickTimeBeforeRepair, ReadQuickTimeSampleHashes(output));
        var afterRepair = await new AudioInfoService().ProbeAsync(output, tools.FfprobePath);
        Assert.True(afterRepair.Success, afterRepair.ErrorMessage);
        Assert.Equal(2, afterRepair.Chapters.Count);

        await File.WriteAllTextAsync(metadata, ";FFMETADATA1\n");
        await processing.MergeConvertedTracksAsync(concat, metadata, output, 0, tools.FfmpegPath, CancellationToken.None, _ => { });
        Assert.Null(ReadNeroForTest(output));
        tagger.WriteFinalTags(output, new FinalAudioTagData { Title = "No chapters in this export" });
        Assert.Null(ReadNeroForTest(output));
        _output.WriteLine("Current FFmpeg chpl preserved byte-for-byte across 5 export scenarios and 15 metadata updates; " +
            "audio packets and QuickTime chapters unchanged; missing-block repair and chapter-free export verified.");
    }

    private static void AssertExpectedNeroChapters(byte[] block, IReadOnlyList<EffectiveChapterInfo> expected)
    {
        Assert.True(block.Length >= 17);
        Assert.Equal(1, block[8]); // FFmpeg's version-one Nero chapter list.
        Assert.Equal(expected.Count, block[16]);
        var position = 17;
        foreach (var chapter in expected)
        {
            Assert.True(block.Length - position >= 9);
            var startTicks = checked((long)BinaryPrimitives.ReadUInt64BigEndian(block.AsSpan(position, 8)));
            position += 8;
            var titleLength = block[position++];
            Assert.True(block.Length - position >= titleLength);
            Assert.Equal(chapter.Title, Encoding.UTF8.GetString(block, position, titleLength));
            Assert.InRange(Math.Abs(startTicks - chapter.StartTicks), 0, TimeSpan.FromMilliseconds(1).Ticks);
            position += titleLength;
        }
        Assert.Equal(block.Length, position);
    }

    private static byte[]? ReadNeroForTest(string path)
    {
        var bytes = File.ReadAllBytes(path);
        byte[]? Read(int start, int end, string sought)
        {
            while (start < end)
            {
                var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(start, 4)));
                Assert.InRange(length, 8, end - start);
                var type = Encoding.ASCII.GetString(bytes, start + 4, 4);
                if (type == sought)
                {
                    if (sought == "chpl")
                        return bytes.AsSpan(start, length).ToArray();
                    var found = Read(start + 8, start + length, sought == "moov" ? "udta" : "chpl");
                    if (found is not null)
                        return found;
                }
                start += length;
            }
            return null;
        }
        return Read(0, bytes.Length, "moov");
    }

    // FFprobe consumes QuickTime chapter packets internally and does not expose them
    // through -show_packets. Read samples through their actual container offsets instead.
    private static List<string> ReadQuickTimeSampleHashes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        uint UInt32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        List<(int Offset, int Size, string Type)> Children(int start, int end)
        {
            var result = new List<(int, int, string)>();
            while (start < end)
            {
                var size = checked((int)UInt32(start));
                Assert.InRange(size, 8, end - start);
                result.Add((start, size, Encoding.ASCII.GetString(bytes, start + 4, 4)));
                start += size;
            }
            return result;
        }
        List<(int Offset, int Size, string Type)> Inside((int Offset, int Size, string Type) box) =>
            Children(box.Offset + 8, box.Offset + box.Size);
        string Hash(int offset, int size) => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(offset, size)));
        var result = new List<string>();
        var movie = Children(0, bytes.Length).Single(box => box.Type == "moov");
        foreach (var track in Inside(movie).Where(box => box.Type == "trak"))
        {
            var media = Inside(track).Single(box => box.Type == "mdia");
            var handler = Inside(media).Single(box => box.Type == "hdlr");
            if (Encoding.ASCII.GetString(bytes, handler.Offset + 16, 4) != "text")
                continue;
            var info = Inside(media).Single(box => box.Type == "minf");
            var table = Inside(info).Single(box => box.Type == "stbl");
            var entries = Inside(table);
            var sizes = entries.Single(box => box.Type == "stsz");
            var chunks = entries.Single(box => box.Type is "stco" or "co64");
            var mapping = entries.Single(box => box.Type == "stsc");
            var times = entries.Single(box => box.Type == "stts");
            result.Add(Hash(times.Offset, times.Size));
            var sampleIndex = 0;
            for (var chunkIndex = 0; chunkIndex < UInt32(chunks.Offset + 12); chunkIndex++)
            {
                var chunkField = chunks.Offset + 16 + chunkIndex * (chunks.Type == "stco" ? 4 : 8);
                var chunkOffset = chunks.Type == "stco" ? UInt32(chunkField) :
                    BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(chunkField, 8));
                var sampleOffset = checked((int)chunkOffset);
                var mappingIndex = 0;
                for (var candidate = 0; candidate < UInt32(mapping.Offset + 12); candidate++)
                    if (UInt32(mapping.Offset + 16 + candidate * 12) <= chunkIndex + 1)
                        mappingIndex = candidate;
                var sampleCount = UInt32(mapping.Offset + 20 + mappingIndex * 12);
                for (var sample = 0; sample < sampleCount; sample++)
                {
                    var sampleSize = UInt32(sizes.Offset + 12);
                    if (sampleSize == 0)
                        sampleSize = UInt32(sizes.Offset + 20 + sampleIndex * 4);
                    result.Add(Hash(sampleOffset, checked((int)sampleSize)));
                    sampleOffset += checked((int)sampleSize);
                    sampleIndex++;
                }
            }
            Assert.Equal(UInt32(sizes.Offset + 16), (uint)sampleIndex);
        }
        return result;
    }
}
