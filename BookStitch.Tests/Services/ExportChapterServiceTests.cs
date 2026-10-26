using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ExportChapterServiceTests
{
    private readonly ExportChapterService _service = new();

    [Fact]
    public void EscapeFfmpegMetadataValue_EscapesFfmpegSpecialCharacters()
    {
        var result = _service.EscapeFfmpegMetadataValue(@"A=B;C#D\E");

        Assert.Equal(@"A\=B\;C\#D\\E", result);
    }

    [Fact]
    public void EscapeFfmpegMetadataValue_ReplacesLineBreaksWithSpaces()
    {
        var result = _service.EscapeFfmpegMetadataValue("A\r\nB");

        Assert.Equal("A  B", result);
    }

    [Fact]
    public void BuildFfmpegChapterMetadataFromTimeline_SerializesRenderedTimeline()
    {
        var track = new TrackInfo();
        var metadata = _service.BuildFfmpegChapterMetadataFromTimeline(
        [
            Chapter(1, track, "Start", 0, 10_000_000),
            Chapter(2, track, "Weiter", 10_000_000, 30_000_000)
        ]);

        Assert.Contains(";FFMETADATA1", metadata);
        Assert.Equal([0, 1000], ExtractChapterStarts(metadata));
        Assert.Equal([1000, 3000], ExtractChapterEnds(metadata));
        Assert.Equal(["Start", "Weiter"], ExtractChapterTitles(metadata));
    }

    [Fact]
    public void BuildFfmpegChapterMetadataFromTimeline_EscapesChapterTitles()
    {
        var metadata = _service.BuildFfmpegChapterMetadataFromTimeline(
        [
            Chapter(1, new TrackInfo(), "A=B;C#D", 0, 10_000_000)
        ]);

        Assert.Contains(@"title=A\=B\;C\#D", metadata);
    }

    [Fact]
    public void BuildFfmpegChapterMetadataFromTimeline_UsesRoundedMillisecondPositions()
    {
        var metadata = _service.BuildFfmpegChapterMetadataFromTimeline(
        [
            Chapter(1, new TrackInfo(), "Precise", ticksStart: 15_390_000, ticksEnd: 30_780_000)
        ]);

        Assert.Equal([1539], ExtractChapterStarts(metadata));
        Assert.Equal([3078], ExtractChapterEnds(metadata));
    }

    [Fact]
    public void BuildFfmpegChapterMetadataFromTimeline_PreservesUnicodeChapterTitles()
    {
        var metadata = _service.BuildFfmpegChapterMetadataFromTimeline(
        [
            Chapter(1, new TrackInfo(), "Überraschung im Märchenwald", 0, 10_000_000)
        ]);

        Assert.Contains("title=Überraschung im Märchenwald", metadata);
    }

    private static EffectiveChapterInfo Chapter(
        int index,
        TrackInfo track,
        string title,
        long ticksStart,
        long ticksEnd) =>
        new(
            index,
            track,
            SourceEmbeddedChapter: null,
            title,
            ticksStart,
            ticksEnd,
            ticksStart,
            ticksEnd,
            IsSynthetic: true);

    private static List<long> ExtractChapterStarts(string metadata)
    {
        return metadata
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("START=", StringComparison.Ordinal))
            .Select(line => long.Parse(line["START=".Length..]))
            .ToList();
    }

    private static List<long> ExtractChapterEnds(string metadata)
    {
        return metadata
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("END=", StringComparison.Ordinal))
            .Select(line => long.Parse(line["END=".Length..]))
            .ToList();
    }

    private static List<string> ExtractChapterTitles(string metadata)
    {
        return metadata
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("title=", StringComparison.Ordinal))
            .Select(line => line["title=".Length..])
            .ToList();
    }
}
