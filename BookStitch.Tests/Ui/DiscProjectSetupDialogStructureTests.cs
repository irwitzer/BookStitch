using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class DiscProjectSetupDialogStructureTests
{
    private static readonly string DialogSource = RepositorySource.Read(
        ["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml.cs"]);
    private static readonly string MainWindowSource = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Fact]
    public void Output_folder_selection_updates_the_dialog_and_publishes_the_global_choice()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        var stateChip = SingleElement(document, "Button", "x:Name", "OutputFolderStateChipButton");
        var chooseButton = SingleElement(document, "Button", "x:Name", "OpenOutputFolderButton");
        var choose = SourceContract.ExtractMember(DialogSource, "private void ChooseOutputFolder_Click");
        var setOutputFolder = SourceContract.ExtractMember(DialogSource, "private void SetOutputFolder(");
        var factory = SourceContract.ExtractMember(MainWindowSource, "private DiscProjectSetupDialog CreateProjectSetupDialog");

        Assert.Equal("ChooseOutputFolder_Click", Attribute(stateChip, "Click"));
        Assert.Equal("OpenOutputFolder_Click", Attribute(chooseButton, "Click"));
        Assert.Equal("{StaticResource AccentOpenOutputFolderButtonStyle}", Attribute(chooseButton, "Style"));
        SourceContract.AssertInOrder(choose, "if (dialog.ShowDialog(this) != true)", "return;", "SetOutputFolder(dialog.FolderName, persistGlobally: true)");
        Assert.DoesNotContain("OutputFolderStateChipButton.IsEnabled", setOutputFolder);
        Assert.Contains("OpenOutputFolderButton.IsEnabled", setOutputFolder);
        var open = SourceContract.ExtractMember(DialogSource, "private void OpenOutputFolder_Click");
        Assert.Contains("Directory.Exists(_outputFolder)", open);
        Assert.Contains("System.Diagnostics.Process.Start", open);
        Assert.DoesNotContain("SetOutputFolder(", open);
        Assert.DoesNotContain("OpenFolderDialog", open);
        Assert.Contains("SetOutputFolder(dialog.FolderName, persistGlobally: true);", choose, StringComparison.Ordinal);
        Assert.Contains("_setOutputFolder?.Invoke(_outputFolder);", setOutputFolder, StringComparison.Ordinal);
        SourceContract.AssertInOrder(factory, "setOutputFolder: folder =>", "OutputFolder = folder;");
        Assert.Contains("OutputFolder);", factory, StringComparison.Ordinal);

        var settingsButton = SourceContract.ExtractMember(DialogSource, "private void SettingsButton_Click");
        Assert.Contains("SetOutputFolder(settings.OutputFolder, persistGlobally: false);", settingsButton, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private void UpdateOutputFolderBadge()", "BookStitch/Dialog/DiscProjectSetupDialog.xaml.cs", false)]
    [InlineData("internal void RefreshOutputFolderBadge()", "BookStitch/Controls/MainOutputSettingsControl.xaml.cs", true)]
    public void Output_folder_badges_delegate_text_measurement_to_the_shared_service(
        string memberSignature,
        string relativePath,
        bool controlsWidth)
    {
        var source = RepositorySource.Read(relativePath.Split('/'));
        var update = SourceContract.ExtractMember(source, memberSignature);

        Assert.Contains("OutputFolderBadgeDisplayService.BuildText(", update, StringComparison.Ordinal);
        Assert.Equal(
            controlsWidth,
            update.Contains("OutputFolderBadgeDisplayService.CalculateWidth(", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1000, 1120, 800, 1920, 920)]
    [InlineData(2000, 1120, 800, 1920, 1200)]
    [InlineData(0, 1120, 800, 1920, 1120)]
    [InlineData(500, 1120, 800, 1920, 800)]
    [InlineData(1200, 1120, 800, 900, 820)]
    public void Initial_width_stays_within_owner_and_work_area_bounds(
        double ownerWidth,
        double fallbackWidth,
        double minimumWidth,
        double workAreaWidth,
        double expected)
    {
        Assert.Equal(
            expected,
            global::BookStitch.MainWindow.CalculateProjectSetupDialogInitialWidth(
                ownerWidth,
                fallbackWidth,
                minimumWidth,
                workAreaWidth));
    }

    [Fact]
    public void Disc_count_input_and_preview_are_limited_to_two_digits()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        var control = RepositorySource.LoadXaml(["BookStitch", "Controls", "DiscCountInputControl.xaml"]);
        var discCount = SingleElement(control, "TextBox", "x:Name", "Input");
        var constructor = SourceContract.ExtractMember(DialogSource, "public DiscProjectSetupDialog(");
        var factory = SourceContract.ExtractMember(MainWindowSource, "private DiscProjectSetupDialog CreateProjectSetupDialog");

        Assert.Equal("2", Attribute(discCount, "MaxLength"));
        Assert.Contains("UpdateSourceTrigger=PropertyChanged", Attribute(discCount, "Text"));
        Assert.Contains("Math.Clamp(request.MaximumDiscs, _minimumDiscs, 99)", constructor, StringComparison.Ordinal);
        Assert.Contains("discCountChanged: totalDiscs =>", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void Setup_layout_mode_and_keyboard_contracts_match_the_shared_project_preparation_surface()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Equal("{StaticResource LargeResizableWindowStyle}", Attribute(document.Root!, "Style"));
        Assert.Equal("1130", Attribute(document.Root!, "MinWidth"));
        var sharedHeader = SingleElement(document, "LargeResizableWindowHeader", "CloseRequested", "CloseButton_Click");
        Assert.Contains(sharedHeader.Descendants(), element => Attribute(element, "Style") == "{StaticResource LargeResizableWindowHeaderTitleTextStyle}");
        var titleContent = sharedHeader.Descendants().Single(element => element.Name.LocalName == "LargeResizableWindowHeader.TitleContent");
        Assert.Contains(titleContent.Descendants(), element => Attribute(element, "Command") == "{x:Static services:DeveloperToolsCommands.OpenDeveloperToolsWindow}");
        var headerActions = sharedHeader.Descendants().Single(element => element.Name.LocalName == "LargeResizableWindowHeader.HeaderActions");
        SourceContract.AssertInOrder(headerActions.ToString(SaveOptions.DisableFormatting), "x:Name=\"MetadataButton\"", "Click=\"SettingsButton_Click\"");

        static string[] ColumnWidths(XElement grid) => grid
            .Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Select(column => Attribute(column, "Width")).ToArray();

        var page0Cards = SingleElement(document, "Grid", "x:Name", "SetupPage0CardsGrid");
        var page1Cards = SingleElement(document, "Grid", "x:Name", "SetupPage1CardsGrid");
        Assert.Equal(["1*", "20", "1*"], ColumnWidths(page0Cards));
        Assert.Equal(["1*", "20", "1*"], ColumnWidths(page1Cards));

        var navigation = SingleElement(document, "Grid", "x:Name", "SetupNavigationOverlay");
        var navigationColumns = ColumnWidths(navigation);
        Assert.Equal(ColumnWidths(page0Cards), navigationColumns.Take(3).ToArray());
        Assert.Equal("14", navigationColumns[3]);
        var previous = SingleElement(document, "Button", "x:Name", "PreviousSetupPageButton");
        var next = SingleElement(document, "Button", "x:Name", "NextSetupPageButton");
        Assert.Equal("0", Attribute(previous, "Grid.Column"));
        Assert.Equal("Right", Attribute(previous, "HorizontalAlignment"));
        Assert.Equal("2", Attribute(next, "Grid.Column"));
        Assert.Equal("Left", Attribute(next, "HorizontalAlignment"));
        Assert.All(new[] { previous, next }, button =>
            Assert.Equal("25", Attribute(button.Descendants().Single(element => element.Name.LocalName == "Viewbox"), "Width")));
        Assert.DoesNotContain(navigation.Descendants(), element =>
            element.Name.LocalName == "TranslateTransform" && Attribute(element, "X") == "-4");

        var start = SingleElement(document, "Button", "x:Name", "StartProjectButton");
        Assert.NotEqual("True", Attribute(start, "IsDefault"));
        Assert.Equal("Window_PreviewKeyDown", Attribute(document.Root!, "PreviewKeyDown"));
        Assert.DoesNotContain("Input_KeyDown", RepositorySource.Read(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]), StringComparison.Ordinal);
        Assert.DoesNotContain("private void Input_KeyDown", DialogSource, StringComparison.Ordinal);
        Assert.Contains("NavigateSetupPage(direction)", SourceContract.ExtractMember(DialogSource, "private void Window_PreviewKeyDown"), StringComparison.Ordinal);
        Assert.Contains("ConceptMetadataFieldControl or TextBoxBase", SourceContract.ExtractMember(DialogSource, "private static bool ShouldPreserveArrowKeyForFocusedEditor"), StringComparison.Ordinal);

        foreach (var textBoxName in new[] { "DiscCountTextBox", "ParallelJobsTextBox" })
        {
            var inputDocument = textBoxName == "DiscCountTextBox"
                ? RepositorySource.LoadXaml(["BookStitch", "Controls", "DiscCountInputControl.xaml"]) : document;
            var textBox = SingleElement(inputDocument, "TextBox", "x:Name", textBoxName == "DiscCountTextBox" ? "Input" : textBoxName);
            var stepperGrid = textBox.Parent!;
            var repeatButtons = stepperGrid.Elements().Where(element => element.Name.LocalName == "RepeatButton").ToArray();
            Assert.Equal(2, repeatButtons.Length);
            Assert.All(repeatButtons, button => Assert.Equal("{StaticResource StepperRepeatButtonStyle}", Attribute(button, "Style")));
        }
    }

    [Fact]
    public void Embedded_metadata_fields_use_the_shared_minimal_editor_surface()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        var expectedFields = new[]
        {
            "SetupTitleField",
            "SetupAlbumField",
            "SetupAuthorField",
            "SetupNarratorField",
            "SetupGenreField"
        };

        foreach (var fieldName in expectedFields)
        {
            var field = SingleElement(document, "ConceptMetadataFieldControl", "x:Name", fieldName);
            Assert.Equal("Minimal", Attribute(field, "PresentationMode"));
        }
    }

    [Fact]
    public void Project_settings_card_owns_the_single_cover_and_metadata_card_contains_only_fields()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var cover = SingleElement(document, "Border", "x:Name", "CoverBorder");
        var coverWidth = double.Parse(Attribute(cover, "Width"), System.Globalization.CultureInfo.InvariantCulture);
        var coverHeight = double.Parse(Attribute(cover, "Height"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(189d, coverWidth);
        Assert.Equal(189d, coverHeight);
        Assert.Equal("CoverBorder_MouseLeftButtonUp", Attribute(cover, "MouseLeftButtonUp"));
        Assert.Equal("CoverBorder_ContextMenuOpening", Attribute(cover, "ContextMenuOpening"));

        var projectTitle = document.Descendants(presentation + "TextBlock").Single(text => Attribute(text, "Text") == "{services:LocalizedText Key=UiProjektEinstellungen}");
        var projectCard = projectTitle.Ancestors(presentation + "Border").First();
        Assert.Contains(cover, projectCard.Descendants(presentation + "Border"));

        var metadataTitle = document.Descendants(presentation + "TextBlock").Single(text => Attribute(text, "Text") == "{services:LocalizedText Key=UiMetadaten}");
        var metadataCard = metadataTitle.Ancestors(presentation + "Border").First();
        Assert.DoesNotContain(metadataCard.Descendants(presentation + "Border"), element => Attribute(element, "x:Name") == "CoverBorder");
        Assert.Equal(5, metadataCard.Descendants().Count(element =>
            element.Name.LocalName == "ConceptMetadataFieldControl" && Attribute(element, "PresentationMode") == "Minimal"));
        Assert.DoesNotContain(document.Descendants(presentation + "TextBlock"), text => Attribute(text, "Text") == "Cover und Metadaten");
    }

    [Fact]
    public void Project_settings_visual_order_matches_disc_parallel_extension_preset_automerge_then_output_fields()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        var names = document.Descendants()
            .Select(element => Attribute(element, "x:Name"))
            .Where(name => name.Length > 0)
            .ToArray();

        static int Index(string[] values, string name) => Array.IndexOf(values, name);
        Assert.True(Index(names, "DiscCountPanel") < Index(names, "ParallelJobsTextBox"));
        Assert.True(Index(names, "ParallelJobsTextBox") < Index(names, "OutputExtensionComboBox"));
        Assert.True(Index(names, "OutputExtensionComboBox") < Index(names, "PresetComboBox"));
        Assert.True(Index(names, "PresetComboBox") < Index(names, "AutoMergeCheckBox"));
        Assert.True(Index(names, "AutoMergeCheckBox") < Index(names, "OpenOutputFolderButton"));
        Assert.True(Index(names, "OpenOutputFolderButton") < Index(names, "FileNameTemplateTextBox"));
        Assert.True(Index(names, "FileNameTemplateTextBox") < Index(names, "PreviewTextBlock"));
    }

    [Fact]
    public void Project_settings_header_uses_one_compact_mode_grid_with_preset_and_noninteractive_auto_status()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var header = SingleElement(document, "Grid", "x:Name", "ProjectSettingsHeaderGrid");
        var baseFields = SingleElement(document, "Grid", "x:Name", "ProjectSettingsBaseFieldsGrid");
        var output = SingleElement(document, "Grid", "x:Name", "ProjectSettingsOutputGrid");
        var discLabel = SingleElement(document, "TextBlock", "x:Name", "DiscCountLabel");
        var discPanel = SingleElement(document, "DiscCountInputControl", "x:Name", "DiscCountPanel");
        var parallel = SingleElement(document, "TextBox", "x:Name", "ParallelJobsTextBox");
        var extension = SingleElement(document, "ComboBox", "x:Name", "OutputExtensionComboBox");
        var preset = SingleElement(document, "ComboBox", "x:Name", "PresetComboBox");
        var autoMerge = SingleElement(document, "CheckBox", "x:Name", "AutoMergeCheckBox");
        var cover = SingleElement(document, "Border", "x:Name", "CoverBorder");
        var projectFolderLabel = SingleElement(document, "TextBlock", "x:Name", "ProjectFolderLabel");
        var projectFolderButton = SingleElement(document, "Button", "x:Name", "ProjectFolderButton");
        var outputFolder = SingleElement(document, "Button", "x:Name", "OutputFolderStateChipButton");

        var rows = baseFields.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions").Elements().ToArray();
        Assert.Equal(6, rows.Length);
        Assert.All(rows, row => Assert.Equal("Auto", Attribute(row, "Height")));
        Assert.Equal("136", Attribute(baseFields.Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions").Elements().First(), "Width"));
        Assert.Equal("0", Attribute(discLabel, "Grid.Row"));
        Assert.Equal("0", Attribute(discPanel, "Grid.Row"));
        Assert.Equal("1", Attribute(parallel.Parent!, "Grid.Row"));
        Assert.Equal("2", Attribute(extension, "Grid.Row"));
        Assert.Equal("3", Attribute(preset, "Grid.Row"));
        Assert.Equal("150", Attribute(preset, "Width"));
        Assert.Equal("4", Attribute(autoMerge, "Grid.Row"));
        Assert.Equal("{services:LocalizedText Key=UiAuto}", Attribute(autoMerge, "Content"));
        Assert.Equal("{StaticResource SettingsToggleStyle}", Attribute(autoMerge, "Style"));
        Assert.Equal("AutoMergeCheckBox_Changed", Attribute(autoMerge, "Checked"));
        Assert.Equal("AutoMergeCheckBox_Changed", Attribute(autoMerge, "Unchecked"));

        var applyMode = SourceContract.ExtractMember(DialogSource, "private void ApplySourceModeProfile(ProjectSetupModeProfile profile)");
        Assert.Contains("DiscCountLabel.Visibility = profile.ShowsDiscCount", applyMode, StringComparison.Ordinal);
        Assert.Contains("DiscCountPanel.Visibility = profile.ShowsDiscCount", applyMode, StringComparison.Ordinal);

        Assert.Equal("0,0,0,20", Attribute(header, "Margin"));
        Assert.Equal("210", Attribute(header, "MinHeight"));
        Assert.Equal("189", Attribute(cover, "Width"));
        Assert.Equal("189", Attribute(cover, "Height"));
        Assert.Equal("14,28,0,0", Attribute(cover, "Margin"));
        Assert.Equal("Right", Attribute(cover, "HorizontalAlignment"));
        Assert.Equal("Top", Attribute(cover, "VerticalAlignment"));
        Assert.DoesNotContain("-", Attribute(cover, "Margin"), StringComparison.Ordinal);
        Assert.Contains(cover, header.Descendants(presentation + "Border"));

        Assert.DoesNotContain(preset, output.Descendants());
        Assert.Equal("0", Attribute(projectFolderLabel, "Grid.Row"));
        Assert.Equal("0", Attribute(projectFolderButton, "Grid.Row"));
        Assert.Equal("1", Attribute(outputFolder.Parent!, "Grid.Row"));
        Assert.Equal("136", Attribute(output.Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions").Elements().First(), "Width"));
        Assert.Equal("37", Attribute(SingleElement(document, "Border", "x:Name", "ProjectSettingsOutputSpacer"), "Height"));

        var sourceFolderText = SingleElement(document, "TextBlock", "x:Name", "ProjectFolderTextBlock");
        Assert.Equal("NoWrap", Attribute(sourceFolderText, "TextWrapping"));
        Assert.Equal("CharacterEllipsis", Attribute(sourceFolderText, "TextTrimming"));
        Assert.Single(document.Descendants(), element =>
            Attribute(element, "TextTrimming") == "CharacterEllipsis" &&
            Attribute(element, "x:Name") == "ProjectFolderTextBlock");
    }

    [Fact]
    public void Setup_pages_share_viewport_geometry_without_cross_page_height_coupling()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        static string[] ColumnWidths(XElement grid) => grid
            .Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Select(column => Attribute(column, "Width")).ToArray();

        var page0 = SingleElement(document, "ScrollViewer", "x:Name", "SetupPage0");
        var page1 = SingleElement(document, "ScrollViewer", "x:Name", "SetupPage1");
        var root0 = SingleElement(document, "Grid", "x:Name", "SetupPage0ViewportRoot");
        var root1 = SingleElement(document, "Grid", "x:Name", "SetupPage1ViewportRoot");
        var layout0 = SingleElement(document, "Grid", "x:Name", "SetupPage0LayoutGrid");
        var cards0 = SingleElement(document, "Grid", "x:Name", "SetupPage0CardsGrid");
        var cards1 = SingleElement(document, "Grid", "x:Name", "SetupPage1CardsGrid");

        Assert.Equal("{Binding ActualHeight, ElementName=SetupPageHost}", Attribute(root0, "MinHeight"));
        Assert.Equal("{Binding ActualHeight, ElementName=SetupPageHost}", Attribute(root1, "MinHeight"));
        Assert.Equal(string.Empty, Attribute(root0, "Margin"));
        Assert.Equal(string.Empty, Attribute(root1, "Margin"));
        Assert.Equal("{StaticResource ConceptStableVerticalGutterScrollViewerStyle}", Attribute(page0, "Style"));
        Assert.Equal("{StaticResource ConceptStableVerticalGutterScrollViewerStyle}", Attribute(page1, "Style"));
        Assert.Equal("22,12,8,10", Attribute(layout0, "Margin"));
        Assert.Equal("22,12,8,10", Attribute(cards1, "Margin"));
        Assert.Equal(["1*", "20", "1*"], ColumnWidths(cards0));
        Assert.Equal(["1*", "20", "1*"], ColumnWidths(cards1));
        Assert.DoesNotContain(page0.Elements(), element => element.Name.LocalName == "StackPanel");
        Assert.DoesNotContain("SyncSupplementaryPageCardHeights", DialogSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SetupPage0ProjectCard.ActualHeight", RepositorySource.Read(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]), StringComparison.Ordinal);
        Assert.Single(page1.Descendants(), element => element.Name.LocalName == "ConceptSeriesFieldControl");
        Assert.Single(page1.Descendants(), element => element.Name.LocalName == "ConceptDescriptionFieldControl");
        Assert.Single(page1.Descendants(), element =>
            element.Name.LocalName == "ConceptMetadataFieldControl" && Attribute(element, "x:Name") == "SetupIsbnField");
    }

    [Fact]
    public void Paging_uses_one_two_page_host_and_one_translate_animation_path()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        var host = SingleElement(document, "Grid", "x:Name", "SetupPageHost");
        var page0 = SingleElement(document, "ScrollViewer", "x:Name", "SetupPage0");
        var page1 = SingleElement(document, "ScrollViewer", "x:Name", "SetupPage1");
        var previous = SingleElement(document, "Button", "x:Name", "PreviousSetupPageButton");
        var next = SingleElement(document, "Button", "x:Name", "NextSetupPageButton");
        var canNavigate = SourceContract.ExtractMember(DialogSource, "internal bool CanNavigateSetupPage(int direction)");
        var navigate = SourceContract.ExtractMember(DialogSource, "internal bool NavigateSetupPage(int direction)");
        var complete = SourceContract.ExtractMember(DialogSource, "private void CompleteSetupPageNavigation(");
        var bridge = RepositorySource.Read(["BookStitch", "Dialog", "ProjectSetupPageNavigationBridge.cs"]);
        var factory = SourceContract.ExtractMember(MainWindowSource, "private DiscProjectSetupDialog CreateProjectSetupDialog");

        Assert.Equal("True", Attribute(host, "ClipToBounds"));
        Assert.Equal(string.Empty, Attribute(page0, "Visibility"));
        Assert.Equal("Collapsed", Attribute(page1, "Visibility"));
        Assert.Equal("False", Attribute(page1, "IsHitTestVisible"));
        Assert.Equal("False", Attribute(page1, "Focusable"));
        Assert.Equal("PreviousSetupPageButton_Click", Attribute(previous, "Click"));
        Assert.Equal("NextSetupPageButton_Click", Attribute(next, "Click"));
        Assert.Contains("target is >= 0 and <= 1", canNavigate, StringComparison.Ordinal);
        Assert.Contains("!_isSetupPageAnimating", canNavigate, StringComparison.Ordinal);
        Assert.Contains("_isSetupPageAnimating = true;", navigate, StringComparison.Ordinal);
        Assert.Contains("ProjectSetupPageNavigationBridge.NotifyNavigationStateChanged();", navigate, StringComparison.Ordinal);
        Assert.Contains("new CubicEase { EasingMode = EasingMode.EaseOut }", navigate, StringComparison.Ordinal);
        Assert.Contains("TranslateTransform.XProperty", navigate, StringComparison.Ordinal);
        Assert.DoesNotContain("MarginProperty", navigate, StringComparison.Ordinal);
        Assert.DoesNotContain("WidthProperty", navigate, StringComparison.Ordinal);
        SourceContract.AssertInOrder(complete, "outgoing.Visibility = Visibility.Collapsed;", "incoming.IsHitTestVisible = true;", "_currentSetupPage = targetPage;", "_isSetupPageAnimating = false;");
        Assert.Contains("ProjectSetupPageNavigationBridge.NotifyNavigationStateChanged();", complete, StringComparison.Ordinal);
        Assert.Contains("WeakReference<DiscProjectSetupDialog>", bridge, StringComparison.Ordinal);
        Assert.Contains("public static event EventHandler? StateChanged;", bridge, StringComparison.Ordinal);
        Assert.Contains("AnimationTimingSettingsService", factory, StringComparison.Ordinal);
        Assert.Contains("GetProjectSetupPageMilliseconds(_debugSettings)", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void Album_unbind_callback_only_opens_the_empty_editable_minimal_presentation()
    {
        var constructor = SourceContract.ExtractMember(DialogSource, "public DiscProjectSetupDialog(");
        var callback = SourceContract.ExtractMember(DialogSource, "private void OnTitleAlbumBindingChanged()");

        Assert.Contains("titleAlbumBindingChanged: OnTitleAlbumBindingChanged", constructor, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            callback,
            "if (_metadataFields.TitleAlbumBinding.IsActive)",
            "Dispatcher.BeginInvoke",
            "_metadataFields.Album.CanEditValues",
            "_metadataFields.Album.Values.Count == 0",
            "SetupAlbumField.TryOpenMinimalEditor();");
        Assert.DoesNotContain("SetValue", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("SetIsEnabled", callback, StringComparison.Ordinal);
    }

    [Fact]
    public void Sort_tracks_resolves_active_drafts_before_returning_control()
    {
        var sort = SourceContract.ExtractMember(DialogSource, "private void SortTracksButton_Click");

        SourceContract.AssertInOrder(
            sort,
            "CaptureResult(validateForStart: false);",
            "TryResolveSetupDraftsBeforeLeaving(SetupDraftLeaveIntent.Exit)",
            "_setupDraftExitApproved = true;",
            "DialogResult = false;");
    }

    [Fact]
    public void Setup_page2_uses_canonical_shared_fields_and_complete_draft_collector()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        var specialControl = RepositorySource.LoadXaml(["BookStitch", "Controls", "ConceptSpecialMetadataFieldControl.xaml"]);
        var series = SingleElement(document, "ConceptSeriesFieldControl", "x:Name", "SetupSeriesField");
        var description = SingleElement(document, "ConceptDescriptionFieldControl", "x:Name", "SetupDescriptionField");
        var abridgedStatus = SingleElement(document, "ConceptSpecialMetadataFieldControl", "x:Name", "SetupAbridgedStatusField");
        var workType = SingleElement(document, "ConceptSpecialMetadataFieldControl", "x:Name", "SetupWorkTypeField");
        var year = SingleElement(document, "ConceptSpecialMetadataFieldControl", "x:Name", "SetupYearField");
        var isbn = SingleElement(document, "ConceptMetadataFieldControl", "x:Name", "SetupIsbnField");
        var yearIsbnHost = SingleElement(document, "Grid", "x:Name", "SetupYearIsbnFieldsGrid");
        var page1 = SingleElement(document, "ScrollViewer", "x:Name", "SetupPage1");

        static string[] ColumnWidths(XElement grid) => grid
            .Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Select(column => Attribute(column, "Width")).ToArray();

        Assert.Contains(series, page1.Descendants());
        Assert.Contains(description, page1.Descendants());
        var sharedSeries = RepositorySource.LoadXaml(["BookStitch", "Controls", "ConceptSeriesFieldControl.xaml"]);
        var sharedSeriesHost = SingleElement(sharedSeries, "Grid", "x:Name", "SeriesEditorRowHost");
        Assert.Equal(["*", "{Binding ColumnGap, RelativeSource={RelativeSource AncestorType={x:Type controls:ConceptSeriesFieldControl}}}", "*"], ColumnWidths(sharedSeriesHost));
        Assert.Equal("Stretch", Attribute(description, "PresentationMode"));
        Assert.Equal(
            ["SetupAbridgedStatusField", "SetupWorkTypeField", "SetupYearField"],
            page1.Descendants()
                .Where(element => element.Name.LocalName == "ConceptSpecialMetadataFieldControl")
                .Select(element => Attribute(element, "x:Name")));
        Assert.NotEqual("Minimal", Attribute(isbn, "PresentationMode"));
        Assert.Equal("True", Attribute(isbn, "UseFullWidth"));

        Assert.Same(abridgedStatus.Parent, workType.Parent);
        Assert.Equal(["1*", "12", "1*"], ColumnWidths(Assert.IsType<XElement>(abridgedStatus.Parent)));
        Assert.Equal("0", Attribute(abridgedStatus, "Grid.Column"));
        Assert.Equal("2", Attribute(workType, "Grid.Column"));

        Assert.Contains(year, yearIsbnHost.Descendants());
        Assert.Contains(isbn, yearIsbnHost.Descendants());
        Assert.Equal(["1*", "12", "1*"], ColumnWidths(yearIsbnHost));
        Assert.Equal("0", Attribute(year, "Grid.Column"));
        Assert.Equal("2", Attribute(isbn, "Grid.Column"));
        Assert.Equal(string.Empty, Attribute(isbn, "Grid.Row"));
        Assert.Equal("12", Attribute(series, "ColumnGap"));

        Assert.DoesNotContain(
            specialControl.Descendants(),
            element => Attribute(element, "x:Name") == "MinimalEditorToggleButton");
        var specialEditor = SingleElement(specialControl, "Border", "x:Name", "EditorBorder");
        Assert.Contains("IsEditorVisible", Attribute(specialEditor, "Visibility"), StringComparison.Ordinal);
        Assert.Contains(
            specialControl.Descendants(),
            element => element.Name.LocalName == "TextBlock" &&
                       Attribute(element, "Text").Contains("OriginLabel", StringComparison.Ordinal));
        var yearWatermark = SingleElement(specialControl, "TextBlock", "x:Name", "YearWatermark");
        Assert.Equal("{x:Static services:MetadataDateParser.YearWatermark}", Attribute(yearWatermark, "Text"));
        var watermarkContract = string.Join(" ", yearWatermark.DescendantsAndSelf().Attributes().Select(attribute => attribute.Value));
        Assert.Contains("IsYear", watermarkContract, StringComparison.Ordinal);
        Assert.Contains("YearEditor", watermarkContract, StringComparison.Ordinal);

        var drafts = SourceContract.ExtractMember(DialogSource, "private SetupDraftParticipant[] GetActiveSetupDraftParticipants()");
        Assert.Contains("_seriesFieldState.HasSeriesNameDraft", drafts, StringComparison.Ordinal);
        Assert.Contains("_seriesFieldState.HasSeriesPartDraft", drafts, StringComparison.Ordinal);
        Assert.Contains("_descriptionFieldState.HasDraft", drafts, StringComparison.Ordinal);
        Assert.Contains("_metadataFields.Fields", drafts, StringComparison.Ordinal);
        Assert.Contains("_isbnField.HasDraft", drafts, StringComparison.Ordinal);
        Assert.Contains("_abridgedStatusFieldState.HasDraft", drafts, StringComparison.Ordinal);
        Assert.Contains("_workTypeFieldState.HasDraft", drafts, StringComparison.Ordinal);
        Assert.Contains("_yearFieldState.HasDraft", drafts, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_keeps_the_window_title_without_a_brand_logo_image()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        var header = Assert.Single(document.Descendants(), element => element.Name.LocalName == "LargeResizableWindowHeader");
        var titleContent = Assert.Single(header.Elements(), element =>
            element.Name.LocalName == "LargeResizableWindowHeader.TitleContent");
        var title = Assert.Single(titleContent.Descendants(), element => element.Name.LocalName == "TextBlock");

        Assert.DoesNotContain(header.Descendants(), element => element.Name.LocalName == "Image");
        Assert.Contains("Title", Attribute(title, "Text"), StringComparison.Ordinal);
        Assert.Contains("AncestorType=Window", Attribute(title, "Text"), StringComparison.Ordinal);
    }

    [Fact]
    public void Setup_and_metadata_share_the_compatibility_field_contract_without_setup_output_preview()
    {
        var setup = RepositorySource.LoadXaml(["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml"]);
        var metadata = RepositorySource.LoadXaml(["BookStitch", "Dialog", "MetadataDialog.xaml"]);
        var shared = RepositorySource.LoadXaml(["BookStitch", "Controls", "ConceptCompatibilityFieldControl.xaml"]);

        var setupField = SingleElement(setup, "ConceptCompatibilityFieldControl", "x:Name", "SetupCompatibilityField");
        var metadataField = SingleElement(metadata, "ConceptCompatibilityFieldControl", "x:Name", "ConceptCompatibilityField");
        var toggle = SingleElement(shared, "ToggleButton", "x:Name", "CompatibilityDescriptionToggle");
        var fieldText = SingleElement(shared, "TextBox", "x:Name", "CompatibilityFieldTextBox");
        var infoPoint = SingleElement(shared, "InfoPoint", "x:Name", "CompatibilityInfoPoint");

        Assert.NotNull(setupField);
        Assert.NotNull(metadataField);
        Assert.Equal("{DynamicResource InfoCompatibilityDescriptionTitle}", Attribute(infoPoint, "Title"));
        Assert.Equal("{DynamicResource InfoCompatibilityDescriptionBody}", Attribute(infoPoint, "Body"));
        Assert.Contains("IsCompatibilityDescriptionEnabled", Attribute(toggle, "IsChecked"), StringComparison.Ordinal);
        Assert.Contains("Mode=TwoWay", Attribute(toggle, "IsChecked"), StringComparison.Ordinal);
        Assert.Contains("CompatibilityFieldText", Attribute(fieldText, "Text"), StringComparison.Ordinal);
        Assert.Equal("True", Attribute(fieldText, "IsReadOnly"));
        Assert.DoesNotContain(setup.Root!.DescendantsAndSelf(), element => Attribute(element, "x:Name") == "ConceptCompatibilityDescriptionPreview");
        Assert.Contains(metadata.Root!.DescendantsAndSelf(), element => Attribute(element, "x:Name") == "ConceptCompatibilityDescriptionPreview");
    }

    [Fact]
    public void Setup_tab_order_follows_the_final_page1_sequence()
    {
        var tabOrder = SourceContract.ExtractMember(DialogSource, "private void ConfigureTabOrder(ProjectSetupModeProfile profile)");
        SourceContract.AssertInOrder(
            tabOrder,
            "DiscCountTextBox.TabIndex",
            "ParallelJobsTextBox.TabIndex",
            "OutputExtensionComboBox.TabIndex",
            "PresetComboBox.TabIndex",
            "AutoMergeCheckBox.TabIndex",
            "OutputFolderStateChipButton.TabIndex",
            "OpenOutputFolderButton.TabIndex",
            "FileNameTemplateTextBox.TabIndex");
    }

    private static XElement SingleElement(
        XDocument document,
        string elementName,
        string attributeName,
        string attributeValue) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == elementName &&
            Attribute(element, attributeName) == attributeValue);

    private static string Attribute(XElement element, string name)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return name.StartsWith("x:", StringComparison.Ordinal)
            ? element.Attribute(xaml + name[2..])?.Value ?? ""
            : element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value ?? "";
    }
}
