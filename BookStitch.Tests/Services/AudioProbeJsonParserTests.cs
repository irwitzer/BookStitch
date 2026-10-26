using System.Globalization;
using System.Text;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioProbeJsonParserTests
{
    [Theory]
    [InlineData(@"C:\Audio\book.m4a", "mov,mp4,m4a,3gp,3g2,mj2", "aac")]
    [InlineData(@"C:\Audio\book.m4b", "mov,mp4,m4a,3gp,3g2,mj2", "aac")]
    [InlineData(@"C:\Audio\book.mp3", "mp3", "mp3")]
    [InlineData(@"C:\Audio\book.flac", "flac", "flac")]
    [InlineData(@"C:\Audio\book.wma", "asf", "wmav2")]
    public void Parse_UsesFfprobeChaptersForSupportedAndBestEffortFormats(
        string filePath,
        string formatName,
        string codecName)
    {
        var json = BuildProbeJson(
            formatName,
            codecName,
            [
                new("Intro", 0, 1250),
                new("Überraschung", 1250, 2500)
            ],
            durationSeconds: 2.5);

        var info = new AudioProbeJsonParser().Parse(filePath, json);

        Assert.True(info.Success);
        Assert.Equal(formatName, info.FormatName);
        Assert.Equal(codecName, info.CodecName);
        Assert.Equal(TimeSpan.FromMilliseconds(2500), info.Duration);
        Assert.Collection(
            info.Chapters,
            chapter =>
            {
                Assert.Equal("Intro", chapter.Title);
                Assert.Equal(TimeSpan.Zero.Ticks, chapter.StartTicks);
                Assert.Equal(TimeSpan.FromMilliseconds(1250).Ticks, chapter.EndTicks);
            },
            chapter =>
            {
                Assert.Equal("Überraschung", chapter.Title);
                Assert.Equal(TimeSpan.FromMilliseconds(1250).Ticks, chapter.StartTicks);
                Assert.Equal(TimeSpan.FromMilliseconds(2500).Ticks, chapter.EndTicks);
            });
    }

    [Theory]
    [InlineData(@"C:\Audio\raw.aac", "aac", "aac")]
    [InlineData(@"C:\Audio\source.wav", "wav", "pcm_s16le")]
    public void Parse_DoesNotInventChaptersForFormatsWithoutGuaranteedChapterSupport(
        string filePath,
        string formatName,
        string codecName)
    {
        var json = BuildProbeJson(formatName, codecName, [], durationSeconds: 10);

        var info = new AudioProbeJsonParser().Parse(filePath, json);

        Assert.True(info.Success);
        Assert.Empty(info.Chapters);
    }

    [Fact]
    public void Parse_ReadsTwoHundredChaptersFromLargeFfprobeJson()
    {
        var json = BuildProbeJson(200);
        Assert.True(json.Length > 40000);

        var info = new AudioProbeJsonParser().Parse(@"C:\Audio\large.m4b", json);

        Assert.True(info.Success);
        Assert.Equal("aac", info.CodecName);
        Assert.Equal(TimeSpan.FromSeconds(200), info.Duration);
        Assert.Equal(200, info.Chapters.Count);
        Assert.Equal("Kapitel 001", info.Chapters[0].Title);
        Assert.Equal("Überraschung 200", info.Chapters[^1].Title);
        Assert.Equal(TimeSpan.FromSeconds(199).Ticks, info.Chapters[^1].StartTicks);
        Assert.Equal(TimeSpan.FromSeconds(200).Ticks, info.Chapters[^1].EndTicks);
    }

    [Fact]
    public void Parse_PreservesMissingChapterTitlesAsEmptySourceData()
    {
        var json = """
            {
              "format": { "duration": "2.000000" },
              "streams": [ { "codec_type": "audio", "codec_name": "aac" } ],
              "chapters": [
                { "time_base": "1/1000", "start": 0, "end": 1000 },
                { "time_base": "1/1000", "start": 1000, "end": 2000, "tags": { "title": "   " } }
              ]
            }
            """;

        var info = new AudioProbeJsonParser().Parse(@"C:\Audio\untitled.m4b", json);

        Assert.True(info.Success);
        Assert.Equal(2, info.Chapters.Count);
        Assert.Equal(string.Empty, info.Chapters[0].Title);
        Assert.Equal(string.Empty, info.Chapters[1].Title);
    }

    [Fact]
    public void Parse_PreservesExistingChapterTitles()
    {
        var json = """
            {
              "format": { "duration": "1.000000" },
              "streams": [ { "codec_type": "audio", "codec_name": "aac" } ],
              "chapters": [
                { "time_base": "1/1000", "start": 0, "end": 1000, "tags": { "TITLE": "  Originaltitel  " } }
              ]
            }
            """;

        var info = new AudioProbeJsonParser().Parse(@"C:\Audio\titled.m4b", json);

        var chapter = Assert.Single(info.Chapters);
        Assert.Equal("Originaltitel", chapter.Title);
    }

    [Fact]
    public void Parse_PreservesPreciseChapterTimesFromTimeBase()
    {
        var json = """
            {
              "format": { "format_name": "flac", "duration": "123.456789" },
              "streams": [ { "codec_type": "audio", "codec_name": "flac" } ],
              "chapters": [
                { "time_base": "1/10000000", "start": 1234567, "end": 23456789, "tags": { "title": "Genau" } }
              ]
            }
            """;

        var info = new AudioProbeJsonParser().Parse(@"C:\Audio\precise.flac", json);

        var chapter = Assert.Single(info.Chapters);
        Assert.Equal(TimeSpan.FromSeconds(0.1234567).Ticks, chapter.StartTicks);
        Assert.Equal(TimeSpan.FromSeconds(2.3456789).Ticks, chapter.EndTicks);
    }

    private static string BuildProbeJson(int chapterCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("""  "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "200.000000", "bit_rate": "192000" },""");
        builder.AppendLine("""  "streams": [ { "codec_type": "audio", "codec_name": "aac", "codec_long_name": "AAC", "sample_rate": "44100", "channels": 2 } ],""");
        builder.AppendLine("""  "chapters": [""");

        for (var index = 1; index <= chapterCount; index++)
        {
            var title = index == chapterCount
                ? $"Überraschung {index.ToString(CultureInfo.InvariantCulture)}"
                : $"Kapitel {index.ToString("000", CultureInfo.InvariantCulture)}";
            builder.AppendLine("    {");
            builder.AppendLine("""      "time_base": "1/1000",""");
            builder.AppendLine($"""      "start": {(index - 1) * 1000},""");
            builder.AppendLine($"""      "start_time": "{(index - 1).ToString("0.000000", CultureInfo.InvariantCulture)}",""");
            builder.AppendLine($"""      "end": {index * 1000},""");
            builder.AppendLine($"""      "end_time": "{index.ToString("0.000000", CultureInfo.InvariantCulture)}",""");
            builder.AppendLine("      \"tags\": { \"title\": \"" + title + "\" }");
            builder.Append(index == chapterCount ? "    }" : "    },");
            builder.AppendLine();
        }

        builder.AppendLine("  ]");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildProbeJson(
        string formatName,
        string codecName,
        IReadOnlyList<ChapterJsonFixture> chapters,
        double durationSeconds)
    {
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine($"  \"format\": {{ \"format_name\": \"{formatName}\", \"duration\": \"{durationSeconds.ToString("0.000000", CultureInfo.InvariantCulture)}\", \"bit_rate\": \"192000\" }},");
        builder.AppendLine($"  \"streams\": [ {{ \"codec_type\": \"audio\", \"codec_name\": \"{codecName}\", \"sample_rate\": \"44100\", \"channels\": 2 }} ],");
        builder.AppendLine("""  "chapters": [""");

        for (var index = 0; index < chapters.Count; index++)
        {
            var chapter = chapters[index];
            builder.AppendLine("    {");
            builder.AppendLine("""      "time_base": "1/1000",""");
            builder.AppendLine($"""      "start": {chapter.StartMilliseconds.ToString(CultureInfo.InvariantCulture)},""");
            builder.AppendLine($"""      "end": {chapter.EndMilliseconds.ToString(CultureInfo.InvariantCulture)},""");
            builder.AppendLine("      \"tags\": { \"title\": \"" + chapter.Title + "\" }");
            builder.Append(index == chapters.Count - 1 ? "    }" : "    },");
            builder.AppendLine();
        }

        builder.AppendLine("  ]");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private sealed record ChapterJsonFixture(string Title, long StartMilliseconds, long EndMilliseconds);
}
