using BookStitch.Models;
using BookStitch.Services;

namespace BookStitch.Presentation;

public sealed record ChapterSettingsOptionDefinition(string Value, string ResourceKey);

public static class ChapterSettingsEditorPresentation
{
    public static IReadOnlyList<ChapterSettingsOptionDefinition> Modes { get; } =
    [
        new(ChapterSettingsService.ModeAuto, "Automatic"),
        new(ChapterSettingsService.ModeCustom, "Custom"),
        new(ChapterSettingsService.ModeOriginal, "Original"),
        new(ChapterSettingsService.ModeNone, "NoChapters"),
        new(ChapterSettingsService.ModeChapterMaster, "ChapterMaster")
    ];

    public static IReadOnlyList<ChapterSettingsOptionDefinition> NumberingStyles { get; } =
    [
        new(ChapterNumberingStyle.Plain.ToString(), "Plain"),
        new(ChapterNumberingStyle.ZeroPadded.ToString(), "ZeroPadded"),
        new(ChapterNumberingStyle.SpacePadded.ToString(), "SpacePadded"),
        new(ChapterNumberingStyle.None.ToString(), "NoNumber")
    ];

    public static IReadOnlyList<ChapterSettingsOptionDefinition> Slots { get; } =
    [
        new("Empty", "Empty"),
        new("Number", "Number"),
        new("BookTitle", "BookTitle"),
        new("Album", "Album"),
        new("OriginalTagTitle", "OriginalTagTitle"),
        new("CleanTagTitle", "CleanTagTitle"),
        new("CleanFileName", "CleanFileName"),
        new("OriginalFileName", "OriginalFileName")
    ];

    public static IReadOnlyList<ChapterSettingsOptionDefinition> Separators { get; } =
    [
        new("Space", "SeparatorSpace"),
        new("Dash", "SeparatorDash"),
        new("EnDash", "SeparatorEnDash"),
        new("Dot", "SeparatorDot"),
        new("Colon", "SeparatorColon"),
        new("Pipe", "SeparatorPipe"),
        new("None", "SeparatorNone")
    ];

    public static bool ShowsNumberingStyle(string mode) =>
        string.Equals(mode, ChapterSettingsService.ModeAuto, StringComparison.Ordinal) ||
        string.Equals(mode, ChapterSettingsService.ModeCustom, StringComparison.Ordinal) ||
        string.Equals(mode, ChapterSettingsService.ModeChapterMaster, StringComparison.Ordinal);

    public static bool ShowsCustomRule(string mode) =>
        string.Equals(mode, ChapterSettingsService.ModeCustom, StringComparison.Ordinal);

    public static bool IsSlotOptionEnabled(string slotValue, string numberingStyle) =>
        !string.Equals(numberingStyle, ChapterNumberingStyle.None.ToString(), StringComparison.Ordinal) ||
        !string.Equals(slotValue, "Number", StringComparison.Ordinal);

    public static string? GetSlotOptionUnavailableHelpText(string slotValue, string numberingStyle) =>
        IsSlotOptionEnabled(slotValue, numberingStyle)
            ? null
            : GetText("NumberUnavailableWithNoNumbering");

    public static string GetText(string resourceKey) =>
        LocalizationText.Get(resourceKey);
}
