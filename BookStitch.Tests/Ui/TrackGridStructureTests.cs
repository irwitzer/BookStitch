using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class TrackGridStructureTests
{
    [Fact]
    public void Original_columns_are_distinct_and_precede_prepared_bitrate()
    {
        var document = LoadMainWindowXaml();
        var bitrate = Single(document, "DataGridTextColumn", "Name", "TrackGridOriginalBitrateColumn");
        var rate = Single(document, "DataGridTextColumn", "Name", "TrackGridOriginalSampleRateColumn");
        var channel = Single(document, "DataGridTextColumn", "Name", "TrackGridOriginalChannelLayoutColumn");
        var target = Single(document, "DataGridTextColumn", "Name", "TrackGridBitrateColumn");
        var targetChannel = Single(document, "DataGridTextColumn", "Name", "TrackGridChannelLayoutColumn");
        Assert.Equal("{Binding BitrateKbps}", Attr(bitrate, "Binding"));
        Assert.Equal("{Binding DisplayOriginalSampleRateHz}", Attr(rate, "Binding"));
        Assert.Equal("SampleRateHz", Attr(rate, "SortMemberPath"));
        Assert.Equal("Collapsed", Attr(target, "Visibility"));
        Assert.Equal("Collapsed", Attr(targetChannel, "Visibility"));
        Assert.Equal("{Binding DisplayOriginalChannelLayout}", Attr(channel, "Binding"));
        Assert.Equal("Channels", Attr(channel, "SortMemberPath"));
        var columns = bitrate.Parent!.Elements().ToList();
        Assert.Equal(columns.IndexOf(bitrate) + 1, columns.IndexOf(rate));
        Assert.Equal(columns.IndexOf(rate) + 1, columns.IndexOf(channel));
        Assert.Equal(columns.IndexOf(channel) + 1, columns.IndexOf(target));
        var code = LoadMainWindowCode();
        Assert.Contains("SetOriginalAudioColumnsVisible(false, force: true)", code);
        Assert.Contains("UiOriginalBitrate", code);
        Assert.Contains("UiOriginalSampleRate", code);
        Assert.Contains("SetOriginalAudioColumnsVisible(true)",
            SourceContract.ExtractMember(code, "private void ResetProjectSourceModeStateForNewSource()"));
        Assert.Contains("SetOriginalAudioColumnsVisible(false)",
            SourceContract.ExtractMember(code, "private void SetPipelineState(ProjectPipelineState state)"));
    }

    [Fact]
    public void Automatic_order_surface_displays_stored_order_and_delegates_restore_to_the_track_service()
    {
        var document = LoadMainWindowXaml();
        var column = Single(document, "DataGridTextColumn", "Name", "TrackGridAutoColumn");

        Assert.Equal("{Binding DisplayAutomaticOrderPosition}", Attr(column, "Binding"));
        Assert.Equal("AutomaticOrderPosition", Attr(column, "SortMemberPath"));

        var source = LoadMainWindowCode();
        var menu = SourceContract.ExtractMember(source, "private void BuildTrackGridRowContextMenu(ContextMenu menu)");
        Assert.Contains("RestoreAutomaticTrackOrder_Click", menu, StringComparison.Ordinal);
        Assert.Contains(
            "CanEditTrackOrder && Tracks.Any(track => track.AutomaticOrderPosition.HasValue)",
            menu,
            StringComparison.Ordinal);

        var restore = SourceContract.ExtractMember(
            source,
            "private void RestoreAutomaticTrackOrder_Click(object sender, RoutedEventArgs e)");
        Assert.Contains(
            "if (!CanEditTrackOrder || Tracks.All(track => !track.AutomaticOrderPosition.HasValue))",
            restore,
            StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            restore,
            "_services.Tracks.TrackListActionService.RestoreAutomaticOrder(Tracks)",
            "ReplaceTrackList(",
            "RestoreSelection(");
    }

    [Fact]
    public void Folder_column_displays_sorts_and_measures_the_public_folder_projection()
    {
        var document = LoadMainWindowXaml();
        var column = Single(document, "DataGridTemplateColumn", "SortMemberPath", "DisplayFolder");
        var text = column.Descendants().Single(element => element.Name.LocalName == "TextBlock");

        Assert.Equal("{Binding DisplayFolder}", Attr(text, "FolderPathInlineBehavior.Text"));

        var estimateWidth = SourceContract.ExtractMember(
            LoadMainWindowCode(),
            "private static double EstimateTrackColumnWidth(string key, string header, IReadOnlyList<TrackInfo> tracks)");
        Assert.Contains("\"DisplayFolder\" => track.DisplayFolder", estimateWidth, StringComparison.Ordinal);
    }

    [Fact]
    public void Blocking_file_warning_rows_map_normal_hover_and_selected_states_to_semantic_resources()
    {
        var document = LoadMainWindowXaml();
        var grid = Single(document, "DataGrid", "Name", "TracksGrid");
        var rowStyle = grid.Descendants().Single(element =>
            element.Name.LocalName == "Style" && Attr(element, "TargetType") == "DataGridRow");

        var blocking = rowStyle.Descendants().Single(element =>
            element.Name.LocalName == "DataTrigger" &&
            Attr(element, "Binding") == "{Binding HasBlockingFileWarning}");
        AssertSetter(blocking, "Background", "{DynamicResource BlockingWarningRowBackgroundBrush}");
        AssertSetter(blocking, "Foreground", "{DynamicResource BlockingWarningRowForegroundBrush}");
        AssertSetter(blocking, "BorderBrush", "{DynamicResource BlockingWarningRowBorderBrush}");

        var hover = MultiTriggerFor(rowStyle, "IsMouseOver");
        AssertSetter(hover, "Background", "{DynamicResource BlockingWarningRowHoverBackgroundBrush}");

        var selected = MultiTriggerFor(rowStyle, "IsSelected");
        AssertSetter(selected, "Background", "{DynamicResource BlockingWarningRowSelectedBackgroundBrush}");
        AssertSetter(selected, "Foreground", "{DynamicResource BlockingWarningRowSelectedForegroundBrush}");
        AssertSetter(selected, "BorderBrush", "{DynamicResource BlockingWarningRowSelectedBorderBrush}");
    }

    [Fact]
    public void Track_grid_uses_read_only_recycling_virtualization()
    {
        var document = LoadMainWindowXaml();
        var grid = Single(document, "DataGrid", "Name", "TracksGrid");

        Assert.Equal("{Binding Tracks}", Attr(grid, "ItemsSource"));
        Assert.Equal("True", Attr(grid, "IsReadOnly"));
        Assert.Equal("True", Attr(grid, "EnableRowVirtualization"));
        Assert.Equal("True", Attr(grid, "ScrollViewer.CanContentScroll"));
        Assert.Equal("True", Attr(grid, "VirtualizingPanel.IsVirtualizing"));
        Assert.Equal("Recycling", Attr(grid, "VirtualizingPanel.VirtualizationMode"));
    }

    [Fact]
    public void Embedded_chapter_rows_expose_accessible_mouse_and_keyboard_selection()
    {
        var document = LoadMainWindowXaml();
        var chapterRows = Single(document, "ItemsControl", "ItemsSource", "{Binding EmbeddedChapterRows}");
        Assert.False(string.IsNullOrWhiteSpace(Attr(chapterRows, "AutomationProperties.Name")));

        var row = Single(
            chapterRows,
            "Grid",
            "PreviewMouseLeftButtonDown",
            "EmbeddedChapterRow_PreviewMouseLeftButtonDown");
        Assert.Equal("True", Attr(row, "Focusable"));
        Assert.Equal("EmbeddedChapterRow_MouseLeftButtonUp", Attr(row, "MouseLeftButtonUp"));
        Assert.Equal("EmbeddedChapterRow_PreviewKeyDown", Attr(row, "PreviewKeyDown"));
    }

    [Fact]
    public void Embedded_chapter_rows_project_into_the_matching_parent_columns_without_a_nested_data_grid()
    {
        var document = LoadMainWindowXaml();
        var chapterRows = Single(document, "ItemsControl", "ItemsSource", "{Binding EmbeddedChapterRows}");
        var row = Single(
            chapterRows,
            "Grid",
            "PreviewMouseLeftButtonDown",
            "EmbeddedChapterRow_PreviewMouseLeftButtonDown");
        var parentColumns = new XElement[]
        {
            Single(document, "DataGridTextColumn", "Name", "TrackGridFileColumn"),
            Single(document, "DataGridTextColumn", "Name", "TrackGridStartColumn"),
            Single(document, "DataGridTemplateColumn", "Name", "TrackGridChapterProposalColumn"),
            Single(document, "DataGridTextColumn", "Name", "TrackGridDurationColumn")
        };

        var childColumns = row.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Where(element => !string.IsNullOrEmpty(Attr(element, "TrackGridChildColumn.Key")))
            .ToArray();
        Assert.Equal(
            parentColumns.Select(element => Attr(element, "TrackGridChildColumn.Key")),
            childColumns.Select(element => Attr(element, "TrackGridChildColumn.Key")));
        Assert.Equal(
            new[] { "{Binding Title}", "{Binding TimelineStart}", "{Binding ChapterProposal}", "{Binding Duration}" },
            childColumns.Select(element => Attr(element, "Text")));
        Assert.All(childColumns, element =>
            Assert.Equal("{StaticResource AccentSelectionTertiaryTextStyle}", Attr(element, "Style")));

        Assert.Equal("False", Attr(parentColumns[1], "CanUserSort"));
        Assert.Equal("{Binding DisplayChapterStart}", Attr(parentColumns[1], "Binding"));
        Assert.Equal("False", Attr(parentColumns[2], "CanUserSort"));
        Assert.Contains(
            parentColumns[2].Descendants(),
            element => element.Name.LocalName == "TextBlock" &&
                       Attr(element, "Text") == "{Binding DisplayChapterProposal}");

        Assert.Contains(row.Descendants(), element => Attr(element, "TrackGridChildColumn.SpansAllColumns") == "True");
        Assert.Equal("EmbeddedChapterRow_Loaded", Attr(row, "Loaded"));
        Assert.Equal("EmbeddedChapterRow_Unloaded", Attr(row, "Unloaded"));
        Assert.DoesNotContain(chapterRows.Descendants(), element => element.Name.LocalName == "DataGrid");
        Assert.DoesNotContain(
            chapterRows.Descendants(),
            element => !string.IsNullOrEmpty(Attr(element, "ScrollViewer.VerticalScrollBarVisibility")));
    }

    [Fact]
    public void Track_grid_has_no_progress_column_and_keeps_warning_and_disclosure_bindings()
    {
        var document = LoadMainWindowXaml();
        Assert.DoesNotContain(document.Descendants(), element =>
            Attr(element, "Name") == "TrackGridProgressColumn" ||
            element.Name.LocalName == "TrackProgressRing");
        Assert.DoesNotContain(document.Descendants().Attributes(), attribute =>
            attribute.Value.Contains("UiOutputMB", StringComparison.Ordinal) ||
            attribute.Value.Contains("DisplayOutputSizeMb", StringComparison.Ordinal) ||
            attribute.Value.Contains("ConvertedSizeMb", StringComparison.Ordinal));

        var indicator = Single(document, "DataGridTemplateColumn", "Name", "TrackGridIndicatorColumn");
        var symbols = indicator.Descendants().Single(element =>
            element.Name.LocalName == "Grid" && Attr(element, "Width") == "54");

        Assert.Equal(
            ["18", "18", "18"],
            symbols.Descendants()
                .Where(element => element.Name.LocalName == "ColumnDefinition")
                .Select(element => Attr(element, "Width")));

        Assert.Contains(symbols.Elements(), element =>
            element.Name.LocalName == "Viewbox" &&
            Attr(element, "Grid.Column") == "0" &&
            Attr(element, "Visibility").Contains("HasFileWarning", StringComparison.Ordinal));
        Assert.Contains(symbols.Elements(), element =>
            element.Name.LocalName == "Viewbox" &&
            Attr(element, "Grid.Column") == "1" &&
            Attr(element, "Visibility").Contains("HasChapterWarning", StringComparison.Ordinal));
        Assert.Contains(symbols.Elements(), element =>
            element.Name.LocalName == "ToggleButton" &&
            Attr(element, "Grid.Column") == "2" &&
            Attr(element, "AutomationProperties.Name").Contains("DisplayChapterDetailsAutomationName", StringComparison.Ordinal));

        var legacyBindings = new[]
        {
            "ProcessingStatusAutomationName",
            "IsProcessingStatusOpen",
            "IsProcessingStatusActive",
            "IsProcessingStatusCompleted",
            "IsProcessingStatusFailed",
            "TrackProgressFraction",
            "TrackProgressVisualState",
            "TrackProgressAutomationName"
        };
        var attributeValues = document.Descendants().Attributes().Select(attribute => attribute.Value).ToArray();
        Assert.All(legacyBindings, binding =>
            Assert.DoesNotContain(attributeValues, value => value.Contains(binding, StringComparison.Ordinal)));

        var resourceKeys = document.Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName == "Key")
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("TrackStatusCircleGeometry", resourceKeys);
        Assert.DoesNotContain("TrackStatusPartialCircleGeometry", resourceKeys);
        Assert.DoesNotContain("TrackStatusCheckGeometry", resourceKeys);
        Assert.DoesNotContain("TrackStatusXGeometry", resourceKeys);
    }

    [Fact]
    public void Chapter_details_can_be_disclosed_from_indicator_and_proposal_columns()
    {
        var document = LoadMainWindowXaml();
        var indicator = Single(document, "DataGridTemplateColumn", "Name", "TrackGridIndicatorColumn");
        var proposal = Single(document, "DataGridTemplateColumn", "Name", "TrackGridChapterProposalColumn");
        var toggles = new[]
        {
            Single(indicator, "ToggleButton", "Click", "ChapterDetailsToggle_Click"),
            Single(proposal, "ToggleButton", "Click", "ChapterDetailsToggle_Click")
        };

        Assert.All(toggles, toggle =>
        {
            Assert.Equal(
                "{Binding IsChapterDetailsExpanded, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
                Attr(toggle, "IsChecked"));
            Assert.Equal(
                "{Binding HasEmbeddedChapterDetails, Converter={StaticResource BooleanToVisibilityConverter}}",
                Attr(toggle, "Visibility"));
            Assert.False(string.IsNullOrWhiteSpace(Attr(toggle, "AutomationProperties.Name")));
        });
    }

    [Fact]
    public void Manual_column_resize_disables_autofit_and_persists_the_shared_child_layout()
    {
        var document = LoadMainWindowXaml();
        var grid = Single(document, "DataGrid", "Name", "TracksGrid");
        Assert.Equal("True", Attr(grid, "CanUserResizeColumns"));

        var resize = SourceContract.ExtractBetween(
            LoadMainWindowCode(),
            "private void TracksGridColumnHeader_DragCompleted",
            "private void TracksGrid_PreviewMouseRightButtonDown");
        SourceContract.AssertInOrder(
            resize,
            "FindVisualAncestor<DataGridColumnHeader>",
            "_settings.TrackGridAutoFitEnabled = false;",
            "SaveTrackGridColumnLayout();",
            "RefreshEmbeddedChapterRowColumnLayouts(force: true);");
    }

    [Fact]
    public void Configurable_row_height_is_bound_to_the_normalized_persisted_setting()
    {
        var document = LoadMainWindowXaml();
        var grid = Single(document, "DataGrid", "Name", "TracksGrid");
        Assert.Equal("{Binding TrackGridRowHeight}", Attr(grid, "RowHeight"));

        var code = LoadMainWindowCode();
        Assert.Contains(
            "TrackGridRowHeightService.GetRowHeight(_debugSettings.TrackGridRowHeightLevel)",
            code,
            StringComparison.Ordinal);

        var update = SourceContract.ExtractBetween(
            code,
            "private void SetTrackGridRowHeightLevel",
            "private DeveloperToolsWindow CreateDeveloperToolsWindow");
        SourceContract.AssertInOrder(
            update,
            "TrackGridRowHeightService.NormalizeLevel(level)",
            "_debugSettings.TrackGridRowHeightLevel = normalized;",
            "DebugSettingsService.Save(_debugSettings);",
            "OnPropertyChanged(nameof(TrackGridRowHeight));");
    }

    [Fact]
    public void Closing_settings_reapplies_track_autofit_after_settings_and_appearance_are_committed()
    {
        var showSettings = SourceContract.ExtractBetween(
            LoadMainWindowCode(),
            "private void ShowSettingsDialog",
            "private IReadOnlyDictionary<Guid, string>? CreateCurrentResolvedAutoContentsBySourceId");

        SourceContract.AssertInOrder(
            showSettings,
            "ModalDialogService.ShowDialog(settingsWindow, owner);",
            "ApplyAppearanceResources(_settings);",
            "AutoFitTrackColumnsAfterRender();");
    }

    [Fact]
    public void Closing_settings_stops_post_dialog_refresh_when_restart_or_shutdown_is_pending()
    {
        var code = LoadMainWindowCode();
        var showSettings = SourceContract.ExtractBetween(
            code,
            "private void ShowSettingsDialog",
            "private bool IsApplicationRestartOrShutdownPending");

        SourceContract.AssertInOrder(
            showSettings,
            "ModalDialogService.ShowDialog(settingsWindow, owner);",
            "if (IsApplicationRestartOrShutdownPending())",
            "return;",
            "ApplyAppearanceResources(_settings);");

        var shutdownGuard = SourceContract.ExtractBetween(
            code,
            "private bool IsApplicationRestartOrShutdownPending",
            "private IReadOnlyDictionary<Guid, string>? CreateCurrentResolvedAutoContentsBySourceId");

        Assert.Contains("Dispatcher.HasShutdownStarted", shutdownGuard, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.HasShutdownFinished", shutdownGuard, StringComparison.Ordinal);
        Assert.Contains("app.ApplicationRestartService.IsRestartRequested", shutdownGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void Embedded_chapter_arrow_navigation_uses_one_visible_parent_child_sequence()
    {
        var code = LoadMainWindowCode();
        var navigation = SourceContract.ExtractBetween(
            code,
            "private bool HandleActiveVisibleTrackGridRowKey",
            "private void SelectParentTrack");

        SourceContract.AssertInOrder(
            navigation,
            "var rows = BuildVisibleTrackGridRows();",
            "var currentIndex = GetActiveVisibleTrackGridRowIndex(rows);",
            "var nextIndex = currentIndex + direction;",
            "SelectVisibleTrackGridRow(rows[nextIndex], focusTarget: true);");
        Assert.Contains("if (!track.IsChapterDetailsExpanded)", navigation, StringComparison.Ordinal);
        Assert.Contains("rows.Add(VisibleTrackGridRow.ForChapter(track, chapter));", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void Middle_button_autoscroll_owns_mouse_capture_and_stops_when_capture_or_scrollability_is_lost()
    {
        var autoScroll = SourceContract.ExtractBetween(
            LoadMainWindowCode(),
            "private bool HandleTrackGridAutoScrollMouseDown",
            "private TrackInfo? JumpToNextTrackWarning");

        Assert.Contains("e.ChangedButton != MouseButton.Middle", autoScroll, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            autoScroll,
            "TracksGrid.CaptureMouse()",
            "_trackGridAutoScrollTimer.Start();",
            "private void StopTrackGridAutoScroll()",
            "_trackGridAutoScrollTimer.Stop();",
            "TracksGrid.ReleaseMouseCapture()");
        Assert.Contains("if (!CanStartTrackGridAutoScroll())", autoScroll, StringComparison.Ordinal);
        Assert.Contains("private void TracksGrid_LostMouseCapture", autoScroll, StringComparison.Ordinal);
        Assert.Contains("StopTrackGridAutoScroll();", autoScroll, StringComparison.Ordinal);
    }

    [Fact]
    public void Embedded_chapter_columns_follow_the_visible_parent_column_order_and_span_contract()
    {
        var layout = SourceContract.ExtractBetween(
            LoadMainWindowCode(),
            "private void RefreshEmbeddedChapterRowColumnLayouts",
            "private static double GetTrackGridColumnLayoutWidth");

        Assert.Contains("TracksGrid.Columns.OrderBy(column => column.DisplayIndex)", layout, StringComparison.Ordinal);
        Assert.Contains("var explicitKey = TrackGridChildColumn.GetKey(column)", layout, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(element, displayColumn)", layout, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(element, columns.Count)", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_layout_without_a_status_column_migrates_it_after_automatic_order()
    {
        var layout = SourceContract.ExtractBetween(
            LoadMainWindowCode(),
            "private void ApplyTrackGridColumnLayout",
            "private void SaveTrackGridColumnLayout");

        Assert.Contains("!saved.ContainsKey(\"StatusIndicator\")", layout, StringComparison.Ordinal);
        Assert.Contains("saved.TryGetValue(\"AutomaticOrderPosition\"", layout, StringComparison.Ordinal);
        Assert.Contains("return autoColumn.DisplayIndex + 0.5d;", layout, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            layout,
            "ResolveMigratedStatusIndicatorDisplayIndex(saved)",
            "ApplyPinnedTrackGridColumnOrder();",
            "SaveTrackGridColumnLayout();");
    }

    private static XDocument LoadMainWindowXaml() =>
        RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);

    private static string LoadMainWindowCode() =>
        RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    private static XElement Single(
        XContainer document,
        string elementName,
        string attributeName,
        string value) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == elementName && Attr(element, attributeName) == value);

    private static XElement MultiTriggerFor(XElement style, string stateProperty) =>
        style.Descendants().Single(element =>
            element.Name.LocalName == "MultiDataTrigger" &&
            element.Descendants().Any(condition =>
                condition.Name.LocalName == "Condition" &&
                Attr(condition, "Binding").Contains(stateProperty, StringComparison.Ordinal)));

    private static void AssertSetter(XElement trigger, string property, string value)
    {
        var setter = trigger.Elements().Single(element =>
            element.Name.LocalName == "Setter" && Attr(element, "Property") == property);
        Assert.Equal(value, Attr(setter, "Value"));
    }

    private static string Attr(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value ?? string.Empty;
}
