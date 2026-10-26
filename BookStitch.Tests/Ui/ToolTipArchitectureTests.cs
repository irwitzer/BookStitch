using BookStitch.Services;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class ToolTipArchitectureTests
{
    [Fact]
    public void Product_xaml_tooltips_use_centralized_localized_markup()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var themePath = Path.Combine(bookStitchRoot, "Themes", "AppearanceTheme.xaml");

        foreach (var path in Directory.EnumerateFiles(bookStitchRoot, "*.xaml", SearchOption.AllDirectories))
        {
            if (string.Equals(path, themePath, StringComparison.OrdinalIgnoreCase))
                continue;

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("ToolTipService.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Property=\"ToolTip\"", text, StringComparison.Ordinal);

            var document = XDocument.Load(path);
            foreach (var element in document.Descendants())
            {
                var attribute = element.Attribute("ToolTip");
                if (attribute is null)
                    continue;

                Assert.StartsWith(
                    "{services:LocalizedToolTip Key=",
                    attribute.Value,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Product_code_limits_runtime_tooltip_wiring_to_chapter_slot_availability()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var allowedInfrastructure = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(bookStitchRoot, "Services", "ToolTipTextService.cs"),
            Path.Combine(bookStitchRoot, "Services", "LocalizedToolTipExtension.cs")
        };
        var allowedRuntimeOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(bookStitchRoot, "Controls", "SettingsTagsChaptersControl.xaml.cs"),
            Path.Combine(bookStitchRoot, "Dialog", "MetadataDialog.xaml.cs"),
            Path.Combine(bookStitchRoot, "MainWindow.xaml.cs")
        };
        var runtimeTooltipOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(bookStitchRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                allowedInfrastructure.Contains(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            var hasRuntimeTooltipWiring = Regex.IsMatch(
                text,
                @"\.ToolTip\s*=|\bToolTipService\.",
                RegexOptions.CultureInvariant);
            if (!hasRuntimeTooltipWiring)
                continue;

            runtimeTooltipOwners.Add(path);
            Assert.Contains(path, allowedRuntimeOwners);
            Assert.Contains("ChapterSettingsEditorPresentation.GetSlotOptionUnavailableHelpText", text, StringComparison.Ordinal);
            Assert.Contains("item.ToolTip = helpText;", text, StringComparison.Ordinal);
            Assert.Contains("ToolTipService.SetShowOnDisabled(item, helpText is not null);", text, StringComparison.Ordinal);
        }

        Assert.True(allowedRuntimeOwners.SetEquals(runtimeTooltipOwners), "Runtime tooltip wiring must stay limited to the three chapter editor views.");
    }

    [Fact]
    public void Appearance_theme_exposes_one_central_colorscene_tooltip_style()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var themePath = Path.Combine(bookStitchRoot, "Themes", "AppearanceTheme.xaml");
        var document = XDocument.Load(themePath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var style = document.Descendants(presentation + "Style").Single(element =>
            (string?)element.Attribute(x + "Key") == "BookStitchToolTipStyle");

        Assert.Equal("ToolTip", (string?)style.Attribute("TargetType"));
        var serialized = style.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("{DynamicResource PanelBackgroundBrush}", serialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource MainTextBrush}", serialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource InputBorderBrush}", serialized, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"7\"", serialized, StringComparison.Ordinal);
        Assert.Contains("MaxWidth\" Value=\"360\"", serialized, StringComparison.Ordinal);

        var implicitStyle = document.Descendants(presentation + "Style").Single(element =>
            element.Attribute(x + "Key") is null &&
            (string?)element.Attribute("TargetType") == "ToolTip");
        Assert.Equal("{StaticResource BookStitchToolTipStyle}", (string?)implicitStyle.Attribute("BasedOn"));

        var developerToolsStyle = document.Descendants(presentation + "Style").Single(element =>
            (string?)element.Attribute(x + "Key") == "DeveloperToolsButtonStyle");
        var developerToolTipSetter = developerToolsStyle.Elements(presentation + "Setter").Single(element =>
            (string?)element.Attribute("Property") == "ToolTip");
        Assert.Equal(
            "{services:LocalizedToolTip Key=DeveloperTools_OpenWindow}",
            (string?)developerToolTipSetter.Attribute("Value"));
    }

    [Fact]
    public void Tooltip_texts_are_localization_ready_and_centrally_resolved()
    {
        Assert.Equal(
            "Projektordner öffnen",
            ToolTipTextService.Get("MainWindow_ExportPreset_OpenProjectFolder", new CultureInfo("de-DE")));
        Assert.Equal(
            "Task MGR öffnen",
            ToolTipTextService.Get("MainWindow_Parallel_OpenTaskManager", new CultureInfo("de-DE")));
        Assert.Equal(
            "Developer Tools öffnen",
            ToolTipTextService.Get("DeveloperTools_OpenWindow", new CultureInfo("de-DE")));
        Assert.Equal("Missing_Key", ToolTipTextService.Get("Missing_Key", new CultureInfo("de-DE")));
    }

    private static string FindBookStitchRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var solution = Path.Combine(current.FullName, "BookStitch.slnx");
            var project = Path.Combine(current.FullName, "BookStitch");
            if (File.Exists(solution) && Directory.Exists(project))
                return project;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("BookStitch repository root could not be located from the test output directory.");
    }
}
