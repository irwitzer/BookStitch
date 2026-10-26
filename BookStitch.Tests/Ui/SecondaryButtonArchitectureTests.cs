using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class SecondaryButtonArchitectureTests
{
    [Fact]
    public void Secondary_variants_inherit_one_interaction_core()
    {
        var theme = LoadTheme();
        var core = Style(theme, "SecondaryButtonCoreStyle");
        var serialized = core.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("{DynamicResource SecondaryButtonBackgroundBrush}", serialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SecondaryButtonHoverBackgroundBrush}", serialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SecondaryButtonPressedBackgroundBrush}", serialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SecondaryButtonForegroundBrush}", serialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SecondaryButtonBorderBrush}", serialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SecondaryButtonHoverBorderBrush}", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("{DynamicResource InputBorderBrush}", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("{DynamicResource MainTextBrush}", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("{DynamicResource NeutralHoverBackgroundBrush}", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("{DynamicResource NeutralPressedBackgroundBrush}", serialized, StringComparison.Ordinal);

        Assert.Equal(
            "{StaticResource BookStitchKeyboardFocusVisualStyle}",
            SetterValue(core, "FocusVisualStyle"));
        foreach (var state in new[] { "IsMouseOver", "IsPressed", "IsEnabled" })
        {
            Assert.Contains(core.Descendants(), element =>
                Is(element, "Trigger") && Attribute(element, "Property") == state);
        }

        Assert.Equal("{StaticResource SecondaryButtonCoreStyle}", BasedOn(theme, "SecondaryButtonStyle"));
        Assert.Equal("{StaticResource SecondaryButtonStyle}", BasedOn(theme, "SecondaryCompactButtonStyle"));
        Assert.Equal("{StaticResource SecondaryButtonStyle}", BasedOn(theme, "SecondarySplitButtonMainStyle"));
        Assert.Equal("{StaticResource SecondaryButtonStyle}", BasedOn(theme, "SecondarySplitButtonMenuStyle"));

        var compact = Style(theme, "SecondaryCompactButtonStyle");
        Assert.DoesNotContain(compact.Elements().Where(element => Is(element, "Setter")), element =>
            new[] { "Background", "Foreground", "BorderBrush", "FocusVisualStyle", "Cursor" }
                .Contains(Attribute(element, "Property"), StringComparer.Ordinal));
        Assert.DoesNotContain(compact.Descendants(), element =>
            Is(element, "ControlTemplate") || Is(element, "Trigger"));
    }

    [Fact]
    public void Secondary_standard_template_propagates_disabled_foreground_to_string_content()
    {
        var theme = LoadTheme();
        var standard = Style(theme, "SecondaryButtonStyle");
        var presenter = standard.Descendants().Single(element => Is(element, "ContentPresenter"));
        Assert.Equal("{TemplateBinding Foreground}", Attribute(presenter, "TextElement.Foreground"));

        var core = Style(theme, "SecondaryButtonCoreStyle");
        var disabled = core.Descendants().Single(element => Is(element, "Trigger") && Attribute(element, "Property") == "IsEnabled" && Attribute(element, "Value") == "False");
        foreach (var (property, value) in new[]
                 {
                     ("Background", "{DynamicResource DisabledButtonBackgroundBrush}"),
                     ("BorderBrush", "{DynamicResource DisabledButtonBorderBrush}"),
                     ("Foreground", "{DynamicResource DisabledButtonTextBrush}")
                 })
            Assert.Contains(disabled.Elements(), element => Is(element, "Setter") && Attribute(element, "Property") == property && Attribute(element, "Value") == value);
    }

    [Fact]
    public void Secondary_split_halves_preserve_their_complete_hover_outline()
    {
        var theme = LoadTheme();
        var main = Style(theme, "SecondarySplitButtonMainStyle");
        var menu = Style(theme, "SecondarySplitButtonMenuStyle");

        Assert.Equal("-1,0,0,0", SetterValue(menu, "Margin"));
        foreach (var style in new[] { main, menu })
        {
            var border = style.Descendants().Single(element =>
                Is(element, "Border") && Attribute(element, "x:Name") == "ButtonBorder");
            Assert.Equal("{TemplateBinding BorderThickness}", Attribute(border, "BorderThickness"));

            var hover = style.Descendants().Single(element =>
                Is(element, "Trigger") &&
                Attribute(element, "Property") == "IsMouseOver" &&
                Attribute(element, "Value") == "True");
            Assert.Contains(hover.Elements(), element =>
                Is(element, "Setter") &&
                Attribute(element, "Property") == "Panel.ZIndex" &&
                Attribute(element, "Value") == "1");
        }
    }

    [Fact]
    public void Secondary_split_action_glyph_follows_its_owning_control_foreground()
    {
        var glyph = Style(LoadTheme(), "SecondarySplitButtonActionGlyphStyle");

        Assert.Equal(
            "{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type Control}}}",
            SetterValue(glyph, "Foreground"));
    }

    [Fact]
    public void Secondary_split_action_menu_exposes_selection_independently_from_hover()
    {
        var style = Style(LoadTheme(), "SecondarySplitButtonActionMenuItemStyle");
        Assert.Equal("{StaticResource BookStitchContextMenuItemStyle}", Attribute(style, "BasedOn"));

        var marker = style.Descendants().Single(element =>
            Is(element, "Path") && Attribute(element, "x:Name") == "SelectionMarker");
        Assert.Equal("Hidden", Attribute(marker, "Visibility"));

        var checkedState = Trigger(style, "IsChecked");
        Assert.Contains(checkedState.Elements(), element =>
            Is(element, "Setter") &&
            Attribute(element, "TargetName") == "SelectionMarker" &&
            Attribute(element, "Property") == "Visibility" &&
            Attribute(element, "Value") == "Visible");
        var highlightedState = Trigger(style, "IsHighlighted");
        Assert.DoesNotContain(highlightedState.Elements(), element =>
            Is(element, "Setter") &&
            Attribute(element, "TargetName") == "SelectionMarker" &&
            Attribute(element, "Property") == "Visibility");
    }

    private static XElement Trigger(XElement style, string property) =>
        style.Descendants().Single(element =>
            Is(element, "Trigger") &&
            Attribute(element, "Property") == property &&
            Attribute(element, "Value") == "True");

    private static string BasedOn(XDocument theme, string key) => Attribute(Style(theme, key), "BasedOn");

    private static string SetterValue(XElement style, string property) =>
        Attribute(style.Elements().Single(element =>
            Is(element, "Setter") && Attribute(element, "Property") == property), "Value");

    private static XElement Style(XDocument theme, string key) =>
        theme.Descendants().Single(element =>
            Is(element, "Style") && Attribute(element, "x:Key") == key);

    private static XDocument LoadTheme() =>
        XDocument.Load(RepositoryFile("BookStitch", "Themes", "AppearanceTheme.xaml"));

    private static bool Is(XElement element, string localName) => element.Name.LocalName == localName;

    private static string Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name.Split(':')[^1])?.Value
        ?? string.Empty;

    private static string RepositoryFile(
        string project,
        params string[] relativePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(SourceFilePath())!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BookStitch.slnx")))
                return Path.Combine([directory.FullName, project, .. relativePath]);

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("BookStitch repository root was not found.");
    }

    private static string SourceFilePath([CallerFilePath] string path = "") => path;
}
