using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class PrimaryButtonArchitectureTests
{
    [Fact]
    public void Primary_family_uses_dedicated_brand_resources_and_one_central_interaction_style()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var themePath = Path.Combine(bookStitchRoot, "Themes", "AppearanceTheme.xaml");
        var theme = XDocument.Load(themePath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";


        var primary = FindStyle(theme, presentation, x, "PrimaryButtonStyle");
        var primarySerialized = primary.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("{DynamicResource PrimaryButtonBackgroundBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonHoverBackgroundBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonPressedBackgroundBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonForegroundBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonBorderBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonHoverBorderBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonPressedBorderBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonVisualStateBehavior.State", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("Value=\"Muted\"", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryMutedButtonBackgroundBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryMutedButtonHoverBackgroundBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryMutedButtonPressedBackgroundBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("Value=\"Disabled\"", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DisabledButtonBackgroundBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DisabledButtonTextBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DisabledButtonBorderBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.DoesNotContain("{StaticResource PrimaryButton", primarySerialized, StringComparison.Ordinal);
        Assert.DoesNotContain("{StaticResource AccentBrush}", primarySerialized, StringComparison.Ordinal);
        Assert.DoesNotContain("{DynamicResource OnAccentTextBrush}", primarySerialized, StringComparison.Ordinal);

        var repeat = FindStyle(theme, presentation, x, "PrimaryRepeatButtonStyle");
        Assert.Equal("RepeatButton", (string?)repeat.Attribute("TargetType"));
        var repeatSerialized = repeat.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("{DynamicResource PrimaryButtonBackgroundBrush}", repeatSerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonHoverBackgroundBrush}", repeatSerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonPressedBackgroundBrush}", repeatSerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource PrimaryButtonForegroundBrush}", repeatSerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DisabledButtonBackgroundBrush}", repeatSerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DisabledButtonTextBrush}", repeatSerialized, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DisabledButtonBorderBrush}", repeatSerialized, StringComparison.Ordinal);

        var compact = FindStyle(theme, presentation, x, "PrimaryCompactButtonStyle");
        Assert.Equal("{StaticResource PrimaryButtonStyle}", (string?)compact.Attribute("BasedOn"));
        Assert.DoesNotContain(compact.Elements(presentation + "Setter"), element =>
            new[] { "Background", "Foreground", "BorderBrush", "FocusVisualStyle", "Cursor" }
                .Contains((string?)element.Attribute("Property"), StringComparer.Ordinal));
        Assert.Empty(compact.Descendants(presentation + "ControlTemplate"));
        Assert.Empty(compact.Descendants(presentation + "Trigger"));
    }

    [Fact]
    public void Primary_and_special_header_templates_propagate_foreground_and_template_cells_inherit_cell_foreground()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var theme = XDocument.Load(Path.Combine(bookStitchRoot, "Themes", "AppearanceTheme.xaml"));
        var main = XDocument.Load(Path.Combine(bookStitchRoot, "MainWindow.xaml"));
        var metadata = XDocument.Load(Path.Combine(bookStitchRoot, "Dialog", "MetadataDialog.xaml"));
        var folderBehavior = File.ReadAllText(Path.Combine(bookStitchRoot, "FolderPathInlineBehavior.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var (styleKey, ancestorType) in new[]
                 {
                     ("PrimaryButtonStyle", "Button"),
                     ("PrimaryRepeatButtonStyle", "RepeatButton")
                 })
        {
            var style = FindStyle(theme, presentation, x, styleKey);
            var presenter = style.Descendants(presentation + "ContentPresenter").Single();
            Assert.Equal("{TemplateBinding Foreground}", (string?)presenter.Attribute("TextElement.Foreground"));
            var scopedTextStyle = presenter
                .Descendants(presentation + "Style")
                .Single(element => (string?)element.Attribute("TargetType") == "TextBlock");
            var foregroundSetter = scopedTextStyle
                .Elements(presentation + "Setter")
                .Single(element => (string?)element.Attribute("Property") == "Foreground");
            Assert.Contains(
                $"AncestorType={{x:Type {ancestorType}}}",
                (string?)foregroundSetter.Attribute("Value") ?? string.Empty,
                StringComparison.Ordinal);
        }

        var inheritedText = FindStyle(theme, presentation, x, "DataGridCellInheritedTextStyle");
        Assert.Contains("AncestorType={x:Type DataGridCell}", inheritedText.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);

        var mainTextBlocks = main.Descendants(presentation + "TextBlock")
            .Where(element => (string?)element.Attribute("Style") == "{StaticResource DataGridCellInheritedTextStyle}")
            .ToArray();
        Assert.True(mainTextBlocks.Length >= 4);
        Assert.Contains(metadata.Descendants(presentation + "Style"), element =>
            (string?)element.Attribute("BasedOn") == "{StaticResource DataGridCellInheritedTextStyle}");
        Assert.DoesNotContain("Foreground =", folderBehavior, StringComparison.Ordinal);
    }

    [Fact]
    public void Numeric_steppers_use_one_central_compact_primary_repeat_button_family()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var theme = XDocument.Load(Path.Combine(bookStitchRoot, "Themes", "AppearanceTheme.xaml"));
        var documents = new[]
        {
            XDocument.Load(Path.Combine(bookStitchRoot, "MainWindow.xaml")),
            XDocument.Load(Path.Combine(bookStitchRoot, "Dialog", "DiscProjectSetupDialog.xaml")),
            XDocument.Load(Path.Combine(bookStitchRoot, "Dialog", "AddMoreDiscsDialog.xaml"))
        };
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var stepper = FindStyle(theme, presentation, x, "StepperRepeatButtonStyle");
        Assert.Equal("RepeatButton", (string?)stepper.Attribute("TargetType"));
        Assert.Equal("{StaticResource PrimaryRepeatButtonStyle}", (string?)stepper.Attribute("BasedOn"));
        Assert.Empty(stepper.Descendants(presentation + "ControlTemplate"));

        Assert.All(documents, document =>
            Assert.DoesNotContain(document.Descendants(presentation + "Style"), element =>
                (string?)element.Attribute(x + "Key") == "DialogStepButtonStyle"));

        var numericSteppers = documents
            .SelectMany(document => document.Descendants(presentation + "RepeatButton"))
            .Where(element => (string?)element.Attribute("Content") is "▲" or "▼")
            .ToArray();
        Assert.NotEmpty(numericSteppers);
        Assert.All(numericSteppers, stepButton =>
            Assert.Equal("{StaticResource StepperRepeatButtonStyle}", (string?)stepButton.Attribute("Style")));
    }

    [Fact]
    public void Resume_project_primary_action_uses_the_central_style_and_standard_focus_behavior()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var xamlPath = Path.Combine(bookStitchRoot, "Dialog", "ResumeProjectDialog.xaml");
        var codePath = Path.Combine(bookStitchRoot, "Dialog", "ResumeProjectDialog.xaml.cs");
        var document = XDocument.Load(xamlPath);
        var code = File.ReadAllText(codePath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.DoesNotContain(document.Descendants(presentation + "Style"), element =>
            (string?)element.Attribute(x + "Key") == "ProjectPrimaryButtonStyle");

        var load = document.Descendants(presentation + "Button").Single(element =>
            (string?)element.Attribute(x + "Name") == "LoadProjectButton");
        Assert.Equal("{StaticResource PrimaryButtonStyle}", (string?)load.Attribute("Style"));
        Assert.Null(load.Attribute("FocusVisualStyle"));
        Assert.Null(load.Attribute("GotKeyboardFocus"));
        Assert.Null(load.Attribute("LostKeyboardFocus"));

        Assert.DoesNotContain("ReferenceEquals(button, LoadProjectButton)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindResource(\"AccentBrush\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindResource(\"AccentHoverBrush\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindResource(\"OnAccentTextBrush\")", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Concept_close_uses_the_secondary_button_family()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var concept = XDocument.Load(Path.Combine(bookStitchRoot, "Dialog", "MetadataDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var close = concept.Descendants(presentation + "Button").Single(element =>
            (string?)element.Attribute("Click") == "Close_Click" &&
            !element.Attributes().Any(attribute =>
                attribute.Name.LocalName.EndsWith(".IsHitTestVisibleInChrome", StringComparison.Ordinal)));
        Assert.Equal("{StaticResource SecondaryButtonStyle}", (string?)close.Attribute("Style"));
    }

    private static XElement FindStyle(
        XDocument document,
        XNamespace presentation,
        XNamespace x,
        string key)
    {
        return document.Descendants(presentation + "Style").Single(element =>
            (string?)element.Attribute(x + "Key") == key);
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
