using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class ExportChapterServiceTests
{
    private readonly ExportChapterService _service = new();

    [Fact]
    public void BuildFinalChapterTitle_UsesChapterTitleFirst()
    {
        var track = new TrackInfo
        {
            ChapterTitle = " Kapitel A ",
            TagTitle = "Tag Titel",
            FileName = "Track 01.mp3"
        };

        var result = _service.BuildFinalChapterTitle(track, 1);

        Assert.Equal("Kapitel A", result);
    }

    [Fact]
    public void BuildFinalChapterTitle_UsesTagTitleWhenChapterTitleIsEmpty()
    {
        var track = new TrackInfo
        {
            ChapterTitle = "",
            TagTitle = " Tag Titel ",
            FileName = "Track 01.mp3"
        };

        var result = _service.BuildFinalChapterTitle(track, 1);

        Assert.Equal("Tag Titel", result);
    }

    [Fact]
    public void BuildFinalChapterTitle_UsesFileNameWithoutExtensionWhenTitlesAreEmpty()
    {
        var track = new TrackInfo
        {
            ChapterTitle = "",
            TagTitle = "",
            FileName = "Track 01.mp3"
        };

        var result = _service.BuildFinalChapterTitle(track, 1);

        Assert.Equal("Track 01", result);
    }

    [Fact]
    public void BuildFinalChapterTitle_UsesFallbackWhenEverythingIsEmpty()
    {
        var track = new TrackInfo();

        var result = _service.BuildFinalChapterTitle(track, 7);

        Assert.Equal("Kapitel 7", result);
    }

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
    public void BuildFfmpegChapterMetadata_BuildsSequentialChapters()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                ChapterTitle = "Start",
                Duration = "00:01"
            },
            new TrackInfo
            {
                ChapterTitle = "Weiter",
                Duration = "00:02"
            }
        };

        var metadata = _service.BuildFfmpegChapterMetadata(tracks);

        Assert.Contains(";FFMETADATA1", metadata);
        Assert.Contains("START=0", metadata);
        Assert.Contains("END=1000", metadata);
        Assert.Contains("title=Start", metadata);
        Assert.Contains("START=1000", metadata);
        Assert.Contains("END=3000", metadata);
        Assert.Contains("title=Weiter", metadata);
    }

    [Fact]
    public void BuildFfmpegChapterMetadata_ExpandsEmbeddedContainerChapters()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                ChapterTitle = "Gesamtdatei",
                DurationTicks = TimeSpan.FromSeconds(30).Ticks,
                EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo { Title = "Eins", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(10).Ticks },
                    new TrackEmbeddedChapterInfo { Title = "Zwei", StartTicks = TimeSpan.FromSeconds(10).Ticks, EndTicks = TimeSpan.FromSeconds(30).Ticks }
                ]
            },
            new TrackInfo
            {
                ChapterTitle = "Nachlauf",
                DurationTicks = TimeSpan.FromSeconds(5).Ticks
            }
        };

        var metadata = _service.BuildFfmpegChapterMetadata(tracks);

        Assert.Contains("START=0", metadata);
        Assert.Contains("END=10000", metadata);
        Assert.Contains("title=Eins", metadata);
        Assert.Contains("START=10000", metadata);
        Assert.Contains("END=30000", metadata);
        Assert.Contains("title=Zwei", metadata);
        Assert.Contains("START=30000", metadata);
        Assert.Contains("END=35000", metadata);
        Assert.Contains("title=Nachlauf", metadata);
    }

    [Fact]
    public void BuildFfmpegChapterMetadata_ParsesHourMinuteSecondDurations()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                ChapterTitle = "Lang",
                Duration = "01:02:03"
            }
        };

        var metadata = _service.BuildFfmpegChapterMetadata(tracks);

        Assert.Contains("START=0", metadata);
        Assert.Contains("END=3723000", metadata);
    }

    [Fact]
    public void BuildFfmpegChapterMetadata_UsesOneSecondFallbackForMissingDuration()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                ChapterTitle = "Ohne Dauer",
                Duration = ""
            }
        };

        var metadata = _service.BuildFfmpegChapterMetadata(tracks);

        Assert.Contains("START=0", metadata);
        Assert.Contains("END=1000", metadata);
    }

    [Fact]
    public void BuildFfmpegChapterMetadata_EscapesChapterTitles()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                ChapterTitle = "A=B;C#D",
                Duration = "00:01"
            }
        };

        var metadata = _service.BuildFfmpegChapterMetadata(tracks);

        Assert.Contains(@"title=A\=B\;C\#D", metadata);
    }

    [Fact]
    public void BuildFfmpegChapterMetadata_UsesDurationTicksWithoutCumulativeDrift()
    {
        var tracks = Enumerable.Range(1, 175)
            .Select(index => new TrackInfo
            {
                ChapterTitle = $"Kapitel {index}",
                Duration = "00:00",
                DurationTicks = TimeSpan.FromSeconds(1.539).Ticks
            })
            .ToList();

        var metadata = _service.BuildFfmpegChapterMetadata(tracks);
        var starts = ExtractChapterStarts(metadata);

        Assert.Equal(0, starts[0]);
        Assert.Equal(1539, starts[1]);
        Assert.Equal(1539 * 99, starts[99]);
        Assert.Equal(1539 * 174, starts[174]);
    }

    [Fact]
    public void BuildFfmpegChapterMetadata_ParsesFractionalSecondsWithoutCumulativeDrift()
    {
        var tracks = Enumerable.Range(1, 120)
            .Select(index => new TrackInfo
            {
                ChapterTitle = $"Kapitel {index}",
                Duration = "00:01.250"
            })
            .ToList();

        var metadata = _service.BuildFfmpegChapterMetadata(tracks);
        var starts = ExtractChapterStarts(metadata);

        Assert.Equal(0, starts[0]);
        Assert.Equal(1250, starts[1]);
        Assert.Equal(1250 * 60, starts[60]);
        Assert.Equal(1250 * 119, starts[119]);
    }

    [Fact]
    public void BuildFfmpegChapterMetadata_PrefersDurationTicksOverDisplayDuration()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                ChapterTitle = "A",
                Duration = "00:01",
                DurationTicks = TimeSpan.FromMilliseconds(1750).Ticks
            },
            new TrackInfo
            {
                ChapterTitle = "B",
                Duration = "00:01",
                DurationTicks = TimeSpan.FromMilliseconds(2250).Ticks
            }
        };

        var metadata = _service.BuildFfmpegChapterMetadata(tracks);

        Assert.Contains("END=1750", metadata);
        Assert.Contains("START=1750", metadata);
        Assert.Contains("END=4000", metadata);
    }

    [Fact]
    public void BuildFfmpegChapterMetadata_PreservesUnicodeChapterTitles()
    {
        var tracks = new[]
        {
            new TrackInfo
            {
                ChapterTitle = "Überraschung im Märchenwald",
                Duration = "00:01"
            }
        };

        var metadata = _service.BuildFfmpegChapterMetadata(tracks);

        Assert.Contains("title=Überraschung im Märchenwald", metadata);
    }

    private static List<long> ExtractChapterStarts(string metadata)
    {
        return metadata
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("START=", StringComparison.Ordinal))
            .Select(line => long.Parse(line["START=".Length..]))
            .ToList();
    }
}
