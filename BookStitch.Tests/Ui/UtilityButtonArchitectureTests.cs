using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class UtilityButtonArchitectureTests
{
    [Fact]
    public void Settings_button_uses_the_shared_utility_interaction_and_semantic_symbol()
    {
        var theme = LoadXaml("Themes/AppearanceTheme.xaml");
        var utility = Style(theme, "UtilityIconButtonStyle");
        var settings = Style(theme, "GlobalSettingsButtonStyle");

        Assert.Equal(
            "{StaticResource BookStitchKeyboardFocusVisualStyle}",
            SetterValue(utility, "FocusVisualStyle"));
        foreach (var state in new[] { "IsMouseOver", "IsPressed", "IsEnabled" })
        {
            Assert.Contains(utility.Descendants(), element =>
                Is(element, "Trigger") && Attribute(element, "Property") == state);
        }

        Assert.Equal("{StaticResource UtilityIconButtonStyle}", Attribute(settings, "BasedOn"));
        Assert.Equal("{x:Static services:UiSymbolResources.Settings}", SetterValue(settings, "Content"));
        Assert.Equal("{StaticResource UtilityTextGlyphTemplate}", SetterValue(settings, "ContentTemplate"));

        var glyphTemplate = theme.Descendants().Single(element =>
            Is(element, "DataTemplate") && Attribute(element, "x:Key") == "UtilityTextGlyphTemplate");
        var glyph = glyphTemplate.Descendants().Single(element => Is(element, "TextBlock"));
        Assert.Contains("AncestorType={x:Type Button}", Attribute(glyph, "Foreground"), StringComparison.Ordinal);
        Assert.Contains("AncestorType={x:Type Button}", Attribute(glyph, "FontFamily"), StringComparison.Ordinal);
        Assert.Contains("AncestorType={x:Type Button}", Attribute(glyph, "FontSize"), StringComparison.Ordinal);

        var hover = settings.Descendants().Single(element =>
            Is(element, "Trigger") && Attribute(element, "Property") == "IsMouseOver" && Attribute(element, "Value") == "True");
        Assert.Contains(hover.Elements(), element =>
            Is(element, "Setter") && Attribute(element, "Property") == "Foreground" && Attribute(element, "Value") == "{DynamicResource AccentBrush}");
    }

    [Theory]
    [InlineData("MainWindow.xaml", "Settings_Click")]
    [InlineData("Dialog/MetadataDialog.xaml", "OpenSettings_Click")]
    [InlineData("Dialog/DiscProjectSetupDialog.xaml", "SettingsButton_Click")]
    public void Settings_entry_points_use_the_shared_utility_button(string relativePath, string clickHandler)
    {
        var document = LoadXaml(relativePath);
        var button = document.Descendants().Single(element =>
            Is(element, "Button") && Attribute(element, "Click") == clickHandler);

        Assert.Equal("{StaticResource GlobalSettingsButtonStyle}", Attribute(button, "Style"));
    }

    [Fact]
    public void Text_only_button_style_preserves_keyboard_and_pointer_interaction()
    {
        var style = Style(LoadXaml("Themes/AppearanceTheme.xaml"), "HiddenTextButtonStyle");

        Assert.Equal("Transparent", SetterValue(style, "Background"));
        Assert.Equal("Transparent", SetterValue(style, "BorderBrush"));
        Assert.Equal("{DynamicResource MainTextBrush}", SetterValue(style, "Foreground"));
        Assert.Equal("{StaticResource BookStitchKeyboardFocusVisualStyle}", SetterValue(style, "FocusVisualStyle"));
        foreach (var state in new[] { "IsMouseOver", "IsPressed", "IsEnabled" })
        {
            Assert.Contains(style.Descendants(), element =>
                Is(element, "Trigger") && Attribute(element, "Property") == state);
        }
    }

    [Theory]
    [InlineData("MainWindow.xaml", "OpenWorkFolder_Click")]
    [InlineData("MainWindow.xaml", "OpenTaskManager_Click")]
    [InlineData("MainWindow.xaml", "OpenSelectedFolder_Click")]
    [InlineData("Dialog/DiscProjectSetupDialog.xaml", "ProjectFolderButton_Click")]
    public void Link_like_actions_use_the_shared_text_only_button(string relativePath, string clickHandler)
    {
        var document = LoadXaml(relativePath);
        var button = document.Descendants().Single(element =>
            Is(element, "Button") && Attribute(element, "Click") == clickHandler);

        Assert.Equal("{StaticResource HiddenTextButtonStyle}", Attribute(button, "Style"));
    }

    [Fact]
    public void Product_buttons_do_not_suppress_keyboard_focus_visuals()
    {
        foreach (var path in Directory.EnumerateFiles(BookStitchRoot(), "*.xaml", SearchOption.AllDirectories))
        {
            if (path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            var suppression = document.Descendants().FirstOrDefault(element =>
                (Is(element, "Button") || Is(element, "RepeatButton")) &&
                Attribute(element, "FocusVisualStyle") == "{x:Null}");
            suppression ??= document.Descendants()
                .Where(IsButtonStyle)
                .SelectMany(style => style.Elements())
                .FirstOrDefault(element =>
                    Is(element, "Setter") &&
                    Attribute(element, "Property") == "FocusVisualStyle" &&
                    Attribute(element, "Value") == "{x:Null}");
            Assert.True(suppression is null, $"Keyboard focus visual is suppressed in {path}: {suppression}");
        }
    }

    private static bool IsButtonStyle(XElement element)
    {
        if (!Is(element, "Style"))
            return false;

        return Attribute(element, "TargetType") is "Button" or "{x:Type Button}" or
            "RepeatButton" or "{x:Type RepeatButton}";
    }

    private static string SetterValue(XElement style, string property) =>
        Attribute(style.Elements().Single(element =>
            Is(element, "Setter") && Attribute(element, "Property") == property), "Value");

    private static XElement Style(XDocument theme, string key) =>
        theme.Descendants().Single(element =>
            Is(element, "Style") && Attribute(element, "x:Key") == key);

    private static XDocument LoadXaml(string relativePath) =>
        XDocument.Load(Path.Combine(BookStitchRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static bool Is(XElement element, string localName) => element.Name.LocalName == localName;

    private static string Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name.Split(':')[^1])?.Value
        ?? string.Empty;

    private static string BookStitchRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(SourceFilePath())!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BookStitch.slnx")))
                return Path.Combine(directory.FullName, "BookStitch");

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("BookStitch repository root was not found.");
    }

    private static string SourceFilePath([CallerFilePath] string path = "") => path;
}
