using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class ChapterCustomTitleService
{
    private readonly ChapterTitleRenderer _renderer;
    private readonly ChapterCustomTextCleanupService _cleanupService;

    public ChapterCustomTitleService()
        : this(new ChapterTitleRenderer(), new ChapterCustomTextCleanupService())
    {
    }

    public ChapterCustomTitleService(
        ChapterTitleRenderer renderer,
        ChapterCustomTextCleanupService cleanupService)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _cleanupService = cleanupService ?? throw new ArgumentNullException(nameof(cleanupService));
    }

    public string Render(
        TrackInfo track,
        ChapterSettings settings,
        int chapterPosition,
        int activeChapterCount,
        string? bookTitle,
        string? album)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(settings);

        var parts = new[]
        {
            RenderSlot(settings.Slot1, track, settings, chapterPosition, activeChapterCount, bookTitle, album),
            RenderSlot(settings.Slot2, track, settings, chapterPosition, activeChapterCount, bookTitle, album),
            RenderSlot(settings.Slot3, track, settings, chapterPosition, activeChapterCount, bookTitle, album)
        };
        var separators = new[]
        {
            ResolveSeparator(settings.Separator1),
            ResolveSeparator(settings.Separator2)
        };

        var result = string.Empty;
        var lastSlotIndex = -1;
        for (var i = 0; i < parts.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i].Text))
                continue;

            if (result.Length > 0 && lastSlotIndex >= 0)
                result += separators[Math.Min(lastSlotIndex, separators.Length - 1)];

            result += parts[i].PreserveTrailingWhitespace
                ? parts[i].Text.TrimStart()
                : parts[i].Text.Trim();
            lastSlotIndex = i;
        }

        return result.TrimStart();
    }

    private ChapterCustomTitlePart RenderSlot(
        string slot,
        TrackInfo track,
        ChapterSettings settings,
        int chapterPosition,
        int activeChapterCount,
        string? bookTitle,
        string? album)
    {
        return ChapterSettingsService.NormalizeSlot(slot) switch
        {
            "Number" => new ChapterCustomTitlePart(
                _renderer.FormatNumber(
                    chapterPosition,
                    activeChapterCount,
                    ChapterSettingsService.GetNumberingStyle(settings, legacyUseLeadingZeros: true)),
                PreserveTrailingWhitespace: true),
            "BookTitle" => TextPart(bookTitle?.Trim() ?? string.Empty),
            "Album" => TextPart(album?.Trim() ?? string.Empty),
            "OriginalTagTitle" => TextPart(track.TagTitle ?? string.Empty),
            "CleanTagTitle" => TextPart(_cleanupService.CleanTagTitle(track.TagTitle,
                track.SourceTrackNumber ?? track.SourceMetadataFields?.SourceTrackNumber)),
            "CleanFileName" => TextPart(_cleanupService.CleanFileName(track.FileName,
                track.SourceTrackNumber ?? track.SourceMetadataFields?.SourceTrackNumber)),
            "OriginalFileName" => TextPart(string.IsNullOrWhiteSpace(track.FileName)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(track.FileName)),
            _ => TextPart(string.Empty)
        };
    }

    private static ChapterCustomTitlePart TextPart(string text) =>
        new(text, PreserveTrailingWhitespace: false);

    public static string ResolveSeparator(string separator)
    {
        return ChapterSettingsService.NormalizeSeparator(separator) switch
        {
            "Dash" => " - ",
            "EnDash" => " – ",
            "Dot" => " · ",
            "Colon" => ": ",
            "Pipe" => " | ",
            "None" => string.Empty,
            _ => " "
        };
    }
}

internal sealed record ChapterCustomTitlePart(string Text, bool PreserveTrailingWhitespace);
