using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class MetadataDialogStructureTests
{
    [Fact]
    public void Concept_search_matcher_uses_metadata_editor_matching_rules()
    {
        Assert.Equal(0, ConceptDialogSearchMatcher.GetRank(["Genre"], "genre"));
        Assert.Equal(1, ConceptDialogSearchMatcher.GetRank(["Beschreibung"], "besch"));
        Assert.Equal(2, ConceptDialogSearchMatcher.GetRank(["Beschreibung"], "schr"));
        Assert.Equal(3, ConceptDialogSearchMatcher.GetRank(["Beschreibung"], "bschb"));
        Assert.Null(ConceptDialogSearchMatcher.GetRank(["BookStitch Testfixture – keine echten bibliografischen Daten."], "beschr", allowOrderedSubsequence: false));
        Assert.Equal(2, ConceptDialogSearchMatcher.GetRank(["BookStitch Testfixture – keine echten bibliografischen Daten."], "bibliograf", allowOrderedSubsequence: false));
        Assert.Equal(3, ConceptDialogSearchMatcher.GetRank(["Kommentar"], "kmntr"));
        Assert.Null(ConceptDialogSearchMatcher.GetRank(["Title"], "tle"));
        Assert.Equal("WERKUNDAUSGABE", ConceptDialogSearchMatcher.Normalize(" Werk und Ausgabe "));
        Assert.Equal("RENE", ConceptDialogSearchMatcher.Normalize("René"));
        Assert.Equal(ConceptDialogSearchMatcher.Normalize("Rene"), ConceptDialogSearchMatcher.Normalize("René"));
        Assert.Equal(ConceptDialogSearchMatcher.Normalize("Nino"), ConceptDialogSearchMatcher.Normalize("Niño"));
        Assert.Equal(0, ConceptDialogSearchMatcher.GetRank(["René"], "Rene"));
        Assert.Equal(0, ConceptDialogSearchMatcher.GetRank(["Ájné"], "Ajne"));
        Assert.Equal(0, ConceptDialogSearchMatcher.GetRank(["Niño"], "Nino"));
        Assert.Equal(0, ConceptDialogSearchMatcher.GetRank(["Märchen"], "Marchen"));
    }

    [Theory]
    [InlineData(ProjectMetadataField.Series, "Reihe")]
    [InlineData(ProjectMetadataField.Series, "Series")]
    public void Concept_search_matches_localized_and_technical_series_names(
        ProjectMetadataField field,
        string query)
    {
        var descriptor = ConceptMetadataFieldCatalog.Get(field);

        Assert.Equal(0, ConceptDialogSearchMatcher.GetRank(descriptor.SearchTerms, query));
    }

    [Theory]
    [InlineData("Reihen Nr.")]
    [InlineData("Series Part")]
    public void Concept_search_matches_localized_and_technical_series_part_names(string query)
    {
        var descriptor = ConceptMetadataFieldCatalog.Get(ConceptMetadataFieldCatalog.SeriesPartId);

        Assert.Equal(0, ConceptDialogSearchMatcher.GetRank(descriptor.SearchTerms, query));
    }

    [Theory]
    [InlineData("original-data", "quick")]
    [InlineData(null, "quick")]
    [InlineData("", "quick")]
    [InlineData("cover", "cover")]
    [InlineData("chapter", "chapter")]
    public void NormalizeConceptFamilyKey_maps_supported_and_legacy_keys(string? familyKey, string expected)
    {
        Assert.Equal(expected, MetadataDialog.NormalizeConceptFamilyKey(familyKey));
    }

    [Fact]
    public void Closing_with_active_drafts_uses_shared_warning_dialog_with_safe_default()
    {
        var code = LoadText("BookStitch", "Dialog", "MetadataDialog.xaml.cs").Replace("\r\n", "\n");
        var closingStart = code.IndexOf("private void MetadataDialog_Closing", StringComparison.Ordinal);
        var closingEnd = code.IndexOf("private MetadataDraftDescriptor[] GetActiveMetadataDrafts", closingStart, StringComparison.Ordinal);
        Assert.True(closingStart >= 0 && closingEnd > closingStart);
        var closing = code[closingStart..closingEnd];

        Assert.DoesNotContain("MessageBox.Show(", closing, StringComparison.Ordinal);
        Assert.Contains("AppDialogService.Show(", closing, StringComparison.Ordinal);
        Assert.Contains("AppDialogKind.Warning", closing, StringComparison.Ordinal);
        Assert.Contains("LocalizationText.Get(\"Metadata.Dialog.Back\")", closing, StringComparison.Ordinal);
        Assert.Contains("AppDialogResult.Cancel", closing, StringComparison.Ordinal);
        Assert.Contains("IsDefault: true", closing, StringComparison.Ordinal);
        Assert.Contains("IsCancel: true", closing, StringComparison.Ordinal);
        Assert.Contains("LocalizationText.Get(\"Metadata.Dialog.CloseAnyway\")", closing, StringComparison.Ordinal);
        Assert.Contains("AppDialogResult.Yes", closing, StringComparison.Ordinal);
        Assert.Contains("IsDanger: true", closing, StringComparison.Ordinal);
        Assert.Contains("if (result != AppDialogResult.Yes)", closing, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true;", closing, StringComparison.Ordinal);
        Assert.Contains("NavigateBackToDraftGroup();", closing, StringComparison.Ordinal);
        Assert.Contains("draft.Discard();", closing, StringComparison.Ordinal);
    }

    [Fact]
    public void Concept_draft_warning_message_lists_distinct_nonempty_labels_in_input_order()
    {
        var message = ConceptDraftWarningPresentation.BuildMessage(
            ["Copyright", "", "Title", "Copyright", "Veröffentlichungsdatum"]);

        Assert.StartsWith(LocalizationText.Get("Metadata.Dialog.UnconfirmedInputMessage"), message, StringComparison.Ordinal);
        Assert.Equal(
            ["• Copyright", "• Title", "• Veröffentlichungsdatum"],
            message.Split(Environment.NewLine).Where(line => line.StartsWith("• ", StringComparison.Ordinal)));
    }

    [Fact]
    public void ResolveDraftReturnFamily_prefers_current_affected_family_and_otherwise_uses_stable_first_family()
    {
        Assert.Equal(
            ConceptMetadataGroupIds.Description,
            MetadataDialog.ResolveDraftReturnFamily(
                ConceptMetadataGroupIds.Description,
                [ConceptMetadataGroupIds.Work, ConceptMetadataGroupIds.Description]));
        Assert.Equal(
            ConceptMetadataGroupIds.Work,
            MetadataDialog.ResolveDraftReturnFamily(
                ConceptMetadataGroupIds.People,
                [ConceptMetadataGroupIds.Work, ConceptMetadataGroupIds.Description]));
        Assert.Equal(
            ConceptMetadataGroupIds.Description,
            MetadataDialog.ResolveDraftReturnFamily(
                null,
                [ConceptMetadataGroupIds.Description, ConceptMetadataGroupIds.Work]));
        Assert.Null(MetadataDialog.ResolveDraftReturnFamily(ConceptMetadataGroupIds.People, []));
    }

    [Fact]
    public void Output_and_binding_toggles_bind_distinct_state()
    {
        var fieldControl = LoadXaml("BookStitch", "Controls", "ConceptMetadataFieldControl.xaml");
        var bindingAccessory = LoadXaml("BookStitch", "Controls", "ConceptMetadataFieldBindingAccessory.xaml");
        var outputToggle = NamedElement(fieldControl, "OutputToggle");
        var bindingToggle = NamedElement(bindingAccessory, "BindingToggle");

        Assert.Equal("{DynamicResource ConceptOutputChipToggleStyle}", Attribute(outputToggle, "Style"));
        Assert.Contains("IsOutputEnabled", Attribute(outputToggle, "IsChecked"), StringComparison.Ordinal);
        Assert.Contains("Mode=TwoWay", Attribute(outputToggle, "IsChecked"), StringComparison.Ordinal);

        Assert.Equal("{DynamicResource ConceptSpecialFunctionChipToggleStyle}", Attribute(bindingToggle, "Style"));
        Assert.Contains("BindingState.IsActive", Attribute(bindingToggle, "IsChecked"), StringComparison.Ordinal);
        Assert.Contains("Mode=TwoWay", Attribute(bindingToggle, "IsChecked"), StringComparison.Ordinal);
        Assert.Equal("{Binding Label, ElementName=Root}", Attribute(bindingToggle, "AutomationProperties.Name"));
        Assert.Equal("{Binding InfoBody, ElementName=Root}", Attribute(bindingToggle, "AutomationProperties.HelpText"));
    }

    [Fact]
    public void Generic_candidate_editor_uses_shared_interaction_behavior()
    {
        var control = LoadXaml("BookStitch", "Controls", "ConceptMetadataFieldControl.xaml");
        var editor = NamedElement(control, "EditorComboBox");

        Assert.Equal("True", AttributeByLocalName(editor, "CandidateComboBoxInteraction.IsEnabled"));
        Assert.Equal("{Binding MaxInputLength}", AttributeByLocalName(editor, "CandidateComboBoxInteraction.MaxInputLength"));
        Assert.Equal("False", Attribute(editor, "IsTextSearchEnabled"));
    }

    [Fact]
    public void Structured_series_editors_use_the_shared_series_control_and_opt_out_of_generic_candidate_interaction()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var control = LoadXaml("BookStitch", "Controls", "ConceptSeriesFieldControl.xaml");
        var editor = NamedElement(control, "SeriesNameComboBox");

        Assert.Equal("ConceptSeriesFieldControl", NamedElement(dialog, "ConceptQuickAccessSeriesField").Name.LocalName);
        Assert.Equal("ConceptSeriesFieldControl", NamedElement(dialog, "ConceptWorkSeriesField").Name.LocalName);
        Assert.Equal("False", AttributeByLocalName(editor, "CandidateComboBoxInteraction.IsEnabled"));
        Assert.Equal("SeriesNameComboBox_PreviewTextInput", Attribute(editor, "PreviewTextInput"));
        Assert.Equal("False", Attribute(editor, "IsTextSearchEnabled"));
    }

    [Fact]
    public void Series_context_reservation_and_quick_access_year_isbn_layout_are_owned_by_their_hosts()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var seriesControl = LoadXaml("BookStitch", "Controls", "ConceptSeriesFieldControl.xaml");
        var quickSeriesHost = NamedElement(dialog, "ConceptQuickAccessSeriesHost");
        var workSeriesHost = NamedElement(dialog, "ConceptWorkSeriesHost");
        var seriesEditorHost = NamedElement(seriesControl, "SeriesEditorRowHost");
        var yearIsbnRow = NamedElement(dialog, "ConceptQuickAccessYearIsbnRow");
        var content = yearIsbnRow.Elements().First(element => element.Name.LocalName == "Grid" && Attribute(element, "Grid.Column") == "0");
        var year = NamedElement(dialog, "ConceptQuickAccessYearField");
        var isbn = NamedElement(dialog, "ConceptQuickAccessIsbnField");

        static string[] Widths(XElement grid) => grid.Elements()
            .Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Select(column => Attribute(column, "Width")).ToArray();

        Assert.Equal(["29*", "11*"], Widths(quickSeriesHost));
        Assert.Equal(["29*", "11*"], Widths(workSeriesHost));
        Assert.Equal(["*", "{Binding ColumnGap, RelativeSource={RelativeSource AncestorType={x:Type controls:ConceptSeriesFieldControl}}}", "*"], Widths(seriesEditorHost));
        Assert.Equal(["29*", "11*"], Widths(yearIsbnRow));
        Assert.Equal(["*", "18", "*"], Widths(content));
        Assert.Equal("0", Attribute(year, "Grid.Column"));
        Assert.Equal("2", Attribute(isbn, "Grid.Column"));
        Assert.Equal("True", Attribute(isbn, "UseFullWidth"));
        Assert.DoesNotContain(year.Descendants(), element => element.Name.LocalName == "ColumnDefinition" && Attribute(element, "Width") is "29*" or "11*");
    }

    [Fact]
    public void Description_quick_access_uses_the_shared_description_control()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        Assert.Equal("ConceptDescriptionFieldControl", NamedElement(dialog, "ConceptQuickAccessDescriptionField").Name.LocalName);
    }

    [Fact]
    public void Value_badges_wire_reordering_events_and_drag_state_feedback()
    {
        var control = LoadXaml("BookStitch", "Controls", "ConceptMetadataFieldControl.xaml");
        var styles = LoadXaml("BookStitch", "Themes", "ConceptDialogStyles.xaml");
        var badges = NamedElement(control, "ValueBadges");
        var badgeStyle = KeyedElement(styles, "Style", "ConceptValueBadgeStyle");
        var draggingTrigger = Assert.Single(
            badgeStyle.Descendants(),
            element => element.Name.LocalName == "DataTrigger" &&
                       Attribute(element, "Binding").Contains("IsDragging", StringComparison.Ordinal) &&
                       Attribute(element, "Value") == "True");

        Assert.Equal("True", Attribute(badges, "AllowDrop"));
        Assert.False(string.IsNullOrWhiteSpace(Attribute(badges, "PreviewMouseLeftButtonDown")));
        Assert.False(string.IsNullOrWhiteSpace(Attribute(badges, "PreviewMouseMove")));
        Assert.False(string.IsNullOrWhiteSpace(Attribute(badges, "DragOver")));
        Assert.False(string.IsNullOrWhiteSpace(Attribute(badges, "Drop")));
        Assert.Contains(
            draggingTrigger.Descendants(),
            element => element.Name.LocalName == "Setter" &&
                       Attribute(element, "Property") is "Background" or "BorderBrush");
    }

    [Fact]
    public void Chapter_grid_is_a_read_only_view_of_presentation_rows()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var grid = NamedElement(dialog, "ConceptChapterGrid");
        var proposalColumn = NamedElement(grid, "ConceptChapterProposalColumn");
        var durationColumn = NamedElement(grid, "ConceptChapterDurationColumn");
        var warningColumn = NamedElement(grid, "ConceptChapterWarningColumn");
        var proposalText = Assert.Single(
            proposalColumn.Descendants(),
            element => element.Name.LocalName == "TextBlock" &&
                       Attribute(element, "Text").Contains("ChapterProposal", StringComparison.Ordinal));

        Assert.Equal("{Binding ChapterTracksView}", Attribute(grid, "ItemsSource"));
        Assert.Equal("True", Attribute(grid, "IsReadOnly"));
        Assert.Equal("False", Attribute(grid, "CanUserSortColumns"));
        Assert.Equal("False", Attribute(grid, "CanUserReorderColumns"));
        Assert.Equal("False", Attribute(grid, "CanUserAddRows"));
        Assert.Equal("False", Attribute(grid, "CanUserDeleteRows"));
        Assert.Equal("{Binding ChapterProposal}", Attribute(proposalText, "Text"));
        Assert.Equal("{Binding Duration}", Attribute(durationColumn, "Binding"));
        Assert.Equal("{Binding ChapterWarning}", Attribute(warningColumn, "Binding"));
    }

    [Fact]
    public void Chapter_grid_and_settings_editor_use_independent_scroll_regions()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var chapterPanel = NamedElement(dialog, "ConceptChapterPanel");
        var grid = NamedElement(chapterPanel, "ConceptChapterGrid");
        var settings = NamedElement(chapterPanel, "ConceptChapterSettingsScrollViewer");

        Assert.Equal("Hidden", Attribute(grid, "ScrollViewer.VerticalScrollBarVisibility"));
        Assert.Equal("Hidden", Attribute(grid, "ScrollViewer.HorizontalScrollBarVisibility"));
        Assert.Equal("Auto", Attribute(settings, "VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", Attribute(settings, "HorizontalScrollBarVisibility"));
        Assert.DoesNotContain(settings.Ancestors(), ancestor => ReferenceEquals(ancestor, grid));
    }

    [Fact]
    public void Audiobook_context_binds_to_project_metadata_values()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var context = NamedElement(dialog, "ConceptMetadataContextAudiobook");
        var bindings = context
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.Contains("{Binding ContextBookTitle}", bindings);
        Assert.Contains("{Binding ContextAuthor}", bindings);
        Assert.Contains("{Binding ContextNarrator}", bindings);
        Assert.Contains("{Binding ContextAuthorLabel}", bindings);
        Assert.Contains("{Binding ContextNarratorLabel}", bindings);
    }

    [Fact]
    public void Compatibility_info_points_use_semantic_resource_bindings()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var compatibility = NamedElement(dialog, "ConceptCompatibilityPanel");
        var sharedControl = LoadXaml("BookStitch", "Controls", "ConceptCompatibilityFieldControl.xaml");
        var compatibilityInfo = NamedElement(sharedControl, "CompatibilityInfoPoint");
        var previewInfo = Assert.Single(
            compatibility.Descendants(),
            element => element.Name.LocalName == "InfoPoint" &&
                       Attribute(element, "Title") == "{DynamicResource InfoDescriptionOutputPreviewTitle}");

        Assert.Equal("{DynamicResource InfoCompatibilityDescriptionTitle}", Attribute(compatibilityInfo, "Title"));
        Assert.Equal("{DynamicResource InfoCompatibilityDescriptionBody}", Attribute(compatibilityInfo, "Body"));
        Assert.Equal("{DynamicResource InfoDescriptionOutputPreviewBody}", Attribute(previewInfo, "Body"));
    }

    [Fact]
    public void Cover_context_binds_to_processed_asset_details()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var coverContext = NamedElement(dialog, "ConceptMetadataContextCover");
        var bindings = coverContext
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => Attribute(element, "Text"))
            .ToArray();

        Assert.Contains("{Binding CoverDetailsFileName}", bindings);
        Assert.Contains("{Binding CoverDetailsFormat}", bindings);
        Assert.Contains("{Binding CoverDetailsDimensions}", bindings);
        Assert.Contains("{Binding CoverDetailsFileSize}", bindings);

        var rememberToggle = Assert.Single(coverContext.Descendants(), element =>
            element.Name.LocalName == "CheckBox" &&
            Attribute(element, "IsChecked").Contains("RememberLastCoverPickerFolder", StringComparison.Ordinal));
        Assert.Equal("{services:LocalizedText Key=UiCoverOrdnerMerken}", Attribute(rememberToggle, "Content"));
        Assert.Equal("{StaticResource SettingsToggleStyle}", Attribute(rememberToggle, "Style"));
        Assert.Contains("Mode=TwoWay", Attribute(rememberToggle, "IsChecked"), StringComparison.Ordinal);

        var detailStack = Assert.Single(coverContext.Elements(), element => element.Name.LocalName == "StackPanel");
        var detailChildren = detailStack.Elements().ToArray();
        var fileSizeIndex = Array.FindIndex(detailChildren, element =>
            Attribute(element, "Text") == "{Binding CoverDetailsFileSize}");
        var toggleIndex = Array.IndexOf(detailChildren, rememberToggle);
        var warningIndex = Array.FindIndex(detailChildren, element =>
            Attribute(element, "Text") == "{DynamicResource CoverWarningThresholdHeading}");
        Assert.True(fileSizeIndex >= 0 && fileSizeIndex < toggleIndex && toggleIndex < warningIndex);
    }

    [Fact]
    public void Surface_combo_box_style_uses_semantic_background_and_template_bound_border()
    {
        var styles = LoadXaml("BookStitch", "Themes", "ConceptDialogStyles.xaml");
        var surface = KeyedElement(styles, "Style", "SurfaceComboBoxStyle");

        Assert.Equal("{StaticResource AppearanceComboBoxStyle}", Attribute(surface, "BasedOn"));
        Assert.Contains(surface.Elements(), element =>
            element.Name.LocalName == "Setter" &&
            Attribute(element, "Property") == "Background" &&
            Attribute(element, "Value") == "{DynamicResource AppBackgroundBrush}");
        Assert.Contains(surface.Elements(), element =>
            element.Name.LocalName == "Setter" &&
            Attribute(element, "Property") == "BorderThickness" &&
            Attribute(element, "Value") == "0");
        Assert.DoesNotContain(surface.Elements(), element =>
            element.Name.LocalName == "Setter" &&
            Attribute(element, "Property") is "FontSize" or "Height");

        var popup = Assert.Single(surface.Descendants(), element => element.Name.LocalName == "Popup");
        var popupBorder = Assert.Single(popup.Descendants(), element => element.Name.LocalName == "Border");
        Assert.Equal("{DynamicResource AppBackgroundBrush}", Attribute(popupBorder, "Background"));
        Assert.Equal("{TemplateBinding BorderThickness}", Attribute(popupBorder, "BorderThickness"));
        Assert.Equal("{TemplateBinding BorderBrush}", Attribute(popupBorder, "BorderBrush"));
        Assert.DoesNotContain("#", surface.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ConceptCompactSettingsComboBoxStyle")]
    [InlineData("ConceptMetadataContextInlineComboBoxStyle")]
    public void Metadata_combo_box_variants_reuse_the_shared_surface_template(string styleKey)
    {
        var styles = LoadXaml("BookStitch", "Themes", "ConceptDialogStyles.xaml");
        var style = KeyedElement(styles, "Style", styleKey);

        Assert.Equal("{StaticResource SurfaceComboBoxStyle}", Attribute(style, "BasedOn"));
        Assert.DoesNotContain(
            style.Elements(),
            element => element.Name.LocalName == "Setter" && Attribute(element, "Property") == "Template");
    }

    [Fact]
    public void Cover_and_chapter_editors_use_their_semantic_combo_box_variants()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var cover = NamedElement(dialog, "ConceptCoverWarningThresholdComboBox");
        Assert.Equal("{StaticResource ConceptMetadataContextInlineComboBoxStyle}", Attribute(cover, "Style"));

        string[] chapterComboNames =
        [
            "ConceptChapterModeComboBox",
            "ConceptChapterNumberingStyleComboBox",
            "ConceptChapterSlot1ComboBox",
            "ConceptChapterSeparator1ComboBox",
            "ConceptChapterSlot2ComboBox",
            "ConceptChapterSeparator2ComboBox",
            "ConceptChapterSlot3ComboBox"
        ];
        Assert.All(chapterComboNames, name =>
            Assert.Equal(
                "{StaticResource ConceptCompactSettingsComboBoxStyle}",
                Attribute(NamedElement(dialog, name), "Style")));
    }

    [Fact]
    public void Metadata_scroll_viewer_reserves_its_vertical_gutter_when_the_scrollbar_is_hidden()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var styles = LoadXaml("BookStitch", "Themes", "AppearanceTheme.xaml");
        var scrollViewer = NamedElement(dialog, "ConceptScrollViewer");
        var gutterStyle = KeyedElement(styles, "Style", "ConceptStableVerticalGutterScrollViewerStyle");
        var verticalScrollBar = NamedElement(gutterStyle, "PART_VerticalScrollBar");
        var gutterColumn = Assert.Single(
            gutterStyle.Descendants(),
            element => element.Name.LocalName == "ColumnDefinition" && Attribute(element, "Width") != "*");
        var visibilityTrigger = Assert.Single(
            gutterStyle.Descendants(),
            element => element.Name.LocalName == "Trigger" &&
                       Attribute(element, "Property") == "ComputedVerticalScrollBarVisibility");
        var visibleSetter = Assert.Single(
            visibilityTrigger.Descendants(),
            element => element.Name.LocalName == "Setter" &&
                       Attribute(element, "TargetName") == "PART_VerticalScrollBar");

        Assert.Equal("{StaticResource ConceptStableVerticalGutterScrollViewerStyle}", Attribute(scrollViewer, "Style"));
        Assert.True(double.TryParse(Attribute(gutterColumn, "Width"), CultureInfo.InvariantCulture, out var gutterWidth));
        Assert.True(gutterWidth > 0);
        Assert.Equal(Attribute(gutterColumn, "Width"), Attribute(verticalScrollBar, "Width"));
        Assert.Equal("Hidden", Attribute(verticalScrollBar, "Visibility"));
        Assert.Equal("Visible", Attribute(visibilityTrigger, "Value"));
        Assert.Equal("Visibility", Attribute(visibleSetter, "Property"));
        Assert.Equal("Visible", Attribute(visibleSetter, "Value"));
    }

    [Fact]
    public void Description_is_editable_while_compatibility_values_and_output_preview_are_read_only()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var descriptionPanel = NamedElement(dialog, "ConceptDescriptionPanel");
        var compatibilityPanel = NamedElement(dialog, "ConceptCompatibilityPanel");
        var descriptionControl = Assert.Single(
            descriptionPanel.Descendants(),
            element => element.Name.LocalName == "ConceptDescriptionFieldControl");
        var descriptionColumns = descriptionPanel
            .Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Select(column => Attribute(column, "Width")).ToArray();
        var sharedDescription = LoadXaml("BookStitch", "Controls", "ConceptDescriptionFieldControl.xaml");
        var descriptionHeader = NamedElement(sharedDescription, "DescriptionHeaderContent");
        var descriptionEditorBorder = NamedElement(sharedDescription, "DescriptionEditorBorder");
        var descriptionEditor = NamedElement(sharedDescription, "DescriptionTextBox");
        var compatibilityControl = NamedElement(compatibilityPanel, "ConceptCompatibilityField");
        Assert.Equal("ConceptCompatibilityFieldControl", compatibilityControl.Name.LocalName);
        var sharedCompatibility = LoadXaml("BookStitch", "Controls", "ConceptCompatibilityFieldControl.xaml");
        var compatibilityField = NamedElement(sharedCompatibility, "CompatibilityFieldTextBox");
        var embeddingToggle = NamedElement(sharedCompatibility, "CompatibilityDescriptionToggle");
        var compatibilityInfo = NamedElement(sharedCompatibility, "CompatibilityInfoPoint");
        var outputPreview = Assert.Single(
            compatibilityPanel.Descendants(),
            element => element.Name.LocalName == "TextBox" &&
                       Attribute(element, "Text").Contains("CompatibilityDescriptionOutputPreview", StringComparison.Ordinal));

        Assert.Equal(["29*", "11*"], descriptionColumns);
        Assert.Equal("0", Attribute(descriptionControl, "Grid.Column"));
        Assert.Equal("Stretch", Attribute(descriptionControl, "PresentationMode"));

        static XElement StretchColumnSpanSetter(XElement element)
        {
            var styleProperty = Assert.Single(
                element.Elements(),
                candidate => candidate.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal));
            var style = Assert.Single(
                styleProperty.Elements(),
                candidate => candidate.Name.LocalName == "Style");
            var triggers = Assert.Single(
                style.Elements(),
                candidate => candidate.Name.LocalName == "Style.Triggers");
            var stretchTrigger = Assert.Single(
                triggers.Elements(),
                candidate => candidate.Name.LocalName == "DataTrigger" &&
                             Attribute(candidate, "Value") == "Stretch");
            return Assert.Single(
                stretchTrigger.Elements(),
                candidate => candidate.Name.LocalName == "Setter" &&
                             Attribute(candidate, "Property") == "Grid.ColumnSpan");
        }

        Assert.Equal("2", Attribute(StretchColumnSpanSetter(descriptionHeader), "Value"));
        Assert.Equal("2", Attribute(StretchColumnSpanSetter(descriptionEditorBorder), "Value"));

        Assert.Contains("Mode=TwoWay", Attribute(descriptionEditor, "Text"), StringComparison.Ordinal);
        Assert.Contains("UpdateSourceTrigger=PropertyChanged", Attribute(descriptionEditor, "Text"), StringComparison.Ordinal);
        Assert.Equal("True", Attribute(descriptionEditor, "AcceptsReturn"));
        Assert.DoesNotContain(
            descriptionPanel.DescendantsAndSelf().Attributes(),
            attribute => attribute.Value.Contains("Compatibility", StringComparison.Ordinal));

        Assert.Equal("{DynamicResource InfoCompatibilityDescriptionTitle}", Attribute(compatibilityInfo, "Title"));
        Assert.Equal("{DynamicResource InfoCompatibilityDescriptionBody}", Attribute(compatibilityInfo, "Body"));
        Assert.Contains("Mode=OneWay", Attribute(compatibilityField, "Text"), StringComparison.Ordinal);
        Assert.Equal("True", Attribute(compatibilityField, "IsReadOnly"));
        Assert.Contains("Mode=OneWay", Attribute(outputPreview, "Text"), StringComparison.Ordinal);
        Assert.Equal("True", Attribute(outputPreview, "IsReadOnly"));
        Assert.Contains("IsCompatibilityDescriptionEnabled", Attribute(embeddingToggle, "IsChecked"), StringComparison.Ordinal);
        Assert.Contains("Mode=TwoWay", Attribute(embeddingToggle, "IsChecked"), StringComparison.Ordinal);
    }

    [Fact]
    public void Search_adornment_exposes_an_accessible_search_action()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        Assert.Equal("{StaticResource LargeResizableWindowStyle}", Attribute(dialog.Root!, "Style"));
        Assert.Equal("920", Attribute(dialog.Root!, "MinWidth"));
        Assert.Equal("590", Attribute(dialog.Root!, "MinHeight"));
        Assert.DoesNotContain(dialog.Root!.Elements(), element => element.Name.LocalName == "WindowChrome.WindowChrome");

        var header = Assert.Single(dialog.Descendants(), element => element.Name.LocalName == "LargeResizableWindowHeader");
        Assert.Equal("200", Attribute(header, "TitleAreaWidth"));
        Assert.Equal("Close_Click", Attribute(header, "CloseRequested"));

        var centerContent = Assert.Single(header.Elements(), element => element.Name.LocalName == "LargeResizableWindowHeader.CenterContent");
        var searchBox = Assert.Single(centerContent.Descendants(), element => AttributeByLocalName(element, "Name") == "ConceptSearchTextBox");
        Assert.Equal("{StaticResource ConceptSearchTextBoxStyle}", Attribute(searchBox, "Style"));
        Assert.Equal("ConceptSearchTextBox_TextChanged", Attribute(searchBox, "TextChanged"));
        Assert.Equal("ConceptSearchTextBox_PreviewKeyDown", Attribute(searchBox, "PreviewKeyDown"));
        var searchHost = searchBox.Parent!;
        Assert.Equal("{Binding ActualWidth, ElementName=ConceptEditorColumnMetric}", Attribute(searchHost, "Width"));
        Assert.Contains(dialog.Descendants(), element => AttributeByLocalName(element, "Name") == "ConceptEditorColumnMetric" && Attribute(element, "Width") == "29*");

        var button = Assert.Single(
            centerContent.Descendants(),
            element => element.Name.LocalName == "Button" &&
                       Attribute(element, "Click") == "ConceptSearchAdornment_Click");

        Assert.Equal("{Binding SearchAdornment}", Attribute(button, "Content"));
        Assert.False(string.IsNullOrWhiteSpace(Attribute(button, "AutomationProperties.Name")));
        Assert.Equal("True", AttributeByLocalName(button, "WindowChrome.IsHitTestVisibleInChrome"));
    }

    [Fact]
    public void Navigation_keeps_editing_and_utility_families_separate()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var primary = NamedElement(dialog, "ConceptFamilyList");
        var utility = NamedElement(dialog, "ConceptUtilityFamilyList");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var primaryNames = primary.Elements().Select(element => Attribute(element, x + "Name")).ToArray();
        var utilityNames = utility.Elements().Select(element => Attribute(element, x + "Name")).ToArray();

        Assert.Contains("ConceptDescriptionFamilyItem", primaryNames);
        Assert.Contains("ConceptCompatibilityFamilyItem", primaryNames);
        Assert.Contains("ConceptCoverFamilyItem", primaryNames);
        Assert.DoesNotContain(primaryNames, utilityNames.Contains);
        Assert.Equal(
            ["ConceptChapterFamilyItem", "ConceptPassThroughFamilyItem", "ConceptHelpFamilyItem"],
            utilityNames);
        Assert.Equal("ConceptFamilyList_SelectionChanged", Attribute(primary, "SelectionChanged"));
        Assert.Equal("ConceptFamilyList_SelectionChanged", Attribute(utility, "SelectionChanged"));
    }

    [Fact]
    public void Accent_badge_text_uses_shared_on_accent_semantics_without_collapsing_special_badges()
    {
        var styles = LoadXaml("BookStitch", "Themes", "ConceptDialogStyles.xaml");
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var textStyle = KeyedElement(styles, "Style", "ConceptValueBadgeTextStyle");
        Assert.Contains(textStyle.Elements(), element =>
            element.Name.LocalName == "Setter" &&
            Attribute(element, "Property") == "Foreground" &&
            Attribute(element, "Value") == "{DynamicResource OnAccentTextBrush}");
        Assert.Contains(textStyle.Descendants(), element =>
            element.Name.LocalName == "Setter" &&
            Attribute(element, "Value") == "{DynamicResource ConceptPassThroughValueBadgeTextBrush}");

        var accentBadges = dialog.Descendants()
            .Where(element => Attribute(element, "Style") is "{StaticResource ConceptAuthorValueBadgeStyle}" or "{StaticResource ConceptSingleValueBadgeStyle}")
            .ToArray();
        Assert.NotEmpty(accentBadges);
        Assert.All(accentBadges, badge => Assert.Contains(badge.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            Attribute(element, "Style") == "{StaticResource ConceptValueBadgeTextStyle}"));
    }

    [Fact]
    public void Metadata_field_badge_actions_bind_to_lock_and_distinct_edit_remove_permissions()
    {
        var fieldControl = LoadXaml("BookStitch", "Controls", "ConceptMetadataFieldControl.xaml");
        var editButton = Assert.Single(
            fieldControl.Descendants(),
            element => element.Name.LocalName == "Button" && Attribute(element, "Click") == "EditValue_Click");
        var removeButton = Assert.Single(
            fieldControl.Descendants(),
            element => element.Name.LocalName == "Button" && Attribute(element, "Click") == "RemoveValue_Click");
        var lockedTrigger = Assert.Single(
            fieldControl.Descendants(),
            element => element.Name.LocalName == "DataTrigger" &&
                       Attribute(element, "Binding").Contains("IsLocked", StringComparison.Ordinal));

        Assert.Contains("CanEditValues", Attribute(editButton, "IsEnabled"), StringComparison.Ordinal);
        Assert.Contains("CanRemoveValues", Attribute(removeButton, "IsEnabled"), StringComparison.Ordinal);
        Assert.Contains(
            lockedTrigger.Descendants(),
            element => element.Name.LocalName == "Setter" &&
                       Attribute(element, "Property") == "Visibility" &&
                       Attribute(element, "Value") == "Collapsed");
    }

    [Theory]
    [InlineData("TrackNumber", "CanEditTrackNumber", "CanRemoveTrackNumber")]
    [InlineData("TrackCount", "CanEditTrackCount", "CanRemoveTrackCount")]
    public void Track_badge_actions_bind_edit_and_remove_to_distinct_permissions(
        string fieldKind,
        string editPermission,
        string removePermission)
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var editButton = Assert.Single(
            dialog.Descendants(),
            element => element.Name.LocalName == "Button" &&
                       Attribute(element, "Tag") == fieldKind &&
                       Attribute(element, "Click") == "EditSingleValue_Click");
        var removeButton = Assert.Single(
            dialog.Descendants(),
            element => element.Name.LocalName == "Button" &&
                       Attribute(element, "Tag") == fieldKind &&
                       Attribute(element, "Click") == "RemoveSingleValue_Click");

        Assert.Contains(editPermission, Attribute(editButton, "Visibility"), StringComparison.Ordinal);
        Assert.Contains(editPermission, Attribute(editButton, "IsEnabled"), StringComparison.Ordinal);
        Assert.Contains(removePermission, Attribute(removeButton, "IsEnabled"), StringComparison.Ordinal);
        Assert.DoesNotContain(editPermission, Attribute(removeButton, "IsEnabled"), StringComparison.Ordinal);
    }

    [Fact]
    public void Badge_edit_action_is_revealed_by_hover_or_keyboard_focus()
    {
        var styles = LoadXaml("BookStitch", "Themes", "ConceptDialogStyles.xaml");
        var editStyle = KeyedElement(styles, "Style", "ConceptValueBadgeEditButtonStyle");
        var defaultOpacity = Assert.Single(
            editStyle.Elements(),
            element => element.Name.LocalName == "Setter" && Attribute(element, "Property") == "Opacity");
        var hoverTrigger = Assert.Single(
            editStyle.Descendants(),
            element => element.Name.LocalName == "DataTrigger" &&
                       Attribute(element, "Binding").Contains("IsMouseOver", StringComparison.Ordinal));
        var focusTrigger = Assert.Single(
            editStyle.Descendants(),
            element => element.Name.LocalName == "Trigger" &&
                       Attribute(element, "Property") == "IsKeyboardFocused" &&
                       Attribute(element, "Value") == "True");

        Assert.Equal("0", Attribute(defaultOpacity, "Value"));
        Assert.Contains(
            hoverTrigger.Descendants(),
            element => element.Name.LocalName == "Setter" &&
                       Attribute(element, "Property") == "Opacity" &&
                       Attribute(element, "Value") == "1");
        Assert.Contains(
            focusTrigger.Descendants(),
            element => element.Name.LocalName == "Setter" &&
                       Attribute(element, "Property") == "Opacity" &&
                       Attribute(element, "Value") == "1");
    }

    private static string LoadText(params string[] relativePath)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine([root, .. relativePath]));
    }

    [Fact]
    public void Shared_series_control_owns_host_independent_series_resources()
    {
        var control = LoadXaml("BookStitch", "Controls", "ConceptSeriesFieldControl.xaml");
        var merged = control.Descendants().First(element => element.Name.LocalName == "ResourceDictionary.MergedDictionaries");
        Assert.Contains(merged.Elements(), element =>
            Attribute(element, "Source").Contains("ConceptDialogStyles.xaml", StringComparison.Ordinal));
        Assert.NotNull(control.Descendants().FirstOrDefault(element => AttributeByLocalName(element, "Key") == "ConceptSeriesEntryCandidateTemplate"));
        Assert.NotNull(control.Descendants().FirstOrDefault(element => AttributeByLocalName(element, "Key") == "ConceptSeriesValueBadgeStyle"));

        var styles = LoadXaml("BookStitch", "Themes", "ConceptDialogStyles.xaml");
        Assert.NotNull(styles.Descendants().FirstOrDefault(element => AttributeByLocalName(element, "Key") == "ConceptAuthorBadgeRemoveButtonStyle"));
    }

    [Fact]
    public void Metadata_dialog_places_shared_series_and_description_controls_without_local_series_editor_copy()
    {
        var dialog = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        Assert.Equal("ConceptSeriesFieldControl", NamedElement(dialog, "ConceptQuickAccessSeriesField").Name.LocalName);
        Assert.Equal("ConceptSeriesFieldControl", NamedElement(dialog, "ConceptWorkSeriesField").Name.LocalName);
        Assert.Equal("ConceptDescriptionFieldControl", NamedElement(dialog, "ConceptQuickAccessDescriptionField").Name.LocalName);
        Assert.Equal("ConceptDescriptionFieldControl", NamedElement(dialog, "ConceptDescriptionFullField").Name.LocalName);
        Assert.DoesNotContain(dialog.Descendants(), element =>
            element.Name.LocalName == "ComboBox" &&
            AttributeByLocalName(element, "Name").Contains("SeriesName", StringComparison.Ordinal));
    }

    private static XDocument LoadXaml(params string[] relativePath) =>
        XDocument.Parse(LoadText(relativePath), LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

    private static XElement NamedElement(XContainer container, string name)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return Assert.Single(container.Descendants(), element => Attribute(element, x + "Name") == name);
    }

    private static XElement KeyedElement(XContainer container, string elementName, string key)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return Assert.Single(
            container.Descendants(),
            element => element.Name.LocalName == elementName && Attribute(element, x + "Key") == key);
    }

    private static string Attribute(XElement element, XName name) =>
        element.Attribute(name)?.Value ?? string.Empty;

    private static string AttributeByLocalName(XElement element, string localName) =>
        element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == localName)?.Value ?? string.Empty;

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BookStitch.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with BookStitch.slnx was not found.");
    }
}
