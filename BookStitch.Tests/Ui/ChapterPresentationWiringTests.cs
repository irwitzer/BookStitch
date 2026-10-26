using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class ChapterPresentationWiringTests
{
    private static readonly string MainWindowSource =
        RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Fact]
    public void Immediate_settings_change_cancels_debounce_before_rebuilding_and_persisting()
    {
        var apply = SourceContract.ExtractMember(
            MainWindowSource,
            "private void ApplyChapterSettingsImmediately()");

        SourceContract.AssertInOrder(
            apply,
            "_chapterMetadataRefreshTimer.Stop();",
            "UpdateIndexes();",
            "PersistTrackListState();");
    }

    [Fact]
    public void Metadata_changes_queue_refresh_only_for_an_active_metadata_dependent_timeline()
    {
        var queue = SourceContract.ExtractMember(
            MainWindowSource,
            "private void QueueChapterMetadataRefreshIfNeeded()");

        Assert.Contains("_isWorkspaceTransitioning || _isLoadingSettings", queue, StringComparison.Ordinal);
        Assert.Contains("Tracks.Count == 0", queue, StringComparison.Ordinal);
        Assert.Contains("!ChapterSettingsUseProjectMetadata()", queue, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            queue,
            "_chapterMetadataRefreshTimer.Stop();",
            "_chapterMetadataRefreshTimer.Start();");
    }

    [Fact]
    public void Debounce_tick_delegates_to_the_guarded_refresh_flush()
    {
        var tick = SourceContract.ExtractMember(
            MainWindowSource,
            "private void ChapterMetadataRefreshTimer_Tick");
        Assert.Contains("FlushPendingChapterMetadataRefresh();", tick, StringComparison.Ordinal);

        var flush = SourceContract.ExtractMember(
            MainWindowSource,
            "private void FlushPendingChapterMetadataRefresh()");
        Assert.Contains("if (!_chapterMetadataRefreshTimer.IsEnabled)", flush, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            flush,
            "_chapterMetadataRefreshTimer.Stop();",
            "UpdateIndexes();",
            "PersistTrackListState();");
    }

    [Fact]
    public void Metadata_dialog_uses_the_central_timeline_refresh_callback()
    {
        var show = SourceContract.ExtractMember(
            MainWindowSource,
            "private void ShowMetadataDialog(Window owner, bool allowDuringProjectSetup = false)");

        Assert.Contains(
            "refreshChapterTimelinePresentation: RefreshChapterTimelinePresentation",
            show,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Track_presentation_refresh_updates_main_and_open_metadata_views()
    {
        var refresh = SourceContract.ExtractMember(
            MainWindowSource,
            "private void RefreshTrackPresentationViews()");

        Assert.Contains("TracksGrid?.Items.Refresh();", refresh, StringComparison.Ordinal);
        Assert.Contains("_developmentMetadataDialog?.RefreshChapterPresentationView();", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void Index_rebuild_publishes_the_timeline_before_refreshing_track_views()
    {
        var update = SourceContract.ExtractMember(MainWindowSource, "private void UpdateIndexes()");

        SourceContract.AssertInOrder(
            update,
            "ChapterModeEngineService.Apply(",
            "RefreshChapterTimelinePresentation();",
            "RefreshTrackPresentationViews();");
    }

    [Fact]
    public void Confirmed_chapter_rule_change_refreshes_final_presentation_with_forced_autofit()
    {
        var dialogSource = RepositorySource.Read(["BookStitch", "Dialog", "MetadataDialog.xaml.cs"]);
        var changed = SourceContract.ExtractMember(dialogSource, "private void ConceptChapterSettingsComboBox_Changed");
        SourceContract.AssertInOrder(
            changed,
            "_chapterSettingsEditorService.Apply(_appSettings, updated);",
            "RefreshChapterSettingsEditorFromSharedState();",
            "_chapterSettingsChanged();",
            "RefreshChapterPresentationView(forceAutoFit: true);");

        var refresh = SourceContract.ExtractMember(dialogSource, "public void RefreshChapterPresentationView(bool forceAutoFit = false)");
        Assert.Contains("changed || forceAutoFit || _chapterGridColumnLayoutState.AutoFitPending", refresh, StringComparison.Ordinal);
        Assert.Contains("ScheduleChapterAutoFit(forceAutoFit || _chapterGridColumnLayoutState.AutoFitPending);", refresh, StringComparison.Ordinal);

        var schedule = SourceContract.ExtractMember(dialogSource, "private void ScheduleChapterAutoFit(bool force)");
        Assert.Contains("_chapterAutoFitForcePending |= force;", schedule, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_quick_chapter_editor_uses_the_shared_editor_service_and_presentation_contract()
    {
        var xaml = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
        var details = SingleElement(xaml, "Border", "x:Name", "MetadataDetailsBorder");
        var content = SingleElement(xaml, "StackPanel", "x:Name", "MetadataDetailsContent");
        var outputRow = content.Elements().First();
        Assert.Equal("MainOutputSettingsControl", outputRow.Name.LocalName);
        Assert.Equal("{Binding OutputFolder}", Attribute(outputRow, "OutputFolder"));
        Assert.Contains(details.Descendants(), element => Attribute(element, "x:Name") == "MainChapterModeComboBox");
        Assert.Contains(details.Descendants(), element => Attribute(element, "x:Name") == "MainChapterNumberingStyleComboBox");
        Assert.Contains(details.Descendants(), element => Attribute(element, "x:Name") == "MainChapterCustomRulePanel");
        Assert.DoesNotContain(details.Descendants(), element => Attribute(element, "x:Name").Contains("Preview", StringComparison.Ordinal));

        var save = SourceContract.ExtractMember(MainWindowSource, "private void MainChapterSettingsComboBox_Changed");
        Assert.Contains("_chapterSettingsEditorService.Read(_settings)", save, StringComparison.Ordinal);
        Assert.Contains("_chapterSettingsEditorService.Apply(", save, StringComparison.Ordinal);
        Assert.Contains("current.PreviewDataSource", save, StringComparison.Ordinal);
        Assert.Contains("ApplySharedChapterSettingsEditorChange();", save, StringComparison.Ordinal);

        var visibility = SourceContract.ExtractMember(MainWindowSource, "private void UpdateMainChapterSettingsEditorVisibility");
        Assert.Contains("ChapterSettingsEditorPresentation.ShowsNumberingStyle(mode)", visibility, StringComparison.Ordinal);
        Assert.Contains("ChapterSettingsEditorPresentation.ShowsCustomRule(mode)", visibility, StringComparison.Ordinal);

        var refresh = SourceContract.ExtractMember(MainWindowSource, "private void ApplyPersistedChapterSettingsEditorChange");
        SourceContract.AssertInOrder(
            refresh,
            "ApplyChapterSettingsImmediately();",
            "RefreshMainChapterSettingsEditorFromSharedState();",
            "_developmentMetadataDialog?.RefreshChapterSettingsEditorFromSharedState();");
    }

    [Fact]
    public void Chapter_proposal_template_and_measurement_share_layout_metrics()
    {
        var metadata = RepositorySource.LoadXaml(["BookStitch", "Dialog", "MetadataDialog.xaml"]);
        var source = RepositorySource.Read(["BookStitch", "Dialog", "MetadataDialog.xaml.cs"]);
        var proposalColumn = SingleElement(metadata, "DataGridTemplateColumn", "x:Name", "ConceptChapterProposalColumn");
        var serialized = proposalColumn.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("ChapterGridLayoutMetrics.ProposalDisclosureGridLength", serialized, StringComparison.Ordinal);
        Assert.Contains("ChapterGridLayoutMetrics.ProposalTextMargin", serialized, StringComparison.Ordinal);
        Assert.Contains("ChapterGridLayoutMetrics.EmbeddedProposalTextMargin", serialized, StringComparison.Ordinal);

        var autoFit = SourceContract.ExtractMember(source, "private void ApplyChapterAutoFit(bool force)");
        Assert.Contains("ChapterGridLayoutMetrics.CalculateProposalColumnWidth", autoFit, StringComparison.Ordinal);
        Assert.Contains("ChapterGridLayoutMetrics.CalculateTextColumnWidth", autoFit, StringComparison.Ordinal);
        Assert.DoesNotContain("+ 16", autoFit, StringComparison.Ordinal);
    }

    [Fact]
    public void Chapter_disclosure_controls_use_the_shared_chapter_style()
    {
        var chapterTheme = RepositorySource.LoadXaml(["BookStitch", "Themes", "ChapterSymbols.xaml"]);
        var style = SingleElement(chapterTheme, "Style", "x:Key", "ChapterDisclosureToggleStyle");
        Assert.Equal("ToggleButton", Attribute(style, "TargetType"));

        foreach (var relativePath in new[]
                 {
                     "BookStitch/MainWindow.xaml",
                     "BookStitch/Dialog/MetadataDialog.xaml"
                 })
        {
            var surface = RepositorySource.LoadXaml(relativePath.Split('/'));
            var disclosureControls = surface.Descendants()
                .Where(element =>
                    IsElement(element, "ToggleButton") &&
                    Attribute(element, "IsChecked").Contains("IsChapterDetailsExpanded", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(disclosureControls);
            Assert.All(
                disclosureControls,
                control => Assert.Equal(
                    "{StaticResource ChapterDisclosureToggleStyle}",
                    Attribute(control, "Style")));
        }
    }

    private static XElement SingleElement(
        XContainer container,
        string elementName,
        string attributeName,
        string attributeValue) =>
        container.Descendants().Single(element =>
            IsElement(element, elementName) && Attribute(element, attributeName) == attributeValue);

    private static bool IsElement(XElement element, string name) => element.Name.LocalName == name;

    private static string Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name.Split(':')[^1])?.Value ?? "";
}
