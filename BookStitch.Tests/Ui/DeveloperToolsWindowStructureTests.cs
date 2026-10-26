using BookStitch.Tests.TestHelpers;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class DeveloperToolsWindowStructureTests
{
    [Fact]
    public void Debug_window_is_modeless_independent_and_has_long_lived_tabs()
    {
        var xaml = LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml");
        var code = LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml.cs");
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var settingsXaml = LoadText("BookStitch", "Windows", "SettingsWindow.xaml");
        var mainXaml = LoadText("BookStitch", "MainWindow.xaml");

        Assert.Contains("Title=\"{services:LocalizedText Key=Developer.BookStitchDeveloperTools}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Topmost=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation=\"Manual\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{services:LocalizedText Key=Developer.DeveloperTools}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource LargeResizableWindowStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<controls:LargeResizableWindowHeader", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource LargeResizableWindowHeaderTitleTextStyle}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinimizeRequested", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Minimize_Click", code, StringComparison.Ordinal);
        Assert.Contains("Content=\"{services:LocalizedText Key=DeveloperTools.Restart}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"RestartBookStitchButton_Click\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximizeRestore_Click", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DeveloperToolsWindow_StateChanged", code, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Status\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Appearance\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Tools\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Performance\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Chapters\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"Projects\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{services:LocalizedText Key=DeveloperTools.Navigation.Environment}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ChapterDebugTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=DeveloperTools.Navigation.Decisions", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MetadataDebugTextBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"DebugChapterModeComboBox\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ChapterSettingsEditorService", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_chapterSettingsChanged();", code, StringComparison.Ordinal);
        Assert.Contains("_getMetadataDebugSnapshot()", code, StringComparison.Ordinal);
        var comboStyle = XDocument.Parse(xaml).Descendants().Single(element =>
            element.Name.LocalName == "Style" && (string?)element.Attribute("TargetType") == "ComboBox");
        Assert.Equal("{StaticResource AppearanceComboBoxStyle}", (string?)comboStyle.Attribute("BasedOn"));
        Assert.Contains(comboStyle.Elements(), setter => (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "27");
        Assert.Equal(3, CountOccurrences(
            xaml,
            "Style=\"{StaticResource DialogInsetScrollViewerStyle}\""));
        Assert.Contains("x:Name=\"DeveloperNavigationList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("_getChapterDebugSnapshot()", code, StringComparison.Ordinal);
        Assert.Contains("CreateCurrentChapterDebugSnapshot", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ChapterAutoEngineService", code, StringComparison.Ordinal);
        Assert.Contains("DebugWindowPlacementService", code, StringComparison.Ordinal);
        Assert.Contains("DebugWindowPlacementTarget.DeveloperTools", code, StringComparison.Ordinal);
        Assert.Contains("DialogPlacementRememberCheckBox_Changed", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DeveloperToolsPlacementCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GlobalSettingsPlacementCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProjectSetupPlacementCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MetadataDialogPlacementCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ResumeProjectPlacementCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DebugWindowPlacementTarget.GlobalSettings", mainWindow, StringComparison.Ordinal);
        Assert.Contains("DebugWindowPlacementTarget.ProjectSetup", mainWindow, StringComparison.Ordinal);
        Assert.Contains("DebugWindowPlacementTarget.MetadataDialog", mainWindow, StringComparison.Ordinal);
        Assert.Contains("DebugWindowPlacementTarget.ResumeProject", mainWindow, StringComparison.Ordinal);
        Assert.Contains("developerToolsWindow.Show();", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("debugWindow.ShowDialog", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag=\"Developer\"", settingsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PipelineStateDebugVisibility", mainXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MainWindowMetricsTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ActiveWindowMetricsTextBlock\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowMetricsPresentationService", code, StringComparison.Ordinal);
        Assert.Contains("window is DeveloperToolsWindow", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Developer_tools_access_is_centralized_on_four_surfaces_without_keyboard_backdoor()
    {
        var mainWindowCode = LoadText("BookStitch", "MainWindow.xaml.cs");
        var commandCode = LoadText("BookStitch", "Services", "DeveloperToolsCommands.cs");
        var appCode = LoadText("BookStitch", "App.xaml.cs");
        var theme = LoadText("BookStitch", "Themes", "AppearanceTheme.xaml");

        Assert.Contains("internal void RestartDeveloperToolsWindowForModalAccess()", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("_developerToolsWindow.Close();", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("developerToolsWindow.Show();", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("developerToolsWindow.Activate();", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("DeveloperToolsCommands.Register();", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.F12", commandCode, StringComparison.Ordinal);
        Assert.Contains("handledEventsToo: true", commandCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Keyboard.PreviewKeyDownEvent", commandCode, StringComparison.Ordinal);
        Assert.Contains("new Action(mainWindow.RestartDeveloperToolsWindowForModalAccess)", commandCode, StringComparison.Ordinal);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var themeDocument = XDocument.Parse(theme);

        var developerToolsStyle = themeDocument.Descendants(presentation + "Style").Single(element =>
            (string?)element.Attribute(x + "Key") == "DeveloperToolsButtonStyle");
        Assert.Equal("{StaticResource UtilityIconButtonStyle}", (string?)developerToolsStyle.Attribute("BasedOn"));

        string? SetterValue(string property) => developerToolsStyle
            .Elements(presentation + "Setter")
            .Single(element => (string?)element.Attribute("Property") == property)
            .Attribute("Value")
            ?.Value;

        Assert.Equal("{StaticResource DeveloperToolsWrenchGeometry}", SetterValue("Content"));
        Assert.Equal("{StaticResource DeveloperToolsIconTemplate}", SetterValue("ContentTemplate"));
        Assert.Equal("{DynamicResource MutedTextBrush}", SetterValue("Foreground"));
        Assert.Equal("{services:LocalizedToolTip Key=DeveloperTools_OpenWindow}", SetterValue("ToolTip"));
        Assert.Equal("True", SetterValue("services:DeveloperToolsCommands.QuickMenuEnabled"));
        Assert.Contains("UIElement.MouseRightButtonUpEvent", commandCode, StringComparison.Ordinal);
        Assert.Contains("DeveloperToolsButton_MouseRightButtonUp", commandCode, StringComparison.Ordinal);
        Assert.Contains("GetQuickMenuEnabled(button)", commandCode, StringComparison.Ordinal);
        Assert.Contains("mainWindow.OpenDeveloperQuickMenu(button);", commandCode, StringComparison.Ordinal);
        Assert.Contains("Application.Current?.MainWindow is not BookStitch.MainWindow mainWindow", commandCode, StringComparison.Ordinal);
        Assert.DoesNotContain("new ContextMenu", commandCode, StringComparison.Ordinal);
        Assert.DoesNotContain(
            developerToolsStyle.Elements(presentation + "Setter"),
            element => (string?)element.Attribute("Property") == "FontFamily");

        var wrenchGeometry = themeDocument.Descendants(presentation + "Geometry").Single(element =>
            (string?)element.Attribute(x + "Key") == "DeveloperToolsWrenchGeometry");
        Assert.Contains("M 3936.508,456.492", wrenchGeometry.Value, StringComparison.Ordinal);
        Assert.Contains("L 927.868,2678.185", wrenchGeometry.Value, StringComparison.Ordinal);

        var iconTemplate = themeDocument.Descendants(presentation + "DataTemplate").Single(element =>
            (string?)element.Attribute(x + "Key") == "DeveloperToolsIconTemplate");
        var iconPath = iconTemplate.Descendants(presentation + "Path").Single();
        Assert.Equal("{StaticResource DeveloperToolsWrenchGeometry}", (string?)iconPath.Attribute("Data"));
        Assert.Equal(
            "{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type Button}}}",
            (string?)iconPath.Attribute("Fill"));
        Assert.DoesNotContain("#", (string?)iconPath.Attribute("Fill") ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("UiSymbolResources.DeveloperTools", theme, StringComparison.Ordinal);

        var surfacePaths = new[]
        {
            new[] { "BookStitch", "MainWindow.xaml" },
            new[] { "BookStitch", "Windows", "SettingsWindow.xaml" },
            new[] { "BookStitch", "Dialog", "MetadataDialog.xaml" },
            new[] { "BookStitch", "Dialog", "DiscProjectSetupDialog.xaml" }
        };

        foreach (var surfacePath in surfacePaths)
        {
            var surfaceDocument = XDocument.Parse(LoadText(surfacePath));
            var buttons = surfaceDocument.Descendants(presentation + "Button")
                .Where(element =>
                    (string?)element.Attribute("Style") == "{StaticResource DeveloperToolsButtonStyle}")
                .ToArray();

            Assert.NotEmpty(buttons);
            foreach (var button in buttons)
            {
                Assert.Equal(
                    "{x:Static services:DeveloperToolsCommands.OpenDeveloperToolsWindow}",
                    (string?)button.Attribute("Command"));
                Assert.Null(button.Attribute("Content"));
            }
        }

        foreach (var removedSurface in new[]
        {
            "ResumeProjectDialog.xaml", "DiscSourceSelectionDialog.xaml", "AddMoreDiscsDialog.xaml",
            "AppMessageDialog.xaml", "AppInputDialog.xaml", "BoxedDiscWaitDialog.xaml",
            "DialogPreviewWindow.xaml"
        })
            Assert.DoesNotContain("DeveloperToolsButtonStyle", LoadText("BookStitch", "Dialog", removedSurface), StringComparison.Ordinal);

        var debugWindow = LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml");
        Assert.DoesNotContain("DeveloperToolsButtonStyle", debugWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Debug_window_keeps_existing_developer_tool_entry_points()
    {
        var xaml = LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml");
        var code = LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml.cs");

        Assert.Contains("ResumeProjectPreview_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("ProjectSetupPreview_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewDiscSourceAnalysis_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("SimulatedDiscWaitPreview_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("HybridDiscTestPreview_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeveloperAudioDiscTest_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("PrivateGenreListCheckBox", xaml, StringComparison.Ordinal);
        Assert.Contains("BackgroundPresetComboBox", xaml, StringComparison.Ordinal);
        Assert.Contains("TrackGridRowHeightComboBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SubtleScrollBarsCheckBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Dezenter Scrollbalken", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UseSubtleScrollBars", code, StringComparison.Ordinal);
        Assert.Contains("_setBackgroundPreset(", code, StringComparison.Ordinal);
        Assert.Contains("_setTextColorPreset(", code, StringComparison.Ordinal);
        Assert.Contains("internal void SynchronizeAppearanceControls()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAndApplyAppearanceSettings", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Appearance_tab_exposes_persistent_six_level_track_grid_row_height_control()
    {
        var xaml = LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml");
        var code = LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml.cs");
        var settings = LoadText("BookStitch", "Models", "DebugSettings.cs");
        var service = LoadText("BookStitch", "Services", "TrackGridRowHeightService.cs");
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");

        Assert.Contains("x:Name=\"TrackGridRowHeightComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"TrackGridRowHeightComboBox_Changed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TrackGridRowHeightService.Options", code, StringComparison.Ordinal);
        Assert.Contains("_setTrackGridRowHeightLevel", code, StringComparison.Ordinal);
        Assert.Contains("TrackGridRowHeightLevel", settings, StringComparison.Ordinal);
        Assert.Contains("new(0, \"Appearance.RowHeight.VeryCompact\", 22)", service, StringComparison.Ordinal);
        Assert.Contains("new(5, \"Appearance.RowHeight.Maximum\", 32)", service, StringComparison.Ordinal);
        Assert.Contains("SetTrackGridRowHeightLevel", mainWindow, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(TrackGridRowHeight));", mainWindow, StringComparison.Ordinal);
    }


    [Fact]
    public void Appearance_and_chapter_tools_are_grouped_by_semantic_tab_and_setup_paging_uses_shared_bridge()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Windows", "DeveloperToolsWindow.xaml"]);
        var code = RepositorySource.Read(["BookStitch", "Windows", "DeveloperToolsWindow.xaml.cs"]);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement Named(string name) => document.Descendants().Single(element =>
            (string?)element.Attribute(x + "Name") == name);
        string OwningTabName(string controlName) =>
            (string?)Named(controlName).Ancestors().First(element => element.Attribute(x + "Name")?.Value.EndsWith("Section", StringComparison.Ordinal) == true).Attribute(x + "Name") ?? "";

        Assert.Equal("AppearanceSection", OwningTabName("MetadataAnimationMillisecondsTextBox"));
        Assert.Equal("AppearanceSection", OwningTabName("ProjectSetupPageAnimationMillisecondsTextBox"));
        Assert.Equal("AppearanceSection", OwningTabName("ProjectSetupPagePreviousButton"));
        Assert.Equal("AppearanceSection", OwningTabName("ProjectSetupPageNextButton"));
        Assert.Equal("AppearanceSection", OwningTabName("ForceShowFfmpegSetupButtonCheckBox"));
        Assert.Equal("ProjectsSection", OwningTabName("PrivateGenreListCheckBox"));
        Assert.DoesNotContain("Hauptfenster &amp; UI-Test", document.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);

        var previous = SourceContract.ExtractMember(code, "private void ProjectSetupPagePreviousButton_Click");
        var next = SourceContract.ExtractMember(code, "private void ProjectSetupPageNextButton_Click");
        var update = SourceContract.ExtractMember(code, "private void UpdateProjectSetupPagePreviewButtons()");
        var cleanup = SourceContract.ExtractMember(code, "private void Cleanup()");
        Assert.Contains("ProjectSetupPageNavigationBridge.TryNavigate(-1)", previous, StringComparison.Ordinal);
        Assert.Contains("ProjectSetupPageNavigationBridge.TryNavigate(1)", next, StringComparison.Ordinal);
        Assert.Contains("ProjectSetupPageNavigationBridge.CanNavigate(-1)", update, StringComparison.Ordinal);
        Assert.Contains("ProjectSetupPageNavigationBridge.CanNavigate(1)", update, StringComparison.Ordinal);
        Assert.Contains("ProjectSetupPageNavigationBridge.StateChanged -= ProjectSetupPageNavigationBridge_StateChanged", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void Projects_tab_keeps_next_start_selection_compact_and_restart_is_at_the_bottom_of_category_navigation()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "Windows", "DeveloperToolsWindow.xaml"]);
        var xaml = document.ToString(SaveOptions.DisableFormatting);
        var code = RepositorySource.Read(["BookStitch", "Windows", "DeveloperToolsWindow.xaml.cs"]);
        var mainWindowCode = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement Named(string name) => document.Descendants().Single(element =>
            (string?)element.Attribute(x + "Name") == name);

        var activeEnvironment = Named("ActiveProjectEnvironmentTextBlock");
        var compactHeader = Assert.IsType<XElement>(activeEnvironment.Parent);
        var headerLabel = compactHeader.Elements().Single(element =>
            element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{services:LocalizedText Key=Developer.ProjectEnvironment}");
        Assert.Equal("Horizontal", (string?)compactHeader.Attribute("Orientation"));
        Assert.Equal("20", (string?)headerLabel.Attribute("FontSize"));
        Assert.Equal("20", (string?)activeEnvironment.Attribute("FontSize"));

        Assert.Contains("x:Name=\"ProjectEnvironmentComboBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{services:LocalizedText Key=UiStandard}\" Tag=\"Production\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{services:LocalizedText Key=Developer.Development}\" Tag=\"Development\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{services:LocalizedText Key=Developer.Tester}\" Tag=\"XTester\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=Developer.ProjectEnvironmentForNextStart", xaml, StringComparison.Ordinal);
        Assert.Contains("LocalizationText.Get(\"Developer.EnvironmentNextStart\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Aktiv in diesem Programmstart", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectEnvironmentRestartHintTextBlock", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Wird nach dem Neustart verwendet.", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Jetzt neu starten", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateProjectEnvironmentRestartState", code, StringComparison.Ordinal);

        var restartButton = document.Descendants().Single(element =>
            element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Click") == "RestartBookStitchButton_Click");
        Assert.Equal("{services:LocalizedText Key=DeveloperTools.Restart}", (string?)restartButton.Attribute("Content"));
        Assert.Contains(restartButton.Ancestors(), ancestor => ancestor.Name.LocalName == "Border" &&
            (string?)ancestor.Attribute("Style") == "{StaticResource NavigationDialogNavigationHostStyle}");
        Assert.Equal("{StaticResource SecondaryCompactButtonStyle}", (string?)restartButton.Attribute("Style"));
        Assert.Equal("{services:LocalizedText Key=DeveloperTools.Restart}", restartButton.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name").Value);
        Assert.DoesNotContain(restartButton.Ancestors(), ancestor =>
            ancestor.Name.LocalName == "TabItem" && (string?)ancestor.Attribute(x + "Name") == "ProjectsSection");

        var restartHandler = SourceContract.ExtractMember(code, "private void RestartBookStitchButton_Click");
        Assert.Contains("_requestApplicationRestart();", restartHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled", restartHandler, StringComparison.Ordinal);
        Assert.Contains("_projectEnvironmentSelectionService.Save(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectRootFolder =", code, StringComparison.Ordinal);
        Assert.Contains("app.ApplicationRestartService.RequestRestart();", mainWindowCode, StringComparison.Ordinal);
        Assert.Contains("Close();", SourceContract.ExtractMember(mainWindowCode, "private void RequestApplicationRestart"), StringComparison.Ordinal);
    }


    [Fact]
    public void Retired_tools_have_no_ui_or_callbacks_and_navigation_uses_shared_settings_styles()
    {
        var xaml = LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml");
        var code = LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml.cs");
        Assert.Contains("NavigationDialogListStyle", xaml);
        Assert.Contains("NavigationDialogContentHostStyle", xaml);
        Assert.DoesNotContain("<TabControl", xaml);
        foreach (var retired in new[] { "DriveMeasurementListView", "WarmupOpticalDrivesCheckBox", "DebugChapterModeComboBox", "DiscWaitDialogVariantComboBox", "InputPreview_Click", "StartDeveloperAudioDiscTest_Click" })
        {
            Assert.DoesNotContain(retired, xaml);
            Assert.DoesNotContain(retired, code);
        }
    }

    private static string LoadText(params string[] relativePathParts) =>
        File.ReadAllText(FindRepositoryFile(relativePathParts));

    private static int CountOccurrences(string value, string pattern) =>
        value.Split(pattern, StringSplitOptions.None).Length - 1;

    private static string FindRepositoryFile(params string[] relativePathParts)
    {
        foreach (var root in RepositorySearchRoots())
        {
            var directory = new DirectoryInfo(root);
            while (directory is { Exists: true })
            {
                var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativePathParts).ToArray());
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Repository file not found.", Path.Combine(relativePathParts));
    }

    private static IEnumerable<string> RepositorySearchRoots([CallerFilePath] string testFilePath = "")
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        var sourceRoot = Directory.GetParent(testFilePath)?.Parent?.Parent?.FullName;
        if (!string.IsNullOrWhiteSpace(sourceRoot))
            yield return sourceRoot;
    }
}
