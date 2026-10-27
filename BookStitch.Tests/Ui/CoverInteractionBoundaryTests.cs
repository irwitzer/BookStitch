using System.Linq;
using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class CoverInteractionBoundaryTests
{
    [Theory]
    [InlineData("BookStitch/MainWindow.xaml")]
    [InlineData("BookStitch/Dialog/DiscProjectSetupDialog.xaml")]
    [InlineData("BookStitch/Dialog/MetadataDialog.xaml")]
    public void Interactive_cover_surfaces_use_the_shared_hint_content(string relativePath)
    {
        var document = LoadXaml(relativePath.Split('/'));
        var sharedHints = document.Descendants().Where(element =>
            IsElement(element, "ContentControl") &&
            Attr(element, "Style") == "{StaticResource CoverPickerHintContentStyle}");

        Assert.Single(sharedHints);
    }

    [Fact]
    public void Shared_cover_hint_uses_two_edge_aligned_columns_and_width_driven_metrics()
    {
        var theme = LoadXaml("BookStitch", "Themes", "AppearanceTheme.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = theme.Descendants(presentation + "Style").Single(element =>
            Attr(element, "x:Key") == "CoverPickerHintContentStyle");
        var serialized = style.ToString(SaveOptions.DisableFormatting);

        Assert.Contains("ConverterParameter=HorizontalMargin", serialized, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Left\"", serialized, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", serialized, StringComparison.Ordinal);
        Assert.Equal(2, style.Descendants(presentation + "ColumnDefinition").Count(element => Attr(element, "Width") == "*"));
        Assert.DoesNotContain("40*", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("20*", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Interactive_cover_owners_delegate_selection_to_shared_picker_service()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var projectSetup = LoadText("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml.cs");

        Assert.Contains("_coverPickerService.ChooseCoverFile(", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_coverPickerService.ChooseCoverFile(", projectSetup, StringComparison.Ordinal);
        Assert.Contains("coverOwner => ChooseCover(coverOwner)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("coverPickerService: _coverPickerService", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_cover_surface_binds_preview_selection_and_remove_capability()
    {
        var document = LoadXaml("BookStitch", "MainWindow.xaml");
        var source = LoadText("BookStitch", "MainWindow.xaml.cs");
        var image = SingleElement(document, "Image", "Source", "{Binding CoverPreviewSource}");
        var cover = image.Ancestors().First(element =>
            IsElement(element, "Border") && Attr(element, "MouseLeftButtonUp") == "Cover_MouseLeftButtonUp");

        Assert.Equal("Cover_DragOver", Attr(cover, "DragOver"));
        Assert.Equal("Cover_Drop", Attr(cover, "Drop"));

        var contextMenu = cover.Descendants().Single(element => IsElement(element, "ContextMenu"));
        Assert.Equal(
            "{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}",
            Attr(contextMenu, "DataContext"));

        var remove = SingleElement(contextMenu, "MenuItem", "Click", "RemoveCover_Click");
        Assert.Equal("{Binding CanRemoveCover}", Attr(remove, "IsEnabled"));
        Assert.Contains("CoverPreviewPresentationService.CanRemove(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_dialog_cover_surface_binds_preview_selection_and_remove_capability()
    {
        var document = LoadXaml("BookStitch", "Dialog", "MetadataDialog.xaml");
        var source = LoadText("BookStitch", "Dialog", "MetadataDialog.xaml.cs");
        var image = SingleElement(document, "Image", "Source", "{Binding CoverPreviewSource}");
        var cover = image.Ancestors().First(element =>
            IsElement(element, "Border") && Attr(element, "MouseLeftButtonUp") == "ConceptCover_MouseLeftButtonUp");
        var contextMenu = cover.Descendants().Single(element => IsElement(element, "ContextMenu"));
        var remove = SingleElement(contextMenu, "MenuItem", "Click", "ConceptRemoveCover_Click");

        Assert.Equal(
            "{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}",
            Attr(contextMenu, "DataContext"));
        Assert.Equal("{Binding CanRemoveCover}", Attr(remove, "IsEnabled"));
        Assert.Contains(
            "_chooseCover(this);",
            SourceContract.ExtractMember(source, "private void ConceptCover_MouseLeftButtonUp"),
            StringComparison.Ordinal);
        Assert.Contains(
            "_removeCover();",
            SourceContract.ExtractMember(source, "private void ConceptRemoveCover_Click"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Disc_setup_cover_remove_action_uses_local_asset_state_and_canonical_metadata()
    {
        var document = LoadXaml("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml");
        var source = LoadText("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml.cs");
        var cover = SingleElement(document, "Border", "x:Name", "CoverBorder");

        Assert.Equal("CoverBorder_ContextMenuOpening", Attr(cover, "ContextMenuOpening"));
        SingleElement(cover, "MenuItem", "Click", "RemoveCover_Click");
        Assert.Contains("removeCoverItem.IsEnabled = !string.IsNullOrWhiteSpace(_processedCoverPath);", source, StringComparison.Ordinal);
        Assert.Contains("_metadataState.ApplyCoverMetadata(\"\", \"\", MetadataOrigin.User);", source, StringComparison.Ordinal);
        Assert.Contains("SetDefaultCoverPreview();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Picker_owners_guard_modal_selection_until_the_input_queue_is_idle()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var projectSetup = LoadText("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml.cs");

        Assert.Contains("if (!CanChangeBookMetadata || _isCoverDialogOpen)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_isCoverDialogOpen = true;", mainWindow, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new Action(() => _isCoverDialogOpen = false)", mainWindow, StringComparison.Ordinal);

        Assert.Contains("if (_isCoverDialogOpen)", projectSetup, StringComparison.Ordinal);
        Assert.Contains("_isCoverDialogOpen = true;", projectSetup, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Threading.DispatcherPriority.ApplicationIdle", projectSetup, StringComparison.Ordinal);
        Assert.Contains("new System.Action(() => _isCoverDialogOpen = false)", projectSetup, StringComparison.Ordinal);
    }

    [Fact]
    public void Managed_cover_workflows_use_the_committed_title_and_preserve_cover_origin()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var setup = LoadText("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml.cs");

        var mainProcessing = SourceContract.ExtractMember(mainWindow, "private void SetCoverFromFile(string filePath, Window? owner = null)");
        Assert.Contains("CoverImageService.CreateProcessedCover(", mainProcessing, StringComparison.Ordinal);
        Assert.Contains("BookTitle", mainProcessing, StringComparison.Ordinal);

        var mainAlignment = SourceContract.ExtractMember(mainWindow, "private void AlignManagedCoverWithBookTitle()");
        Assert.Contains("EnsureManagedCoverForTitle(currentPath, _projectMetadata.BookTitle)", mainAlignment, StringComparison.Ordinal);
        Assert.Contains("var origin = _projectMetadata.CanonicalMetadata.Cover.Origin;", mainAlignment, StringComparison.Ordinal);
        Assert.Contains("ApplyCoverMetadata(_projectMetadata.CoverSourcePath, alignedPath, origin)", mainAlignment, StringComparison.Ordinal);
        Assert.Contains(
            "AlignManagedCoverWithBookTitle();",
            SourceContract.ExtractMember(mainWindow, "public string BookTitle"),
            StringComparison.Ordinal);

        var setupProcessing = SourceContract.ExtractMember(setup, "private async Task ProcessSelectedCover(string sourcePath)");
        Assert.Contains("_metadataState.BookTitle.Trim()", setupProcessing, StringComparison.Ordinal);

        var setupAlignment = SourceContract.ExtractMember(setup, "private void AlignManagedCoverWithCommittedTitle()");
        Assert.Contains("EnsureManagedCoverForTitle(_processedCoverPath, _metadataState.BookTitle)", setupAlignment, StringComparison.Ordinal);
        Assert.Contains("var origin = _metadataState.CanonicalMetadata.Cover.Origin;", setupAlignment, StringComparison.Ordinal);
        Assert.Contains("ApplyCoverMetadata(_selectedCoverSourcePath, alignedPath, origin)", setupAlignment, StringComparison.Ordinal);
        Assert.Contains(
            "AlignManagedCoverWithCommittedTitle();",
            SourceContract.ExtractMember(setup, "private bool CaptureResult(bool validateForStart)"),
            StringComparison.Ordinal);
    }

    private static XDocument LoadXaml(params string[] relativePath) =>
        RepositorySource.LoadXaml(relativePath);

    private static string LoadText(params string[] relativePath) =>
        RepositorySource.Read(relativePath);

    private static XElement SingleElement(
        XContainer container,
        string localName,
        string attributeName,
        string attributeValue) =>
        container.Descendants()
            .Single(element =>
                IsElement(element, localName) &&
                Attr(element, attributeName) == attributeValue);

    private static bool IsElement(XElement element, string localName) =>
        element.Name.LocalName == localName;

    private static string Attr(XElement element, string attributeName)
    {
        XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        if (attributeName.StartsWith("x:", StringComparison.Ordinal))
            return element.Attribute(xamlNamespace + attributeName[2..])?.Value ?? string.Empty;

        return element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == attributeName)
            ?.Value ?? string.Empty;
    }

}
