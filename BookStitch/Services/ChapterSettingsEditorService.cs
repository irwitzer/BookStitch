using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ChapterSettingsEditorState(
    string Mode,
    string NumberingStyle,
    string PreviewDataSource,
    string Slot1,
    string Separator1,
    string Slot2,
    string Separator2,
    string Slot3);

/// <summary>
/// Applies either chapter settings editor to the one shared application state.
/// Persistence and the productive refresh remain owned by the hosting window.
/// </summary>
public sealed class ChapterSettingsEditorService
{
    public ChapterSettingsEditorState Read(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var chapterSettings = ChapterSettingsService.Normalize(
            settings.ChapterSettings,
            legacyUseLeadingZeros: true);
        settings.ChapterSettings = chapterSettings;
        return new ChapterSettingsEditorState(
            chapterSettings.Mode,
            chapterSettings.NumberingStyle,
            chapterSettings.PreviewDataSource,
            chapterSettings.Slot1,
            chapterSettings.Separator1,
            chapterSettings.Slot2,
            chapterSettings.Separator2,
            chapterSettings.Slot3);
    }

    public void Apply(AppSettings settings, ChapterSettingsEditorState state)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(state);
        var chapterSettings = ChapterSettingsService.Normalize(
            settings.ChapterSettings,
            legacyUseLeadingZeros: true);
        chapterSettings.Mode = state.Mode;
        chapterSettings.NumberingStyle = state.NumberingStyle;
        chapterSettings.PreviewDataSource = state.PreviewDataSource;
        chapterSettings.Slot1 = state.Slot1;
        chapterSettings.Separator1 = state.Separator1;
        chapterSettings.Slot2 = state.Slot2;
        chapterSettings.Separator2 = state.Separator2;
        chapterSettings.Slot3 = state.Slot3;
        settings.ChapterSettings = ChapterSettingsService.Normalize(
            chapterSettings,
            legacyUseLeadingZeros: true);
    }
}
