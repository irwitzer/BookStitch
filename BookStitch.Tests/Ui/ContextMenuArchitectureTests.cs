using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class ContextMenuArchitectureTests
{
    [Fact]
    public void Central_context_menu_styles_preserve_palette_focus_and_disabled_state()
    {
        var theme = RepositorySource.LoadXaml(["BookStitch", "Themes", "AppearanceTheme.xaml"]);
        var contextMenuStyle = Style(theme, "BookStitchContextMenuStyle");
        var menuItemStyle = Style(theme, "BookStitchContextMenuItemStyle");

        Assert.Equal("{DynamicResource PanelBackgroundBrush}", SetterValue(contextMenuStyle, "Background"));
        Assert.Equal("{DynamicResource MainTextBrush}", SetterValue(contextMenuStyle, "Foreground"));
        Assert.Equal("{DynamicResource InputBorderBrush}", SetterValue(contextMenuStyle, "BorderBrush"));
        Assert.Equal(
            "{StaticResource BookStitchKeyboardFocusVisualStyle}",
            SetterValue(menuItemStyle, "FocusVisualStyle"));

        var highlighted = Trigger(menuItemStyle, "IsHighlighted", "True");
        Assert.Equal("{DynamicResource AccentBrush}", SetterValue(highlighted, "Background", "ItemBorder"));
        Assert.Equal("{DynamicResource OnAccentTextBrush}", SetterValue(highlighted, "Foreground"));

        var disabled = Trigger(menuItemStyle, "IsEnabled", "False");
        Assert.Equal("{DynamicResource MutedTextBrush}", SetterValue(disabled, "Foreground"));
        Assert.Equal("Arrow", SetterValue(disabled, "Cursor"));
    }

    [Fact]
    public void Submenu_style_owns_a_right_hand_popup_and_leaf_style_marks_checked_items()
    {
        var theme = RepositorySource.LoadXaml(["BookStitch", "Themes", "AppearanceTheme.xaml"]);
        var submenuStyle = Style(theme, "BookStitchContextSubmenuItemStyle");
        var leafStyle = Style(theme, "BookStitchContextMenuItemStyle");

        Assert.Equal("{StaticResource BookStitchContextMenuItemStyle}", Attribute(submenuStyle, "BasedOn"));
        var popup = submenuStyle.Descendants().Single(element =>
            IsElement(element, "Popup") && Attribute(element, "x:Name") == "PART_Popup");
        Assert.Equal("Right", Attribute(popup, "Placement"));
        Assert.Equal("{Binding RelativeSource={RelativeSource TemplatedParent}}", Attribute(popup, "PlacementTarget"));
        Assert.Equal("{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}", Attribute(popup, "IsOpen"));
        Assert.Contains(popup.Descendants(), element =>
            IsElement(element, "StackPanel") && Attribute(element, "IsItemsHost") == "True");

        var checkable = Trigger(leafStyle, "IsCheckable", "True");
        Assert.Equal("18,0,0,0", SetterValue(checkable, "Margin", "HeaderPresenter"));
        var checkedTrigger = Trigger(leafStyle, "IsChecked", "True");
        Assert.Equal("Visible", SetterValue(checkedTrigger, "Visibility", "SelectionMarker"));
    }

    [Fact]
    public void Text_edit_context_menu_targets_its_owning_editor_from_shared_input_styles()
    {
        var theme = RepositorySource.LoadXaml(["BookStitch", "Themes", "AppearanceTheme.xaml"]);
        var menu = theme.Descendants().Single(element =>
            IsElement(element, "ContextMenu") && Attribute(element, "x:Key") == "BookStitchTextEditContextMenu");

        Assert.Equal("False", Attribute(menu, "x:Shared"));
        Assert.Equal("{StaticResource BookStitchContextMenuStyle}", Attribute(menu, "Style"));

        var items = menu.Elements().Where(element => IsElement(element, "MenuItem")).ToArray();
        Assert.Equal(
            ["ApplicationCommands.Cut", "ApplicationCommands.Copy", "ApplicationCommands.Paste"],
            items.Select(element => Attribute(element, "Command")).ToArray());
        Assert.All(items, item =>
        {
            Assert.Equal(
                "{Binding PlacementTarget, RelativeSource={RelativeSource AncestorType={x:Type ContextMenu}}}",
                Attribute(item, "CommandTarget"));
            Assert.Equal("{StaticResource BookStitchContextMenuItemStyle}", Attribute(item, "Style"));
        });

        var textBoxStyle = Style(theme, "AppearanceTextBoxStyle");
        Assert.Equal(
            "{DynamicResource BookStitchTextEditContextMenu}",
            SetterValue(textBoxStyle, "ContextMenu"));

        var editableComboTextBox = Style(theme, "AppearanceComboBoxStyle").Descendants().Single(element =>
            IsElement(element, "TextBox") && Attribute(element, "x:Name") == "PART_EditableTextBox");
        Assert.Equal(
            "{DynamicResource BookStitchTextEditContextMenu}",
            Attribute(editableComboTextBox, "ContextMenu"));
    }

    [Fact]
    public void Combo_box_template_owns_outer_chrome_once_and_keeps_editable_text_borderless()
    {
        var theme = RepositorySource.LoadXaml(["BookStitch", "Themes", "AppearanceTheme.xaml"]);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var style = Style(theme, "AppearanceComboBoxStyle");
        var template = style.Descendants(presentation + "ControlTemplate").First(element =>
            Attribute(element, "TargetType") == "ComboBox");
        var chrome = template.Descendants(presentation + "Border").Single(element =>
            Attribute(element, "x:Name") == "ComboBoxChrome");
        var toggle = template.Descendants(presentation + "ToggleButton").Single(element =>
            Attribute(element, "x:Name") == "ToggleButton");
        var editor = template.Descendants(presentation + "TextBox").Single(element =>
            Attribute(element, "x:Name") == "PART_EditableTextBox");

        Assert.Equal("{TemplateBinding Background}", Attribute(chrome, "Background"));
        Assert.Equal("{TemplateBinding BorderBrush}", Attribute(chrome, "BorderBrush"));
        Assert.Equal("{TemplateBinding BorderThickness}", Attribute(chrome, "BorderThickness"));
        Assert.Equal("{DynamicResource AppearanceComboBoxCornerRadius}", Attribute(chrome, "CornerRadius"));
        Assert.Equal("Transparent", Attribute(toggle, "Background"));
        Assert.Equal("0", Attribute(toggle, "BorderThickness"));
        Assert.Empty(toggle.Descendants(presentation + "Border"));
        Assert.Equal("Transparent", Attribute(editor, "Background"));
        Assert.Equal("0", Attribute(editor, "BorderThickness"));
        Assert.Equal("{DynamicResource BookStitchTextEditContextMenu}", Attribute(editor, "ContextMenu"));
    }

    [Theory]
    [InlineData("BookStitch/Themes/ConceptDialogStyles.xaml", "ConceptSearchTextBoxStyle", "AppearanceTextBoxStyle")]
    [InlineData("BookStitch/Dialog/DiscProjectSetupDialog.xaml", "DialogTextBoxStyle", "AppearanceCompactTextBoxStyle")]
    [InlineData("BookStitch/Dialog/DiscProjectSetupDialog.xaml", "DialogComboBoxStyle", "AppearanceCompactComboBoxStyle")]
    public void Dialog_input_style_definitions_inherit_the_central_text_edit_boundary(
        string relativePath,
        string styleKey,
        string baseStyleKey)
    {
        var document = RepositorySource.LoadXaml(relativePath.Split('/'));
        var style = Style(document, styleKey);

        Assert.Equal($"{{StaticResource {baseStyleKey}}}", Attribute(style, "BasedOn"));
    }

    [Fact]
    public void Application_owned_context_menus_use_the_central_styles()
    {
        var appXamlPath = RepositorySource.Find(["BookStitch", "App.xaml"]);
        var sourceRoot = Path.GetDirectoryName(appXamlPath)!;

        foreach (var xamlPath in EnumerateSourceFiles(sourceRoot, "*.xaml"))
        {
            if (string.Equals(
                    Path.GetFileName(xamlPath),
                    "AppearanceTheme.xaml",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var document = XDocument.Load(
                xamlPath,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

            foreach (var contextMenu in document.Descendants().Where(element => IsElement(element, "ContextMenu")))
            {
                Assert.True(
                    string.Equals(
                        "{StaticResource BookStitchContextMenuStyle}",
                        Attribute(contextMenu, "Style"),
                        StringComparison.Ordinal),
                    $"ContextMenu in {Path.GetRelativePath(sourceRoot, xamlPath)} must use BookStitchContextMenuStyle.");
            }
        }

        foreach (var codePath in EnumerateSourceFiles(sourceRoot, "*.cs"))
        {
            var code = File.ReadAllText(codePath);
            var relativePath = Path.GetRelativePath(sourceRoot, codePath);

            if (code.Contains("new ContextMenu", StringComparison.Ordinal))
            {
                Assert.True(
                    code.Contains("BookStitchContextMenuStyle", StringComparison.Ordinal),
                    $"ContextMenu created in {relativePath} must use BookStitchContextMenuStyle.");
            }

            if (code.Contains("new MenuItem", StringComparison.Ordinal))
            {
                Assert.True(
                    code.Contains("BookStitchContextMenuItemStyle", StringComparison.Ordinal),
                    $"MenuItem created in {relativePath} must use BookStitchContextMenuItemStyle.");
            }
        }
    }

    private static IEnumerable<string> EnumerateSourceFiles(string sourceRoot, string searchPattern) =>
        Directory.EnumerateFiles(sourceRoot, searchPattern, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(sourceRoot, path));

    private static bool IsBuildOutput(string sourceRoot, string path) =>
        Path.GetRelativePath(sourceRoot, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private static bool IsElement(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, localName.Split(':')[^1], StringComparison.Ordinal))?.Value ?? "";

    private static XElement Style(XDocument document, string key) =>
        document.Descendants().Single(element =>
            IsElement(element, "Style") && Attribute(element, "x:Key") == key);

    private static XElement Trigger(XElement style, string property, string value) =>
        style.Descendants().Single(element =>
            IsElement(element, "Trigger") &&
            Attribute(element, "Property") == property &&
            Attribute(element, "Value") == value);

    private static string SetterValue(XContainer container, string property, string targetName = "") =>
        Attribute(
            container.Elements().Single(element =>
                IsElement(element, "Setter") &&
                Attribute(element, "Property") == property &&
                Attribute(element, "TargetName") == targetName),
            "Value");
}
