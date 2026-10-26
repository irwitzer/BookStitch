using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class AppearanceResourceBoundaryTests
{
    [Theory]
    [InlineData("AppearanceTextBoxStyle")]
    [InlineData("AppearanceComboBoxStyle")]
    public void Inputs_dim_the_complete_disabled_control_and_restore_normal_opacity_when_enabled(string key)
    {
        var theme = XDocument.Parse(RepositorySource.Read(["BookStitch", "Themes", "AppearanceTheme.xaml"]));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = theme.Root!.Elements().Single(element => (string?)element.Attribute(x + "Key") == key);
        var disabled = style.Descendants().Single(element => element.Name.LocalName == "Trigger" &&
            (string?)element.Attribute("Property") == "IsEnabled" && (string?)element.Attribute("Value") == "False");
        var opacity = disabled.Elements().Single(element => (string?)element.Attribute("Property") == "Opacity");
        Assert.Null(opacity.Attribute("TargetName"));
        Assert.Equal("0.72", (string?)opacity.Attribute("Value"));
        Assert.DoesNotContain(style.Elements(), element => (string?)element.Attribute("Property") == "Opacity" &&
            (string?)element.Attribute("Value") != "1");
    }

    [Fact]
    public void Appearance_theme_is_merged_once_at_application_scope()
    {
        var appXamlPath = RepositorySource.Find(["BookStitch", "App.xaml"]);
        var bookStitchRoot = Path.GetDirectoryName(appXamlPath)!;
        var appXaml = File.ReadAllText(appXamlPath);

        Assert.Contains("Source=\"/Themes/AppearanceTheme.xaml\"", appXaml, StringComparison.Ordinal);
        foreach (var xamlFile in ProductXamlFiles(bookStitchRoot).Where(path =>
                     !string.Equals(path, appXamlPath, StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain("AppearanceTheme.xaml", File.ReadAllText(xamlFile), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Runtime_appearance_brush_consumers_use_dynamic_resources()
    {
        var bookStitchRoot = Path.GetDirectoryName(
            RepositorySource.Find(["BookStitch", "BookStitch.csproj"]))!;
        var runtimeBrushKeys = AppearanceResourceKeys.RuntimeBrushKeys.OfType<string>().ToArray();

        foreach (var xamlFile in ProductXamlFiles(bookStitchRoot))
        {
            var text = File.ReadAllText(xamlFile);
            foreach (var brushKey in runtimeBrushKeys)
            {
                Assert.DoesNotContain(
                    $"StaticResource {brushKey}",
                    text,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Mode_specific_accent_and_button_colors_do_not_leak_into_product_views()
    {
        var bookStitchRoot = Path.GetDirectoryName(
            RepositorySource.Find(["BookStitch", "BookStitch.csproj"]))!;
        var themePath = Path.Combine(bookStitchRoot, "Themes", "AppearanceTheme.xaml");
        var brandColors = new[] { BrandAccentPalette.Dark, BrandAccentPalette.Light }
            .SelectMany(AccentColors)
            .Concat(new[] { ButtonPalette.Dark, ButtonPalette.Light }.SelectMany(ButtonColors))
            .Select(ToRgbHex)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var xamlFile in ProductXamlFiles(bookStitchRoot).Where(path =>
                     !string.Equals(path, themePath, StringComparison.OrdinalIgnoreCase)))
        {
            var leakedColor = XDocument.Load(xamlFile)
                .Descendants()
                .Attributes()
                .FirstOrDefault(attribute => brandColors.Contains(attribute.Value));
            Assert.True(
                leakedColor is null,
                $"Brand color {leakedColor?.Value} must come from a semantic resource in {Path.GetRelativePath(bookStitchRoot, xamlFile)}.");
        }
    }

    [Fact]
    public void Global_combo_box_items_use_one_highlight_visual_and_disabled_state_overrides_it()
    {
        var bookStitchRoot = Path.GetDirectoryName(
            RepositorySource.Find(["BookStitch", "BookStitch.csproj"]))!;
        var theme = XDocument.Load(Path.Combine(bookStitchRoot, "Themes", "AppearanceTheme.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var style = theme.Root!.Elements(presentation + "Style").Single(element =>
            (string?)element.Attribute("TargetType") == "ComboBoxItem" && element.Attribute(x + "Key") is null);
        var triggers = style
            .Descendants(presentation + "ControlTemplate")
            .Single()
            .Descendants(presentation + "Trigger")
            .ToArray();
        var highlighted = triggers.Single(trigger =>
            (string?)trigger.Attribute("Property") == "IsHighlighted" &&
            (string?)trigger.Attribute("Value") == "True");

        Assert.Contains(
            "{DynamicResource ChoiceItemHighlightBackgroundBrush}",
            highlighted.ToString(SaveOptions.DisableFormatting),
            StringComparison.Ordinal);
        Assert.Contains(
            "{DynamicResource ChoiceItemHighlightForegroundBrush}",
            highlighted.ToString(SaveOptions.DisableFormatting),
            StringComparison.Ordinal);
        Assert.DoesNotContain(triggers, trigger => (string?)trigger.Attribute("Property") == "IsSelected");

        var disabled = triggers.Single(trigger =>
            (string?)trigger.Attribute("Property") == "IsEnabled" &&
            (string?)trigger.Attribute("Value") == "False");
        var disabledXml = disabled.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("Property=\"Background\" Value=\"Transparent\"", disabledXml, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DisabledButtonTextBrush}", disabledXml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Opacity\" Value=\"0.48\"", disabledXml, StringComparison.Ordinal);
    }

    [Fact]
    public void Compact_combo_box_keeps_27_dip_outer_height_and_removes_inner_vertical_margins()
    {
        var bookStitchRoot = Path.GetDirectoryName(
            RepositorySource.Find(["BookStitch", "BookStitch.csproj"]))!;
        var theme = XDocument.Load(Path.Combine(bookStitchRoot, "Themes", "AppearanceTheme.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var style = theme.Root!.Elements(presentation + "Style").Single(element =>
            (string?)element.Attribute(x + "Key") == "AppearanceCompactComboBoxStyle");
        var resources = style.Element(presentation + "Style.Resources")!;
        var contentMargin = resources.Elements(presentation + "Thickness").Single(element =>
            (string?)element.Attribute(x + "Key") == "AppearanceComboBoxContentMargin");
        var editMargin = resources.Elements(presentation + "Thickness").Single(element =>
            (string?)element.Attribute(x + "Key") == "AppearanceComboBoxEditableTextMargin");
        var setters = style.Elements(presentation + "Setter").ToArray();

        Assert.Equal("7,0,28,0", contentMargin.Value);
        Assert.Equal("7,0,28,0", editMargin.Value);
        Assert.Equal(contentMargin.Value, editMargin.Value);
        var baseStyle = theme.Root!.Elements(presentation + "Style").Single(element =>
            (string?)element.Attribute(x + "Key") == "AppearanceComboBoxStyle");
        var editableTextBox = baseStyle.Descendants(presentation + "TextBox").Single(element =>
            (string?)element.Attribute(x + "Name") == "PART_EditableTextBox");
        var contentSite = baseStyle.Descendants(presentation + "ContentPresenter").Single(element =>
            (string?)element.Attribute(x + "Name") == "ContentSite");
        Assert.Equal("0", (string?)editableTextBox.Attribute("Padding"));
        Assert.Equal(
            "{DynamicResource AppearanceComboBoxEditableTextMargin}",
            (string?)editableTextBox.Attribute("Margin"));
        Assert.Equal(
            "{DynamicResource AppearanceComboBoxContentMargin}",
            (string?)contentSite.Attribute("Margin"));
        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "Height" && (string?)setter.Attribute("Value") == "27");
        Assert.Contains(setters, setter =>
            (string?)setter.Attribute("Property") == "VerticalContentAlignment" && (string?)setter.Attribute("Value") == "Center");
    }

    private static IEnumerable<Color> AccentColors(BrandAccentPaletteDefinition palette) =>
    [
        palette.Accent, palette.AccentHover, palette.AccentSoft, palette.AccentForeground, palette.Selection
    ];

    private static IEnumerable<Color> ButtonColors(ButtonPaletteDefinition palette) =>
    [
        palette.PrimaryBackgroundColor, palette.PrimaryHoverBackgroundColor, palette.PrimaryPressedBackgroundColor,
        palette.PrimaryForegroundColor, palette.PrimaryBorderColor, palette.PrimaryHoverBorderColor, palette.PrimaryPressedBorderColor,
        palette.SecondaryBackgroundColor, palette.SecondaryHoverBackgroundColor, palette.SecondaryPressedBackgroundColor,
        palette.SecondaryForegroundColor, palette.SecondaryBorderColor, palette.SecondaryHoverBorderColor
    ];

    private static string ToRgbHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static IEnumerable<string> ProductXamlFiles(string bookStitchRoot) =>
        Directory.EnumerateFiles(bookStitchRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(bookStitchRoot, path)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                segment.Equals("obj", StringComparison.OrdinalIgnoreCase)));
}
