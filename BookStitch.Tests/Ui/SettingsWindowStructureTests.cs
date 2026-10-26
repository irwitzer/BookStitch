using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using BookStitch.Services;
using System.Globalization;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class SettingsWindowStructureTests
{
    private static readonly string[] SettingsControlNames =
    [
        "SettingsGeneralControl",
        "SettingsConversionControl",
        "SettingsLanguageControl",
        "SettingsProjectManagementControl",
        "SettingsAppearanceControl",
        "SettingsTagsChaptersControl",
        "SettingsDriveControl",
        "SettingsInfoControl",
        "SettingsFaqControl"
    ];

    [Fact]
    public void Conversion_settings_are_additional_views_of_the_existing_main_window_state()
    {
        var main = LoadXaml("MainWindow.xaml");
        var settings = LoadXaml("Controls", "SettingsConversionControl.xaml");
        foreach (var view in new[] { main, settings })
        {
            var preset = Single(view, "ComboBox", "Name", "ExportPresetComboBox");
            Assert.Equal("{Binding SelectedExportPreset, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}", Attr(preset, "SelectedItem"));
            var jobs = Single(view, "TextBox", "Name", "ParallelJobsTextBox");
            Assert.Equal("{Binding ParallelJobsInput, Mode=TwoWay, UpdateSourceTrigger=LostFocus}", Attr(jobs, "Text"));
            Assert.DoesNotContain(preset.Ancestors(), element => Attr(element, "Visibility") == "Collapsed");
            Assert.Equal(2, jobs.Parent!.Elements().Count(element => element.Name.LocalName == "RepeatButton"));
        }
        var code = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        Assert.Contains("conversionSettings.DataContext = this;", code);
        Assert.Contains("if (args.RemovedItems.Count > 0) ExportPresetComboBox_SelectionChanged(sender, args);", code);
        Assert.Contains("conversionSettings.ParallelJobsTextBox.LostFocus += ParallelJobsInput_LostFocus;", code);
        Assert.Contains("conversionSettings.IncreaseParallelJobsButton.Click += IncreaseParallelJobs_Click;", code);
        Assert.Contains("conversionSettings.DecreaseParallelJobsButton.Click += DecreaseParallelJobs_Click;", code);
        Assert.Contains("conversionSettings.OpenWorkFolderButton.Click += OpenWorkFolder_Click;", code);
        Assert.Contains("conversionSettings.OpenTaskManagerButton.Click += OpenTaskManager_Click;", code);
        Assert.DoesNotContain("ConversionSettingsParkingHost", code);
    }

    [Fact]
    public void Conversion_form_shares_columns_and_metadata_inset_with_full_width_wrapping_help()
    {
        var view = LoadXaml("Controls", "SettingsConversionControl.xaml");
        var grid = Single(view, "Grid", "Name", "ExportInputsPanel");
        Assert.Equal(2, grid.Element(grid.Name.Namespace + "Grid.ColumnDefinitions")!.Elements().Count());
        Assert.Equal("{StaticResource SettingsFormMargin}", Attr(grid, "Margin"));
        var metadata = LoadXaml("Controls", "SettingsTagsChaptersControl.xaml");
        var folderLabel = metadata.Descendants().Single(e => Attr(e, "Text") == "{services:LocalizedText Key=UiUnterordnerImAusgabeordner}");
        Assert.Contains(folderLabel.Ancestors(), e => Attr(e, "Margin") == Attr(grid, "Margin"));
        foreach (var name in new[] { "ExportPresetComboBox", "StandardSampleRateComboBox" })
        {
            var combo = Single(view, "ComboBox", "Name", name);
            Assert.Same(grid, combo.Parent);
            Assert.Equal("1", Attr(combo, "Grid.Column"));
            Assert.Equal("Left", Attr(combo, "HorizontalAlignment"));
            Assert.Empty(Attr(combo, "Width"));
            Assert.Equal("True", Attr(combo, "ComboBoxContentWidth.IsEnabled"));
        }
        var parallel = Single(view, "Grid", "Name", "ParallelJobsInputs");
        Assert.Same(grid, parallel.Parent);
        Assert.Equal("1", Attr(parallel, "Grid.Column"));
        Assert.Equal("{services:LocalizedText Key=Settings.Conversion.SampleRate.Label}",
            Attr(Single(view, "TextBlock", "Name", "SampleRateLabel"), "Text"));
        foreach (var name in new[] { "ExportPresetDescription", "ParallelJobsDescription", "SampleRateDescription" })
        {
            var description = Single(view, "TextBlock", "Name", name);
            Assert.Same(grid, description.Parent);
            Assert.Equal("2", Attr(description, "Grid.ColumnSpan"));
            Assert.Equal("{StaticResource ConversionDescriptionStyle}", Attr(description, "Style"));
        }
        var helpStyle = Single(view, "Style", "Key", "ConversionDescriptionStyle");
        Assert.Contains(helpStyle.Elements(), e => Attr(e, "Property") == "TextWrapping" && Attr(e, "Value") == "Wrap");
        var heading = Single(view, "TextBlock", "Name", "PassThroughHeading");
        Assert.Same(grid, heading.Parent);
        Assert.True(int.Parse(Attr(heading, "Grid.Row")) > int.Parse(Attr(Single(view, "TextBlock", "Name", "SampleRateDescription"), "Grid.Row")));
        Assert.Equal("Wrap", Attr(Single(view, "TextBlock", "Name", "PassThroughDescription"), "TextWrapping"));
        Assert.Equal("{services:LocalizedToolTip Key=MainWindow_ExportPreset_OpenProjectFolder}", Attr(Single(view, "Button", "Name", "OpenWorkFolderButton"), "ToolTip"));
        Assert.Equal("{services:LocalizedToolTip Key=MainWindow_Parallel_OpenTaskManager}", Attr(Single(view, "Button", "Name", "OpenTaskManagerButton"), "ToolTip"));
    }

    [Fact]
    public void Settings_shell_composes_one_owner_per_navigation_section_with_shared_navigation_and_footer()
    {
        var shell = LoadXaml("Windows", "SettingsWindow.xaml");

        Assert.Equal("{StaticResource LargeResizableWindowStyle}", Attr(shell.Root!, "Style"));
        Assert.DoesNotContain(shell.Descendants(), element => element.Name.LocalName is "TabControl" or "TabItem");

        var navigation = Single(shell, "ListBox", "Name", "SettingsNavigationList");
        Assert.Equal("{services:LocalizedText Key=Settings.Faq.NavigationTitle}", Attr(Single(shell, "ListBoxItem", "Tag", "FAQ"), "Content"));
        Assert.Equal(
            ["General", "Conversion", "Metadata", "Chapters", "ProjectManagement", "Drive", "Language", "Info", "FAQ"],
            shell.Descendants().Where(element => element.Name.LocalName == "ListBoxItem").Select(item => Attr(item, "Tag")));
        Assert.Equal("{StaticResource NavigationDialogListStyle}", Attr(navigation, "Style"));
        Assert.Contains(shell.Descendants(), element => Attr(element, "Text") == "{services:LocalizedText Key=UiKategorie}");
        Assert.Equal("{services:LocalizedText Key=UiKategorie}", Attr(navigation, "AutomationProperties.Name"));
        Assert.Equal("1030", Attr(shell.Root!, "Width"));
        Assert.Equal("1030", Attr(shell.Root!, "MinWidth"));
        Assert.Equal("600", Attr(shell.Root!, "MinHeight"));

        Assert.Single(shell.Descendants(), element => Attr(element, "Name") == "SettingsContentHost");
        Assert.DoesNotContain(shell.Descendants(), element => Attr(element, "Name") == "SettingsContextHost");
        Assert.Single(shell.Descendants(), element => element.Name.LocalName == "LargeResizableWindowFooter");

        var header = Assert.Single(shell.Descendants(), element => element.Name.LocalName == "LargeResizableWindowHeader");
        Assert.Equal("Close_Click", Attr(header, "CloseRequested"));
        Assert.Contains(header.Descendants(), element => Attr(element, "Command") == "{x:Static services:DeveloperToolsCommands.OpenDeveloperToolsWindow}");
        Assert.DoesNotContain(header.Descendants(), element => Attr(element, "Content") == "Schließen");
        Assert.DoesNotContain(header.Descendants(), element => Attr(element, "Style") == "{StaticResource GlobalSettingsGlyphStyle}");
        Assert.DoesNotContain(header.Descendants(), element => Attr(element, "Text") == "Globale Optionen für Export-Ablauf und Projektverwaltung.");

        foreach (var controlName in SettingsControlNames.Where(name => name != "SettingsAppearanceControl"))
            Assert.Single(shell.Descendants(), element => element.Name.LocalName == controlName);
        Assert.Single(LoadXaml("Controls", "SettingsGeneralControl.xaml").Descendants(),
            element => element.Name.LocalName == "SettingsAppearanceControl");

        var tags = LoadXaml("Controls", "SettingsTagsChaptersControl.xaml");
        Assert.Single(tags.Descendants(), element => Attr(element, "Name") == "MetadataSettingsSection");
        Assert.Single(tags.Descendants(), element => Attr(element, "Name") == "ChaptersSettingsSection");

        var code = RepositorySource.Read(["BookStitch", "Windows", "SettingsWindow.xaml.cs"]);
        Assert.DoesNotContain("SettingsService.Save", code, StringComparison.Ordinal);
        Assert.Contains("LegacyTagsChaptersSection", code, StringComparison.Ordinal);
        Assert.Contains("TagsChaptersSettingsControl.ShowSection", code, StringComparison.Ordinal);
        Assert.Contains("ProjectManagementSettingsControl.Initialize", code, StringComparison.Ordinal);
        Assert.Contains("sectionKey == \"ProjectManagement\"", code, StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            SourceContract.ExtractMember(code, "protected override void OnClosing"),
            "ProjectManagementSettingsControl.CommitPendingEdits();",
            "TagsChaptersSettingsControl.CommitPendingEdits();");
        Assert.Contains(
            "_settingsManager.Flush();",
            SourceContract.ExtractMember(code, "protected override void OnClosed"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Info_page_keeps_its_sidebar_image_passive_and_general_export_reuses_live_output_folder_callbacks()
    {
        var info = LoadXaml("Controls", "SettingsInfoControl.xaml");
        var image = info.Descendants().Single(element => element.Name.LocalName == "Image" && Attr(element, "Name") == "InfoSidebarImage");
        Assert.Equal("{DynamicResource InfoSquareImageSource}", Attr(image, "Source"));
        Assert.DoesNotContain(info.Descendants(), element => element.Name.LocalName == "Button" &&
            (Attr(element, "Click").Contains("Cover", StringComparison.Ordinal) || Attr(element, "Name").Contains("Cover", StringComparison.Ordinal)));

        var general = LoadXaml("Controls", "SettingsGeneralControl.xaml");
        Assert.Single(general.Descendants(), element => Attr(element, "Name") == "OutputFolderPathTextBlock");
        var choose = Single(general, "Button", "Name", "ChooseOutputFolderPathButton");
        Assert.Equal("ChooseOutputFolder_Click", Attr(choose, "Click"));

        var generalCode = RepositorySource.Read(["BookStitch", "Controls", "SettingsGeneralControl.xaml.cs"]);
        Assert.Contains("Func<string> getOutputFolder", generalCode, StringComparison.Ordinal);
        Assert.Contains("Action<Window> chooseOutputFolder", generalCode, StringComparison.Ordinal);
        Assert.Contains("INotifyPropertyChanged outputFolderChangeSource", generalCode, StringComparison.Ordinal);
        Assert.Contains("_chooseOutputFolder!(owner);", generalCode, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenFolderDialog", generalCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.OutputFolder =", generalCode, StringComparison.Ordinal);

        var settingsWindow = RepositorySource.Read(["BookStitch", "Windows", "SettingsWindow.xaml.cs"]);
        SourceContract.AssertInOrder(
            settingsWindow,
            "getOutputFolder,",
            "chooseOutputFolder,",
            "outputFolderChangeSource");

        var main = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        Assert.Contains("getOutputFolder: () => OutputFolder", main, StringComparison.Ordinal);
        Assert.Contains("chooseOutputFolder: owner => ChooseOutputFolder(owner)", main, StringComparison.Ordinal);
        Assert.Contains("private void ChooseOutputFolder(Window owner)", main, StringComparison.Ordinal);
        Assert.Contains("outputFolderChangeSource: this", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_scroll_host_uses_full_width_and_metadata_editors_remain_bounded()
    {
        var shell = LoadXaml("Windows", "SettingsWindow.xaml");
        var contentHost = Single(shell, "Grid", "Name", "SettingsContentHost");
        Assert.DoesNotContain(contentHost.Descendants(), element => element.Name.LocalName == "Setter" &&
            Attr(element, "Property") == "MaxWidth");
        Assert.Contains(contentHost.Descendants(), element => element.Name.LocalName == "Setter" &&
            Attr(element, "Property") == "HorizontalAlignment" && Attr(element, "Value") == "Stretch");
        Assert.Equal("0", Attr(contentHost.Parent!, "Padding"));

        var tags = LoadXaml("Controls", "SettingsTagsChaptersControl.xaml");
        var fileName = Single(tags, "ComboBox", "Name", "FileNameTemplateComboBox");
        Assert.Equal("Left", Attr(fileName, "HorizontalAlignment"));
        Assert.Equal("360", Attr(fileName, "MaxWidth"));
        Assert.Equal("True", Attr(fileName, "ComboBoxContentWidth.IsEnabled"));
        Assert.Equal("Left", Attr(Single(tags, "ComboBox", "Name", "OutputExtensionComboBox"), "HorizontalAlignment"));
        Assert.Equal("Left", Attr(Single(tags, "ComboBox", "Name", "CoverWarningThresholdComboBox"), "HorizontalAlignment"));
    }

    [Fact]
    public void Large_resizable_window_chrome_is_centralized_and_used_by_the_requested_large_surfaces()
    {
        var app = LoadXaml("App.xaml");
        Assert.Contains(app.Descendants(), element =>
            element.Name.LocalName == "ResourceDictionary" &&
            Attr(element, "Source") == "/Themes/WindowChromeStyles.xaml");
        Assert.Contains(app.Descendants(), element =>
            element.Name.LocalName == "ResourceDictionary" &&
            Attr(element, "Source") == "/Themes/NavigationDialogStyles.xaml");

        var chrome = LoadXaml("Themes", "WindowChromeStyles.xaml");
        var windowStyle = Single(chrome, "Style", "Key", "LargeResizableWindowStyle");
        Assert.Equal("{x:Type Window}", Attr(windowStyle, "TargetType"));
        Assert.Contains(windowStyle.Descendants(), element =>
            element.Name.LocalName == "WindowChrome" &&
            Attr(element, "GlassFrameThickness") == "0");
        Assert.Contains(windowStyle.Descendants(), element =>
            element.Name.LocalName == "ResizeGrip" &&
            Attr(element, "Style") == "{StaticResource LargeResizableWindowResizeGripStyle}");

        var headerStyle = chrome.Descendants().Single(element =>
            element.Name.LocalName == "Style" && Attr(element, "TargetType") == "{x:Type controls:LargeResizableWindowHeader}");
        Assert.Contains(headerStyle.Elements(), element =>
            element.Name.LocalName == "Setter" && Attr(element, "Property") == "Height" && Attr(element, "Value") == "46");
        foreach (var partName in new[] { "PART_MinimizeButton", "PART_MaximizeRestoreButton", "PART_CloseButton" })
            Assert.Contains(headerStyle.Descendants(), element => Attr(element, "Name") == partName);
        var headerCode = RepositorySource.Read(["BookStitch", "Controls", "LargeResizableWindowHeader.xaml.cs"]);
        Assert.Contains("nameof(ShowMinimize)", headerCode, StringComparison.Ordinal);
        Assert.Contains("new PropertyMetadata(false)", headerCode, StringComparison.Ordinal);
        Assert.Contains(chrome.Descendants(), element => element.Name.LocalName == "Trigger" &&
            Attr(element, "Property") == "ShowMinimize" && Attr(element, "Value") == "False");

        var titleStyle = Single(chrome, "Style", "Key", "LargeResizableWindowHeaderTitleTextStyle");
        Assert.Contains(titleStyle.Elements(), element => element.Name.LocalName == "Setter" && Attr(element, "Property") == "FontSize" && Attr(element, "Value") == "18");
        Assert.Contains(titleStyle.Elements(), element => element.Name.LocalName == "Setter" && Attr(element, "Property") == "FontWeight" && Attr(element, "Value") == "SemiBold");
        Assert.Contains(titleStyle.Elements(), element => element.Name.LocalName == "Setter" && Attr(element, "Property") == "Foreground" && Attr(element, "Value") == "{DynamicResource AccentForegroundBrush}");

        var captionStyle = Single(chrome, "Style", "Key", "LargeResizableWindowCaptionButtonStyle");
        var captionGlyph = Single(captionStyle.Document!, "TextBlock", "Name", "CaptionGlyph");
        Assert.Equal("{TemplateBinding Content}", Attr(captionGlyph, "Text"));
        Assert.Contains(captionStyle.Descendants(), element =>
            element.Name.LocalName == "Setter" && Attr(element, "TargetName") == "CaptionGlyph" &&
            Attr(element, "Property") == "Foreground" && Attr(element, "Value") == "{DynamicResource AccentForegroundBrush}");

        foreach (var path in new[]
                 {
                     new[] { "Windows", "SettingsWindow.xaml" },
                     new[] { "Windows", "DeveloperToolsWindow.xaml" },
                     new[] { "Dialog", "DiscProjectSetupDialog.xaml" },
                     new[] { "Dialog", "ResumeProjectDialog.xaml" },
                     new[] { "Dialog", "DialogPreviewWindow.xaml" },
                     new[] { "Dialog", "MetadataDialog.xaml" }
                 })
        {
            var document = LoadXaml(path);
            Assert.Equal("{StaticResource LargeResizableWindowStyle}", Attr(document.Root!, "Style"));
            Assert.DoesNotContain(document.Root!.Elements(), element => element.Name.LocalName == "WindowChrome.WindowChrome");
            Assert.Empty(Attr(document.Root!, "WindowStyle"));
            Assert.Empty(Attr(document.Root!, "AllowsTransparency"));
            Assert.Single(document.Descendants(), element => element.Name.LocalName == "LargeResizableWindowHeader");
        }
    }

    [Fact]
    public void Settings_surfaces_keep_internal_format_out_and_use_the_shared_inset_boundary()
    {
        foreach (var controlName in SettingsControlNames)
        {
            var document = LoadXaml("Controls", controlName + ".xaml");
            var text = document.ToString(SaveOptions.DisableFormatting);
            Assert.DoesNotContain("AudioDiscWorkingFormat", text, StringComparison.Ordinal);

            if (controlName == "SettingsProjectManagementControl")
            {
                Assert.Equal("Disabled", Attr(Single(document, "ScrollViewer", "Name", "IntegrityRetentionScrollViewer"), "HorizontalScrollBarVisibility"));
                Assert.Single(document.Root!.Elements(), element =>
                    element.Name.LocalName == "Grid" && Attr(element, "Margin") == "16,16,14,16");
            }
            else if (controlName == "SettingsFaqControl")
            {
                var answer = Single(document, "ScrollViewer", "Name", "AnswerScrollViewer");
                Assert.Equal("{StaticResource ConceptStableVerticalGutterScrollViewerStyle}", Attr(answer, "Style"));
                Assert.DoesNotContain(answer.Descendants(), element => Attr(element, "Name") == "QuestionsComboBox");
            }
            else if (controlName == "SettingsAppearanceControl")
            {
                Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "ScrollViewer");
                Assert.Single(document.Root!.Elements(), element => element.Name.LocalName == "StackPanel");
            }
            else
            {
                var scrollViewer = Assert.Single(document.Root!.Elements(), element =>
                    element.Name.LocalName == "ScrollViewer" &&
                    Attr(element, "Style") == "{StaticResource DialogInsetScrollViewerStyle}");
                Assert.Same(document.Root, scrollViewer.Parent);
            }
        }
    }

    [Fact]
    public void Global_boolean_settings_share_the_central_toggle_style_and_keep_their_existing_owners()
    {
        var general = LoadXaml("Controls", "SettingsGeneralControl.xaml");
        var tags = LoadXaml("Controls", "SettingsTagsChaptersControl.xaml");
        var drive = LoadXaml("Controls", "SettingsDriveControl.xaml");

        foreach (var (document, controlName, content) in new[]
                 {
                     (general, "MergeAutomaticallyCheckBox", "{services:LocalizedText Key=UiAutoMerge}"),
                     (general, "KeepComputerAwakeCheckBox", "{services:LocalizedText Key=UiComputerWachhalten}"),
                     (tags, "OverwriteWithoutAskingCheckBox", "{services:LocalizedText Key=UiZieldateiUeberschreiben}"),
                     (tags, "RememberLastCoverPickerFolderCheckBox", "{services:LocalizedText Key=UiCoverOrdnerMerken}"),
                     (drive, "ExperimentalDriveRoundEnabledCheckBox", "{services:LocalizedText Key=UiDriveCircleFuerCDProjekteAktivieren}")
                 })
        {
            var toggle = Single(document, "CheckBox", "Name", controlName);
            Assert.Equal(content, Attr(toggle, "Content"));
            Assert.Equal("{StaticResource SettingsToggleStyle}", Attr(toggle, "Style"));
        }

        Assert.DoesNotContain(general.Descendants(), element => Attr(element, "Name") == "OverwriteWithoutAskingCheckBox");

        var generalCode = RepositorySource.Read(["BookStitch", "Controls", "SettingsGeneralControl.xaml.cs"]);
        var tagsCode = RepositorySource.Read(["BookStitch", "Controls", "SettingsTagsChaptersControl.xaml.cs"]);
        Assert.DoesNotContain("OverwriteWithoutAskingCheckBox", generalCode, StringComparison.Ordinal);
        Assert.Contains(
            "OverwriteWithoutAskingCheckBox.IsChecked = Settings.OverwriteFinalOutputWithoutAsking;",
            tagsCode,
            StringComparison.Ordinal);
        SourceContract.AssertInOrder(
            SourceContract.ExtractMember(tagsCode, "private void OverwriteWithoutAskingCheckBox_Changed"),
            "Settings.OverwriteFinalOutputWithoutAsking = OverwriteWithoutAskingCheckBox.IsChecked == true;",
            "_settingsManager!.SaveImmediate();");

        var overwriteInfo = Single(tags, "InfoPoint", "Name", "OverwriteWithoutAskingInfoPoint");
        Assert.Equal("{DynamicResource InfoOverwriteFinalOutputTitle}", Attr(overwriteInfo, "Title"));
        Assert.Equal("{DynamicResource InfoOverwriteFinalOutputBody}", Attr(overwriteInfo, "Body"));
        var rememberInfo = Single(tags, "InfoPoint", "Name", "RememberLastCoverPickerFolderInfoPoint");
        Assert.Equal("{DynamicResource InfoRememberCoverFolderTitle}", Attr(rememberInfo, "Title"));
        Assert.Equal("{DynamicResource InfoRememberCoverFolderBody}", Attr(rememberInfo, "Body"));

        Assert.Equal(
            "Aktiviert: Eine bereits vorhandene Zieldatei wird ohne Rückfrage überschrieben.\nDeaktiviert: Ist die Zieldatei bereits vorhanden, fragt BookStitch nach einer Umbenennung.",
            LocalizationText.Get("InfoOverwriteFinalOutputBody", CultureInfo.GetCultureInfo("de-DE")).Replace("\r\n", "\n"));
        Assert.Equal(
            "Aktiviert: BookStitch merkt sich den zuletzt manuell verwendeten Ordner für die Cover-Auswahl.\nDeaktiviert: Die Cover-Auswahl startet im aktuellen Projekt- bzw. Quellordner.",
            LocalizationText.Get("InfoRememberCoverFolderBody", CultureInfo.GetCultureInfo("de-DE")).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Settings_toggle_and_retention_styles_are_centralized_in_the_appearance_theme()
    {
        var appearance = LoadXaml("Themes", "AppearanceTheme.xaml");
        var toggleStyle = Single(appearance, "Style", "Key", "SettingsToggleStyle");
        var retentionStyle = Single(appearance, "Style", "Key", "SettingsProjectRetentionComboBoxStyle");
        var toggleLabel = Single(toggleStyle.Document!, "TextBlock", "Name", "ToggleLabel");
        var checkedTrigger = toggleStyle
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Trigger" &&
                Attr(element, "Property") == "IsChecked" &&
                Attr(element, "Value") == "True");

        Assert.Equal("CheckBox", Attr(toggleStyle, "TargetType"));
        Assert.Equal("Strikethrough", Attr(toggleLabel, "TextDecorations"));
        Assert.Contains(checkedTrigger.Descendants(), element =>
            element.Name.LocalName == "Setter" &&
            Attr(element, "TargetName") == "ToggleLabel" &&
            Attr(element, "Property") == "TextDecorations" &&
            Attr(element, "Value") == "{x:Null}");
        Assert.Equal("{StaticResource AppearanceCompactComboBoxStyle}", Attr(retentionStyle, "BasedOn"));

        var general = LoadXaml("Controls", "SettingsGeneralControl.xaml");
        var projectManagement = LoadXaml("Controls", "SettingsProjectManagementControl.xaml");
        var retention = Single(projectManagement, "ComboBox", "Name", "ProjectRetentionDaysComboBox");
        Assert.Equal("{StaticResource SettingsProjectRetentionComboBoxStyle}", Attr(retention, "Style"));
        Assert.DoesNotContain(general.Descendants(), element => Attr(element, "Name") == "ProjectRetentionDaysComboBox");

        foreach (var key in new[] { "SettingsPrimaryButtonStyle", "SettingsSecondaryButtonStyle", "SettingsDestructiveButtonStyle" })
            Assert.Single(appearance.Descendants(), element => element.Name.LocalName == "Style" && Attr(element, "Key") == key);
        Assert.Equal("{StaticResource SettingsSecondaryButtonStyle}",
            Attr(Single(projectManagement, "Button", "Click", "DeleteOldProjectsNow_Click"), "Style"));
        Assert.Equal("{StaticResource SettingsDestructiveButtonStyle}",
            Attr(Single(projectManagement, "Button", "Click", "DeleteAllProjects_Click"), "Style"));
    }

    [Fact]
    public void Chapter_editor_owner_keeps_compact_preview_and_persist_before_notify_contract()
    {
        var document = LoadXaml("Controls", "SettingsTagsChaptersControl.xaml");
        var sourceSelector = Single(document, "ComboBox", "Name", "ChapterPreviewDataSourceComboBox");
        var preview = Single(document, "TextBlock", "Name", "ChapterPreviewTextBlock");

        Assert.Equal("ChapterSettingsComboBox_Changed", Attr(sourceSelector, "SelectionChanged"));
        foreach (var name in new[] { "ChapterModeComboBox", "ChapterNumberingStyleComboBox", "ChapterPreviewDataSourceComboBox", "ChapterSlot1ComboBox", "ChapterSlot2ComboBox", "ChapterSlot3ComboBox" })
        {
            var combo = Single(document, "ComboBox", "Name", name);
            Assert.Equal("{StaticResource AppearanceCompactComboBoxStyle}", Attr(combo, "Style"));
            Assert.Empty(Attr(combo, "Height"));
        }
        Assert.Equal("CharacterEllipsis", Attr(preview, "TextTrimming"));
        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName == "ListBox" && Attr(element, "Name") == "ChapterPreviewListBox");

        var code = RepositorySource.Read(["BookStitch", "Controls", "SettingsTagsChaptersControl.xaml.cs"]);
        SourceContract.AssertInOrder(
            SourceContract.ExtractMember(code, "private void SaveChapterSettingsFromControls"),
            "var persisted = _settingsManager!.SaveImmediate();",
            "if (persisted)",
            "_chapterSettingsChanged?.Invoke();");
    }

    [Fact]
    public void DriveCircle_owner_uses_the_dedicated_slot_interaction_and_shared_refresh_surface()
    {
        var document = LoadXaml("Controls", "SettingsDriveControl.xaml");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TextBlock" && Attr(element, "Text") == "{services:LocalizedText Key=UiDriveCircle}");
        Assert.DoesNotContain("experimentelles Feature", document.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ungetestet", document.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Laufwerksrunde", document.ToString(), StringComparison.Ordinal);

        foreach (var controlName in new[] { "ExperimentalDriveRoundEnabledCheckBox", "DiscDriveSlotPanel", "DriveRoundRefreshButton" })
            Assert.Single(document.Descendants(), element => Attr(element, "Name") == controlName);

        var host = Single(document, "StackPanel", "Name", "DiscDriveSlotPanel");
        Assert.Equal("Vertical", Attr(host, "Orientation"));
        Assert.Equal("True", Attr(host, "AllowDrop"));
        Assert.Equal("DiscDriveSlotPanel_Drop", Attr(host, "Drop"));
        Assert.DoesNotContain(document.Descendants(), element => Attr(element, "Name") is "DriveRoundLeftButton" or "DriveRoundRightButton");

        var slot = LoadXaml("Controls", "DriveCircleSlot.xaml");
        Assert.Equal("{x:Static services:DiscDriveConfigurationService.MaximumUserNameLength}",
            Attr(Single(slot, "TextBox", "Name", "NameEditor"), "MaxLength"));
        var editor = Single(slot, "TextBox", "Name", "NameEditor");
        Assert.Equal("{DynamicResource InputBackgroundBrush}", Attr(editor, "Background"));
        Assert.Equal("{DynamicResource MainTextBrush}", Attr(editor, "Foreground"));
        Assert.Equal("{DynamicResource MainTextBrush}", Attr(editor, "CaretBrush"));
        var appearance = LoadXaml("Themes", "AppearanceTheme.xaml").ToString(SaveOptions.DisableFormatting);
        Assert.DoesNotContain("DriveCircleActiveBackgroundBrush", appearance, StringComparison.Ordinal);
        Assert.DoesNotContain("DriveCircleHoverBackgroundBrush", appearance, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonBackgroundBrush}", appearance, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonHoverBackgroundBrush}", appearance, StringComparison.Ordinal);
        var driveInfoPoint = Assert.Single(slot.Descendants(), element => element.Name.LocalName == "InfoPoint");
        Assert.Equal("OnPrimary", Attr(driveInfoPoint, "Variant"));
        Assert.Equal("Slot_PreviewMouseLeftButtonDown", Attr(slot.Root!, "PreviewMouseLeftButtonDown"));
        Assert.Equal("Slot_PreviewMouseMove", Attr(slot.Root!, "PreviewMouseMove"));
        Assert.Equal("Slot_PreviewMouseRightButtonDown", Attr(slot.Root!, "PreviewMouseRightButtonDown"));

        var slotCode = RepositorySource.Read(["BookStitch", "Controls", "DriveCircleSlot.xaml.cs"]);
        Assert.Contains("SystemParameters.MinimumHorizontalDragDistance", slotCode, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.MinimumVerticalDragDistance", slotCode, StringComparison.Ordinal);
        Assert.Contains("Header = LocalizationText.Get(\"Settings.Drive.Rename\")", slotCode, StringComparison.Ordinal);
        Assert.Contains("IsDriveEnabled ? \"Settings.Drive.Disable\" : \"Settings.Drive.Enable\"", slotCode, StringComparison.Ordinal);
        SourceContract.AssertInOrder(slotCode, "menu.Items.Add(rename);", "menu.Items.Add(new Separator", "menu.Items.Add(toggle);");

        var code = RepositorySource.Read(["BookStitch", "Controls", "SettingsDriveControl.xaml.cs"]);
        Assert.Contains("DragDrop.DoDragDrop", code, StringComparison.Ordinal);
        Assert.Contains("MoveDriveToIndex", code, StringComparison.Ordinal);
        Assert.Contains("RenameDrive", code, StringComparison.Ordinal);
        Assert.Contains("configuration.StableId", code, StringComparison.Ordinal);

        var refresh = Single(document, "Button", "Name", "DriveRoundRefreshButton");
        Assert.Equal("{StaticResource SettingsSecondaryButtonStyle}", Attr(refresh, "Style"));
        Assert.Equal("{services:LocalizedText Key=Settings.Drive.Reset}", Attr(refresh, "Content"));
        var update = Single(document, "Button", "Name", "DriveRoundUpdateButton");
        Assert.Equal("{services:LocalizedText Key=Settings.Drive.Refresh}", Attr(update, "Content"));
        var updateHandler = SourceContract.ExtractMember(code, "private async void DriveRoundUpdate_Click(");
        Assert.Contains("RefreshDriveCircleListAsync(saveSettings: true)", updateHandler);
        Assert.DoesNotContain("reset: true", updateHandler);
    }

    [Fact]
    public void Settings_category_shells_and_custom_chapter_rule_do_not_add_decorative_outer_borders()
    {
        foreach (var controlName in SettingsControlNames)
        {
            var document = LoadXaml("Controls", controlName + ".xaml");
            Assert.DoesNotContain(document.Root!.Elements(), element => element.Name.LocalName == "Border");
        }

        var tags = LoadXaml("Controls", "SettingsTagsChaptersControl.xaml");
        Assert.DoesNotContain(tags.Descendants(), element => Attr(element, "Name") == "ChapterCustomSettingsBorder");
        Assert.Single(tags.Descendants(), element => Attr(element, "Name") == "ChapterCustomSettingsPanel");
    }

    [Fact]
    public void Project_management_hosts_shared_preview_above_retention_without_load_action()
    {
        var document = LoadXaml("Controls", "SettingsProjectManagementControl.xaml");
        var combo = Single(document, "ComboBox", "Name", "ProjectsComboBox");
        var retention = Single(document, "ComboBox", "Name", "ProjectRetentionDaysComboBox");
        Assert.True(combo.AncestorsAndSelf().Any());
        Assert.Contains(retention.Elements(), element => Attr(element, "Content") == "360");
        Assert.Contains(retention.Elements(), element => Attr(element, "Content") == "{services:LocalizedText Key=UiNie}");
        Assert.Single(document.Descendants(), element => Attr(element, "Name") == "DeleteSelectedProjectButton");
        Assert.DoesNotContain(document.Descendants(), element => Attr(element, "Content") is "{services:LocalizedText Key=UiProjektLaden}" or "{services:LocalizedText Key=UiProjektFortsetzen}");

        Assert.Equal("Grid", document.Root!.Elements().Single().Name.LocalName);
        var previewScroll = Single(document, "ScrollViewer", "Name", "ProjectPreviewScrollViewer");
        var previewBorder = previewScroll.Ancestors().First(element => element.Name.LocalName == "Border");
        Assert.Empty(Attr(previewBorder, "Height"));
        Assert.Equal("150", Attr(previewBorder, "MinHeight"));
        Assert.Equal("3", Attr(previewBorder, "Grid.Row"));
        Assert.Equal("Auto", Attr(previewScroll, "VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", Attr(Single(document, "ScrollViewer", "Name", "IntegrityRetentionScrollViewer"), "HorizontalScrollBarVisibility"));

        var code = RepositorySource.Read(["BookStitch", "Controls", "SettingsProjectManagementControl.xaml.cs"]);
        Assert.Contains("ProjectBrowserPresentationService", code, StringComparison.Ordinal);
        Assert.Contains("ProjectRetentionModes.Never", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Lifecycle_sensitive_handlers_are_attached_and_detached_by_their_ui_owners()
    {
        var general = RepositorySource.Read(["BookStitch", "Controls", "SettingsGeneralControl.xaml.cs"]);
        Assert.Contains("_ownerWindow.StateChanged += OwnerWindow_StateChanged;", general, StringComparison.Ordinal);
        Assert.Contains("_ownerWindow.StateChanged -= OwnerWindow_StateChanged;", general, StringComparison.Ordinal);
        Assert.Contains("_focusTestCancellation?.Cancel();", general, StringComparison.Ordinal);

        var tags = RepositorySource.Read(["BookStitch", "Controls", "SettingsTagsChaptersControl.xaml.cs"]);
        Assert.Contains("_coverPickerService.PropertyChanged += CoverPickerService_PropertyChanged;", tags, StringComparison.Ordinal);
        Assert.Contains("_coverPickerService.PropertyChanged -= CoverPickerService_PropertyChanged;", tags, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_owner_routes_repository_actions_to_their_canonical_urls()
    {
        var document = LoadXaml("Controls", "SettingsInfoControl.xaml");
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Button" && Attr(element, "Click") == "OpenGitHubProject_Click");
        Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Button" && Attr(element, "Click") == "OpenThirdPartyNotices_Click");

        var code = RepositorySource.Read(["BookStitch", "Controls", "SettingsInfoControl.xaml.cs"]);
        Assert.Contains("https://github.com/irwitzer/BookStitch", code, StringComparison.Ordinal);
        Assert.Contains("https://github.com/irwitzer/BookStitch/blob/main/THIRD_PARTY_NOTICES.md", code, StringComparison.Ordinal);
        Assert.Contains("OpenUrl(GitHubProjectUrl)", code, StringComparison.Ordinal);
        Assert.Contains("OpenUrl(ThirdPartyNoticesUrl)", code, StringComparison.Ordinal);
    }

    private static XDocument LoadXaml(params string[] relativePath) =>
        RepositorySource.LoadXaml(["BookStitch", .. relativePath]);

    private static XElement Single(
        XDocument document,
        string elementName,
        string attributeName,
        string value) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == elementName && Attr(element, attributeName) == value);

    private static string Attr(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value ?? string.Empty;
}
