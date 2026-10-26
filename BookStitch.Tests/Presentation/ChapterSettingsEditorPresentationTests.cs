using BookStitch.Models;
using BookStitch.Presentation;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Presentation;

public sealed class ChapterSettingsEditorPresentationTests
{
    [Fact]
    public void Shared_option_catalog_and_visibility_rules_are_stable_and_mode_semantic()
    {
        Assert.Equal(
            [
                ChapterSettingsService.ModeAuto,
                ChapterSettingsService.ModeCustom,
                ChapterSettingsService.ModeOriginal,
                ChapterSettingsService.ModeNone,
                ChapterSettingsService.ModeChapterMaster
            ],
            ChapterSettingsEditorPresentation.Modes.Select(option => option.Value));
        Assert.Equal(
            [
                ChapterNumberingStyle.Plain.ToString(),
                ChapterNumberingStyle.ZeroPadded.ToString(),
                ChapterNumberingStyle.SpacePadded.ToString(),
                ChapterNumberingStyle.None.ToString()
            ],
            ChapterSettingsEditorPresentation.NumberingStyles.Select(option => option.Value));
        Assert.Equal(
            ["Empty", "Number", "BookTitle", "Album", "OriginalTagTitle", "CleanTagTitle", "CleanFileName", "OriginalFileName"],
            ChapterSettingsEditorPresentation.Slots.Select(option => option.Value));
        Assert.Equal(
            ["Space", "Dash", "EnDash", "Dot", "Colon", "Pipe", "None"],
            ChapterSettingsEditorPresentation.Separators.Select(option => option.Value));

        Assert.True(ChapterSettingsEditorPresentation.ShowsNumberingStyle(ChapterSettingsService.ModeAuto));
        Assert.True(ChapterSettingsEditorPresentation.ShowsNumberingStyle(ChapterSettingsService.ModeCustom));
        Assert.True(ChapterSettingsEditorPresentation.ShowsNumberingStyle(ChapterSettingsService.ModeChapterMaster));
        Assert.False(ChapterSettingsEditorPresentation.ShowsNumberingStyle(ChapterSettingsService.ModeOriginal));
        Assert.False(ChapterSettingsEditorPresentation.ShowsNumberingStyle(ChapterSettingsService.ModeNone));
        Assert.True(ChapterSettingsEditorPresentation.ShowsCustomRule(ChapterSettingsService.ModeCustom));
        Assert.False(ChapterSettingsEditorPresentation.ShowsCustomRule(ChapterSettingsService.ModeAuto));
        Assert.False(ChapterSettingsEditorPresentation.ShowsCustomRule(ChapterSettingsService.ModeOriginal));

        Assert.Equal("Kapitel-Editor", ChapterSettingsEditorPresentation.GetText("ChapterSettingsHeading"));
        Assert.False(ChapterSettingsEditorPresentation.IsSlotOptionEnabled("Number", ChapterNumberingStyle.None.ToString()));
        Assert.True(ChapterSettingsEditorPresentation.IsSlotOptionEnabled("Number", ChapterNumberingStyle.Plain.ToString()));
        Assert.True(ChapterSettingsEditorPresentation.IsSlotOptionEnabled("BookTitle", ChapterNumberingStyle.None.ToString()));
        Assert.Equal(
            "Nicht verfügbar, weil der Nummerierungsstil „Keine Nummer“ gewählt ist.",
            ChapterSettingsEditorPresentation.GetSlotOptionUnavailableHelpText("Number", ChapterNumberingStyle.None.ToString()));
        Assert.Null(ChapterSettingsEditorPresentation.GetSlotOptionUnavailableHelpText("Number", ChapterNumberingStyle.Plain.ToString()));
    }
}
