using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class SelectionSurfaceArchitectureTests
{
    [Fact]
    public void Settings_and_developer_selection_surfaces_use_their_central_selection_styles()
    {
        var settings = LoadXaml("BookStitch", "Windows", "SettingsWindow.xaml");
        var settingsNavigation = settings.Descendants().Single(element =>
            element.Name.LocalName == "ListBox" && Attr(element, "Name") == "SettingsNavigationList");
        Assert.Equal("{StaticResource NavigationDialogListStyle}", Attr(settingsNavigation, "Style"));
        Assert.Equal("{StaticResource NavigationDialogItemStyle}", Attr(settingsNavigation, "ItemContainerStyle"));

        var developer = LoadXaml("BookStitch", "Windows", "DeveloperToolsWindow.xaml");
        var navigation = developer.Descendants().Single(element => Attr(element, "Name") == "DeveloperNavigationList");
        Assert.Equal("{StaticResource NavigationDialogListStyle}", Attr(navigation, "Style"));
        Assert.Equal("{StaticResource NavigationDialogItemStyle}", Attr(navigation, "ItemContainerStyle"));
    }

    [Fact]
    public void Drive_lists_use_the_central_accent_selection_family()
    {
        foreach (var relativePath in new[] { "Dialog/AddMoreDiscsDialog.xaml", "Dialog/DiscSourceSelectionDialog.xaml" })
        {
            var document = LoadXaml("BookStitch", relativePath.Split('/'));
            var driveList = document.Descendants().Single(element =>
                element.Name.LocalName == "ListBox" && Attr(element, "Name") == "DriveList");

            Assert.Equal("{StaticResource ManualDiscDriveListItemStyle}", Attr(driveList, "ItemContainerStyle"));
            var itemStyle = document.Descendants().Single(element =>
                element.Name.LocalName == "Style" && Attr(element, "Key") == "ManualDiscDriveListItemStyle");
            Assert.Equal("{StaticResource AccentSelectionListBoxItemStyle}", Attr(itemStyle, "BasedOn"));
            Assert.Contains(itemStyle.Descendants(), element =>
                element.Name.LocalName == "Setter" &&
                Attr(element, "Property") == "IsEnabled" &&
                Attr(element, "Value") == "{Binding IsEnabled}");
            var itemTemplate = driveList.Descendants().Single(element => element.Name.LocalName == "DataTemplate");
            Assert.Contains(itemTemplate.Descendants(), element =>
                element.Name.LocalName == "TextBlock" &&
                Attr(element, "Text") == "{Binding DisplayName}" &&
                Attr(element, "Style") == "{StaticResource AccentSelectionPrimaryTextStyle}");
            Assert.Contains(itemTemplate.Descendants(), element =>
                element.Name.LocalName == "TextBlock" &&
                Attr(element, "Text") == "{Binding StatusText}" &&
                Attr(element, "Style") == "{StaticResource AccentSelectionSecondaryTextStyle}");
            var columns = itemTemplate.Descendants().Where(element => element.Name.LocalName == "ColumnDefinition").ToArray();
            Assert.True(columns.Length >= 4);
            Assert.Equal("174", Attr(columns[3], "Width"));
            Assert.Contains(itemTemplate.Descendants(), element =>
                element.Name.LocalName == "TextBlock" &&
                Attr(element, "Text") == "{Binding UserName}" &&
                Attr(element, "Grid.Column") == "3");
        }
    }


    [Fact]
    public void Optical_drive_ui_boundaries_use_async_drive_services_instead_of_sync_enumeration()
    {
        var mainWindow = File.ReadAllText(FindRepositoryFile(["BookStitch", "MainWindow.xaml.cs"]));
        var settingsDrive = File.ReadAllText(FindRepositoryFile(["BookStitch", "Controls", "SettingsDriveControl.xaml.cs"]));
        var sourceDialog = File.ReadAllText(FindRepositoryFile(["BookStitch", "Dialog", "DiscSourceSelectionDialog.xaml.cs"]));
        var addDialog = File.ReadAllText(FindRepositoryFile(["BookStitch", "Dialog", "AddMoreDiscsDialog.xaml.cs"]));

        Assert.Contains("BuildDriveRoundAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GetCdDrivesAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildDriveRound(", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GetCdDrivesAsync", settingsDrive, StringComparison.Ordinal);
        Assert.DoesNotContain("_discDriveService.GetCdDrives()", settingsDrive, StringComparison.Ordinal);
        Assert.Contains("DiscDriveRefreshService", sourceDialog, StringComparison.Ordinal);
        Assert.Contains("DiscDriveRefreshService", addDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run(() => _discDriveService.GetCdDrives())", sourceDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run(() => _discDriveService.GetCdDrives())", addDialog, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_drive_dialogs_preserve_the_requested_drive_across_the_initial_async_refresh()
    {
        foreach (var relativePath in new[] { "DiscSourceSelectionDialog.xaml.cs", "AddMoreDiscsDialog.xaml.cs" })
        {
            var source = File.ReadAllText(FindRepositoryFile(["BookStitch", "Dialog", relativePath]));

            Assert.Contains("_initialPreferredDriveRoot = preferredDriveRoot;", source, StringComparison.Ordinal);
            Assert.Contains("?? _initialPreferredDriveRoot;", source, StringComparison.Ordinal);
        }

        var mainWindow = File.ReadAllText(FindRepositoryFile(["BookStitch", "MainWindow.xaml.cs"]));
        Assert.Contains("_settings.LastSelectedOpticalDrive", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_settings.LastSelectedOpticalDrive = Path.GetPathRoot(dialog.SelectedPath) ?? dialog.SelectedPath;", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_drive_dialogs_use_media_change_events_without_background_refresh_timers()
    {
        foreach (var relativePath in new[] { "DiscSourceSelectionDialog.xaml.cs", "AddMoreDiscsDialog.xaml.cs" })
        {
            var source = File.ReadAllText(FindRepositoryFile(["BookStitch", "Dialog", relativePath]));

            Assert.Contains("OpticalMediaChangeMonitor", source, StringComparison.Ordinal);
            Assert.Contains("MediaChanged += MediaChangeMonitor_MediaChanged", source, StringComparison.Ordinal);
            Assert.Contains("MediaChanged -= MediaChangeMonitor_MediaChanged", source, StringComparison.Ordinal);
            Assert.Contains("_mediaChangeMonitor.Dispose()", source, StringComparison.Ordinal);
            Assert.Contains("RefreshDriveAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RefreshInterval", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RefreshTimer_Tick", source, StringComparison.Ordinal);
        }
    }

    private static XDocument LoadXaml(string project, params string[] relativePath) =>
        XDocument.Load(FindRepositoryFile([project, .. relativePath]));

    private static string Attr(XElement element, string attributeName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == attributeName)?.Value ?? string.Empty;

    private static string FindRepositoryFile(string[] relativePath, [CallerFilePath] string sourcePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BookStitch.slnx")))
                return Path.Combine([directory.FullName, .. relativePath]);
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("BookStitch repository root was not found.");
    }
}
