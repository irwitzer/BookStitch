using BookStitch.Models;
using System.IO;
using System.Globalization;
using System.Text;

namespace BookStitch.Services;

public sealed class ExportChapterService
{
    public string BuildFfmpegChapterMetadata(IReadOnlyList<TrackInfo> tracks)
    {
        var builder = new StringBuilder();
        builder.AppendLine(";FFMETADATA1");

        var currentStartTicks = 0L;
        var fallbackIndex = 1;

        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            var trackDuration = GetPreciseDuration(track) ?? TimeSpan.FromSeconds(1);
            var trackDurationTicks = Math.Max(1L, trackDuration.Ticks);

            if (track.EmbeddedChapters.Count > 0)
            {
                foreach (var embeddedChapter in NormalizeEmbeddedChapters(track, fallbackIndex))
                {
                    var chapterStartTicks = currentStartTicks + embeddedChapter.StartTicks;
                    var chapterEndTicks = currentStartTicks + embeddedChapter.EndTicks;
                    if (chapterEndTicks <= chapterStartTicks)
                        chapterEndTicks = chapterStartTicks + TimeSpan.TicksPerSecond;

                    AppendChapter(builder, chapterStartTicks, chapterEndTicks, embeddedChapter.Title);
                    fallbackIndex++;
                }
            }
            else
            {
                var currentEndTicks = currentStartTicks + trackDurationTicks;
                var chapterTitle = BuildFinalChapterTitle(track, fallbackIndex);
                AppendChapter(builder, currentStartTicks, currentEndTicks, chapterTitle);
                fallbackIndex++;
            }

            currentStartTicks += trackDurationTicks;
        }

        return builder.ToString();
    }

    public string BuildFinalChapterTitle(TrackInfo track, int fallbackIndex)
    {
        if (!string.IsNullOrWhiteSpace(track.ChapterTitle))
            return track.ChapterTitle.Trim();

        if (!string.IsNullOrWhiteSpace(track.TagTitle))
            return track.TagTitle.Trim();

        if (!string.IsNullOrWhiteSpace(track.FileName))
            return Path.GetFileNameWithoutExtension(track.FileName).Trim();

        return "Kapitel " + fallbackIndex.ToString(CultureInfo.InvariantCulture);
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

    private static IEnumerable<TrackEmbeddedChapterInfo> NormalizeEmbeddedChapters(TrackInfo track, int firstFallbackIndex)
    {
        var fallbackIndex = firstFallbackIndex;
        foreach (var chapter in track.EmbeddedChapters
                     .Where(chapter => chapter.EndTicks > chapter.StartTicks)
                     .OrderBy(chapter => chapter.StartTicks))
        {
            var title = string.IsNullOrWhiteSpace(chapter.Title)
                ? "Kapitel " + fallbackIndex.ToString(CultureInfo.InvariantCulture)
                : chapter.Title.Trim();
            fallbackIndex++;

            yield return new TrackEmbeddedChapterInfo
            {
                Title = title,
                StartTicks = Math.Max(0, chapter.StartTicks),
                EndTicks = Math.Max(chapter.EndTicks, chapter.StartTicks + 1)
            };
        }
    }

    private static TimeSpan? GetPreciseDuration(TrackInfo track)
    {
        if (track.DurationTicks is > 0)
            return TimeSpan.FromTicks(track.DurationTicks.Value);

        return TryParseDuration(track.Duration);
    }

    private static long ToMilliseconds(long ticks)
    {
        return (long)Math.Round(ticks / (double)TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero);
    }

    private static TimeSpan? TryParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Trim().Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }

        if (parts.Length == 3 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes) &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
        {
            return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }

        return null;
    }
}
