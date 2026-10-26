using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class MetadataDialogLifecycleWiringTests
{
    private static readonly string MainWindowSource = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Fact]
    public void Metadata_dialog_is_modal_to_its_actual_owner_and_settings_are_owned_by_metadata()
    {
        var opening = SourceContract.ExtractMember(MainWindowSource, "private void ShowMetadataDialog(Window owner");
        Assert.Contains("Owner = ResolveDevelopmentMetadataDialogOwner(owner)", opening, StringComparison.Ordinal);
        Assert.Contains("ModalDialogService.ShowDialog(dialog, dialog.Owner)", opening, StringComparison.Ordinal);
        Assert.DoesNotContain("dialog.Show();", opening, StringComparison.Ordinal);
        Assert.Contains("openSettings: settingsOwner => ShowSettingsDialog(settingsOwner)", opening, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_action_host_routes_the_button_without_toggling_the_panel()
    {
        var document = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
        var metadataButton = SingleElement(document, "Button", "Click", "OpenMetadataDialog_Click");
        var actionHost = Assert.IsType<XElement>(metadataButton.Parent);

        Assert.Equal("{StaticResource PrimaryCompactButtonStyle}", Attribute(metadataButton, "Style"));
        Assert.Equal("{Binding CanChangeBookMetadata}", Attribute(metadataButton, "IsEnabled"));
        Assert.Equal("Border", actionHost.Name.LocalName);
        Assert.Equal("MetadataActionHost", Attribute(actionHost, "x:Name"));

        var projectsButton = SingleElement(document, "Button", "x:Name", "ProjectsButton");
        Assert.Same(projectsButton.Parent, actionHost.Parent);
        Assert.Same(projectsButton, actionHost.ElementsAfterSelf().First());
        Assert.DoesNotContain(actionHost.Ancestors(), element =>
            Attribute(element, "MouseLeftButtonUp") == "MetadataHeader_MouseLeftButtonUp");

        var headerClick = SourceContract.ExtractMember(MainWindowSource, "private void MetadataHeader_MouseLeftButtonUp");
        var actionBoundary = SourceContract.ExtractMember(MainWindowSource, "private bool IsWithinMetadataActionHost");
        SourceContract.AssertInOrder(
            headerClick,
            "IsWithinMetadataActionHost(e.OriginalSource as DependencyObject)",
            "e.Handled = true;",
            "return;",
            "SetMetadataPanelExpanded(!_isMetadataPanelExpanded, animate: true);");
        Assert.Contains("ReferenceEquals(current, MetadataActionHost)", actionBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_panel_starts_collapsed_and_export_refresh_does_not_open_it()
    {
        Assert.Contains("SetMetadataPanelExpanded(false, animate: false);", MainWindowSource, StringComparison.Ordinal);

        var exportRefresh = SourceContract.ExtractMember(MainWindowSource, "private void NotifyExportUiStateChanged()");
        Assert.DoesNotContain("SetMetadataPanelExpanded(true", exportRefresh, StringComparison.Ordinal);
        Assert.DoesNotContain("SetMetadataPanelExpanded(!_isMetadataPanelExpanded", exportRefresh, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Availability_allows_project_setup_only_as_an_explicit_exception(
        bool canChangeBookMetadata,
        bool allowDuringProjectSetup,
        bool expected)
    {
        Assert.Equal(
            expected,
            global::BookStitch.MainWindow.CanOpenMetadataDialog(canChangeBookMetadata, allowDuringProjectSetup));
    }

    [Theory]
    [InlineData("private void ResetMetadataForNewDiscProject()", "_developmentMetadataDialog?.NotifyProjectChanged();")]
    [InlineData("private async Task LoadFolderAsync(", "RefreshOpenDevelopmentMetadataForExternalChanges();")]
    public void Project_changes_refresh_the_open_metadata_dialog(
        string memberSignature,
        string refreshCall)
    {
        var projectChange = SourceContract.ExtractMember(MainWindowSource, memberSignature);

        Assert.Contains(refreshCall, projectChange, StringComparison.Ordinal);
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
