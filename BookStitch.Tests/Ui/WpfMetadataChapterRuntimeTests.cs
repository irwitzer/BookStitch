using BookStitch;
using BookStitch.Dialog;
using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Presentation;
using BookStitch.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using WpfResourceDictionary = System.Windows.ResourceDictionary;
using WpfShutdownMode = System.Windows.ShutdownMode;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Chapter_settings_editor_updates_shared_state_and_preserves_hidden_custom_values()
    {
        RunOnStaThread(() =>
        {
            var settings = CreateChapterEditorSettings();
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences(), appSettings: settings);

            try
            {
                var mode = Assert.IsType<ComboBox>(dialog.FindName("ConceptChapterModeComboBox"));
                var numbering = Assert.IsType<ComboBox>(dialog.FindName("ConceptChapterNumberingStyleComboBox"));
                var slot1 = Assert.IsType<ComboBox>(dialog.FindName("ConceptChapterSlot1ComboBox"));
                var separator1 = Assert.IsType<ComboBox>(dialog.FindName("ConceptChapterSeparator1ComboBox"));
                var numberingPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptChapterNumberingStylePanel"));
                var customPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptChapterCustomRulePanel"));
                var editorScrollViewer = Assert.IsType<ScrollViewer>(dialog.FindName("ConceptChapterSettingsScrollViewer"));

                Assert.Equal(
                    ChapterSettingsEditorPresentation.Modes.Select(option => option.Value),
                    mode.Items.OfType<ComboBoxItem>().Select(SelectedValue));
                Assert.Equal(
                    ChapterSettingsEditorPresentation.NumberingStyles.Select(option => option.Value),
                    numbering.Items.OfType<ComboBoxItem>().Select(SelectedValue));
                Assert.Equal(System.Windows.Visibility.Visible, numberingPanel.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, customPanel.Visibility);
                Assert.Equal(ScrollBarVisibility.Auto, editorScrollViewer.VerticalScrollBarVisibility);
                Assert.Null(dialog.FindName("ConceptChapterPreviewDataSourceComboBox"));
                Assert.Null(dialog.FindName("ConceptChapterPreviewTextBlock"));

                SelectValue(mode, ChapterSettingsService.ModeCustom);
                SelectValue(numbering, ChapterNumberingStyle.ZeroPadded.ToString());
                SelectValue(slot1, "CleanFileName");
                SelectValue(separator1, "Pipe");
                DrainDispatcher();

                Assert.Equal(ChapterSettingsService.ModeCustom, settings.ChapterSettings!.Mode);
                Assert.Equal(ChapterSettingsService.PreviewDataSourceCurrentProject, settings.ChapterSettings.PreviewDataSource);
                Assert.Equal(ChapterNumberingStyle.ZeroPadded.ToString(), settings.ChapterSettings.NumberingStyle);
                Assert.Equal("CleanFileName", settings.ChapterSettings.Slot1);
                Assert.Equal("Pipe", settings.ChapterSettings.Separator1);
                Assert.Equal(System.Windows.Visibility.Visible, customPanel.Visibility);

                SelectValue(mode, ChapterSettingsService.ModeOriginal);
                DrainDispatcher();
                Assert.Equal(System.Windows.Visibility.Collapsed, numberingPanel.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, customPanel.Visibility);
                Assert.Equal("CleanFileName", settings.ChapterSettings.Slot1);
                Assert.Equal("Pipe", settings.ChapterSettings.Separator1);

                SelectValue(mode, ChapterSettingsService.ModeCustom);
                DrainDispatcher();
                Assert.Equal("CleanFileName", SelectedValue(slot1));
                Assert.Equal("Pipe", SelectedValue(separator1));
            }
            finally
            {
                CloseWithoutDraftWarning(dialog);
            }
        });
    }

    [Fact]
    public void Chapter_numbering_style_updates_number_slots_with_an_accessible_reason()
    {
        RunOnStaThread(() =>
        {
            var settings = CreateChapterEditorSettings();
            var dialog = CreateMetadataDialog(new ProjectMetadataState(), new MetadataPreferences(), appSettings: settings);

            try
            {
                var numbering = Assert.IsType<ComboBox>(dialog.FindName("ConceptChapterNumberingStyleComboBox"));
                var slot1 = Assert.IsType<ComboBox>(dialog.FindName("ConceptChapterSlot1ComboBox"));

                SelectValue(numbering, ChapterNumberingStyle.None.ToString());
                DrainDispatcher();

                Assert.Equal("Empty", settings.ChapterSettings!.Slot1);
                Assert.Equal("Empty", SelectedValue(slot1));
                var expectedReason = ChapterSettingsEditorPresentation.GetSlotOptionUnavailableHelpText(
                    "Number",
                    ChapterNumberingStyle.None.ToString());
                Assert.All(
                    new[] { "ConceptChapterSlot1ComboBox", "ConceptChapterSlot2ComboBox", "ConceptChapterSlot3ComboBox" }
                        .Select(name => Assert.IsType<ComboBox>(dialog.FindName(name))),
                    comboBox =>
                    {
                        var numberItem = comboBox.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "Number"));
                        Assert.False(numberItem.IsEnabled);
                        Assert.Equal(expectedReason, numberItem.ToolTip);
                        Assert.Equal(expectedReason, System.Windows.Automation.AutomationProperties.GetHelpText(numberItem));
                    });

                SelectValue(numbering, ChapterNumberingStyle.Plain.ToString());
                DrainDispatcher();
                Assert.True(slot1.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "Number")).IsEnabled);
            }
            finally
            {
                CloseWithoutDraftWarning(dialog);
            }
        });
    }

    [Fact]
    public void Chapter_settings_refresh_projects_external_changes_without_change_feedback()
    {
        RunOnStaThread(() =>
        {
            var settings = CreateChapterEditorSettings();
            var changeCount = 0;
            var dialog = CreateMetadataDialog(
                new ProjectMetadataState(),
                new MetadataPreferences(),
                appSettings: settings,
                chapterSettingsChanged: () => changeCount++);

            try
            {
                var mode = Assert.IsType<ComboBox>(dialog.FindName("ConceptChapterModeComboBox"));
                var numbering = Assert.IsType<ComboBox>(dialog.FindName("ConceptChapterNumberingStyleComboBox"));
                var slot1 = Assert.IsType<ComboBox>(dialog.FindName("ConceptChapterSlot1ComboBox"));
                var numberingPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptChapterNumberingStylePanel"));
                var customPanel = Assert.IsType<StackPanel>(dialog.FindName("ConceptChapterCustomRulePanel"));
                var editorService = new ChapterSettingsEditorService();

                editorService.Apply(
                    settings,
                    editorService.Read(settings) with
                    {
                        Mode = ChapterSettingsService.ModeNone,
                        NumberingStyle = ChapterNumberingStyle.SpacePadded.ToString(),
                        Slot1 = "Album"
                    });
                dialog.RefreshChapterSettingsEditorFromSharedState();
                DrainDispatcher();

                Assert.Equal(ChapterSettingsService.ModeNone, SelectedValue(mode));
                Assert.Equal(ChapterNumberingStyle.SpacePadded.ToString(), SelectedValue(numbering));
                Assert.Equal("Album", SelectedValue(slot1));
                Assert.Equal(System.Windows.Visibility.Collapsed, numberingPanel.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, customPanel.Visibility);
                Assert.Equal(0, changeCount);
            }
            finally
            {
                CloseWithoutDraftWarning(dialog);
            }
        });
    }

    private static AppSettings CreateChapterEditorSettings() =>
        new()
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

    private static string SelectedValue(ComboBox comboBox) =>
        Assert.IsType<ComboBoxItem>(comboBox.SelectedItem).Tag?.ToString() ?? string.Empty;

    private static string SelectedValue(ComboBoxItem item) =>
        item.Tag?.ToString() ?? string.Empty;

    private static void SelectValue(ComboBox comboBox, string value)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .Single(item => string.Equals(item.Tag?.ToString(), value, StringComparison.Ordinal));
    }

}
