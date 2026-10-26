using BookStitch.Models;
using System.Globalization;
using System.Text;

namespace BookStitch.Services;

public sealed class ExportChapterService
{
    public string BuildFfmpegChapterMetadataFromTimeline(
        IReadOnlyList<EffectiveChapterInfo> renderedTimeline)
    {
        ArgumentNullException.ThrowIfNull(renderedTimeline);
        return BuildFfmpegChapterMetadataCore(
            renderedTimeline,
            chapter => chapter.Title);
    }

    private string BuildFfmpegChapterMetadataCore(
        IReadOnlyList<EffectiveChapterInfo> timeline,
        Func<EffectiveChapterInfo, string> titleFactory)
    {
        var builder = new StringBuilder();
        builder.AppendLine(";FFMETADATA1");

        foreach (var chapter in timeline)
            AppendChapter(builder, chapter.StartTicks, chapter.EndTicks, titleFactory(chapter));

        return builder.ToString();
    }

    public string EscapeFfmpegMetadataValue(string value)
    {
        return (value ?? "")
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace("#", "\\#", StringComparison.Ordinal);
    }

    private void AppendChapter(StringBuilder builder, long startTicks, long endTicks, string title)
    {
        builder.AppendLine();
        builder.AppendLine("[CHAPTER]");
        builder.AppendLine("TIMEBASE=1/1000");
        builder.Append("START=").AppendLine(ToMilliseconds(startTicks).ToString(CultureInfo.InvariantCulture));
        builder.Append("END=").AppendLine(ToMilliseconds(endTicks).ToString(CultureInfo.InvariantCulture));
        builder.Append("title=").AppendLine(EscapeFfmpegMetadataValue(title));
    }

    private static long ToMilliseconds(long ticks)
    {
        return (long)Math.Round(ticks / (double)TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero);
    }
}
