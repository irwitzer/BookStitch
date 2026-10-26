using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterSettingsEditorServiceTests
{
    [Fact]
    public void ApplyAndRead_UseTheSameAppSettingsInstanceForBothEditors()
    {
        var settings = new AppSettings();
        var service = new ChapterSettingsEditorService();

        service.Apply(
            settings,
            new ChapterSettingsEditorState(
                ChapterSettingsService.ModeCustom,
                ChapterNumberingStyle.ZeroPadded.ToString(),
                ChapterSettingsService.PreviewDataSourceCurrentProject,
                "Number",
                "Dash",
                "BookTitle",
                "Colon",
                "CleanTagTitle"));
        var stateReadByOtherEditor = service.Read(settings);

        Assert.Equal(ChapterSettingsService.ModeCustom, stateReadByOtherEditor.Mode);
        Assert.Equal(ChapterNumberingStyle.ZeroPadded.ToString(), stateReadByOtherEditor.NumberingStyle);
        Assert.Equal("BookTitle", stateReadByOtherEditor.Slot2);
    }

    [Fact]
    public void Apply_NormalizesInvalidEditorValuesInTheSharedModel()
    {
        var settings = new AppSettings();

        new ChapterSettingsEditorService().Apply(
            settings,
            new ChapterSettingsEditorState(
                "invalid",
                "invalid",
                "invalid",
                "invalid",
                "invalid",
                "invalid",
                "invalid",
                "invalid"));

        var chapterSettings = Assert.IsType<ChapterSettings>(settings.ChapterSettings);
        Assert.Equal(ChapterSettingsService.ModeAuto, chapterSettings.Mode);
        Assert.Equal(ChapterNumberingStyle.ZeroPadded.ToString(), chapterSettings.NumberingStyle);
        Assert.Equal("Empty", chapterSettings.Slot1);
    }

    [Fact]
    public void Apply_NormalizesOriginalModeInTheSharedModel()
    {
        var settings = new AppSettings();

        new ChapterSettingsEditorService().Apply(
            settings,
            new ChapterSettingsEditorState(
                "original",
                ChapterNumberingStyle.Plain.ToString(),
                ChapterSettingsService.PreviewDataSourceSample,
                "Empty",
                "Space",
                "Empty",
                "Space",
                "Empty"));

        var chapterSettings = Assert.IsType<ChapterSettings>(settings.ChapterSettings);
        Assert.Equal(ChapterSettingsService.ModeOriginal, chapterSettings.Mode);
    }
    [Fact]
    public void Compact_editor_update_preserves_preview_data_source_from_shared_state()
    {
        var settings = new AppSettings
        {
            ChapterSettings = new ChapterSettings
            {
                Mode = ChapterSettingsService.ModeAuto,
                NumberingStyle = ChapterNumberingStyle.Plain.ToString(),
                PreviewDataSource = ChapterSettingsService.PreviewDataSourceCurrentProject,
                Slot1 = "Number",
                Separator1 = "Dash",
                Slot2 = "BookTitle",
                Separator2 = "Colon",
                Slot3 = "CleanTagTitle"
            }
        };
        var service = new ChapterSettingsEditorService();
        var current = service.Read(settings);

        service.Apply(
            settings,
            current with
            {
                Mode = ChapterSettingsService.ModeCustom,
                NumberingStyle = ChapterNumberingStyle.ZeroPadded.ToString(),
                Slot1 = "CleanFileName"
            });

        var updated = service.Read(settings);
        Assert.Equal(ChapterSettingsService.PreviewDataSourceCurrentProject, updated.PreviewDataSource);
        Assert.Equal(ChapterSettingsService.ModeCustom, updated.Mode);
        Assert.Equal("CleanFileName", updated.Slot1);
    }

}
