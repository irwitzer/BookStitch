using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class DestructiveButtonArchitectureTests
{
    [Fact]
    public void Destructive_button_family_is_centralized_and_mode_aware()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var themePath = Path.Combine(bookStitchRoot, "Themes", "AppearanceTheme.xaml");
        var theme = XDocument.Load(themePath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var core = FindStyle(theme, presentation, x, "DestructiveButtonCoreStyle");
        Assert.Equal("Button", (string?)core.Attribute("TargetType"));
        Assert.Contains("BookStitchKeyboardFocusVisualStyle", core.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);

        var danger = FindStyle(theme, presentation, x, "DangerButtonStyle");
        Assert.Equal("{StaticResource DestructiveButtonCoreStyle}", (string?)danger.Attribute("BasedOn"));
        var dangerText = danger.ToString(SaveOptions.DisableFormatting);
        foreach (var key in new[]
        {
            "DangerBackgroundBrush",
            "DangerBorderBrush",
            "DangerTextBrush",
            "DangerHoverBackgroundBrush",
            "DangerHoverBorderBrush",
            "DangerPressedBackgroundBrush",
            "DangerPressedBorderBrush"
        })
        {
            Assert.Contains($"{{DynamicResource {key}}}", dangerText, StringComparison.Ordinal);
        }

        Assert.Contains("TextElement.Foreground=\"{TemplateBinding Foreground}\"", dangerText, StringComparison.Ordinal);

        var quiet = FindStyle(theme, presentation, x, "QuietDangerButtonStyle");
        Assert.Equal("{StaticResource DestructiveButtonCoreStyle}", (string?)quiet.Attribute("BasedOn"));
        var quietText = quiet.ToString(SaveOptions.DisableFormatting);
        Assert.Contains("TextElement.Foreground=\"{TemplateBinding Foreground}\"", quietText, StringComparison.Ordinal);

        foreach (var key in new[]
        {
            "QuietDangerBackgroundBrush",
            "QuietDangerBorderBrush",
            "QuietDangerTextBrush",
            "QuietDangerHoverBackgroundBrush",
            "QuietDangerHoverBorderBrush",
            "QuietDangerHoverTextBrush",
            "QuietDangerPressedBackgroundBrush"
        })
        {
            Assert.Contains($"{{DynamicResource {key}}}", quietText, StringComparison.Ordinal);
        }

    }

    [Fact]
    public void Settings_and_resume_use_only_the_central_destructive_styles()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var settings = XDocument.Load(Path.Combine(bookStitchRoot, "Controls", "SettingsProjectManagementControl.xaml"));
        var resume = XDocument.Load(Path.Combine(bookStitchRoot, "Dialog", "ResumeProjectDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.DoesNotContain(settings.Descendants(presentation + "Style"), element =>
            (string?)element.Attribute(x + "Key") == "QuietDangerButtonStyle");
        Assert.DoesNotContain(resume.Descendants(presentation + "Style"), element =>
            (string?)element.Attribute(x + "Key") == "ProjectDeleteButtonStyle");

        var deleteOld = FindButtonByClick(settings, presentation, "DeleteOldProjectsNow_Click");
        var deleteAll = FindButtonByClick(settings, presentation, "DeleteAllProjects_Click");
        Assert.Equal("{StaticResource SettingsSecondaryButtonStyle}", (string?)deleteOld.Attribute("Style"));
        Assert.Equal("{StaticResource SettingsDestructiveButtonStyle}", (string?)deleteAll.Attribute("Style"));

        var resumeDelete = resume.Descendants(presentation + "Button").Single(element =>
            (string?)element.Attribute(x + "Name") == "DeleteProjectButton");
        Assert.Equal("{StaticResource QuietDangerButtonStyle}", (string?)resumeDelete.Attribute("Style"));
        Assert.Null(resumeDelete.Attribute("FocusVisualStyle"));
        Assert.Null(resumeDelete.Attribute("GotKeyboardFocus"));
        Assert.Null(resumeDelete.Attribute("LostKeyboardFocus"));
    }

    [Fact]
    public void Red_error_and_warning_presentation_uses_semantic_resources_instead_of_local_literals()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var appInput = File.ReadAllText(Path.Combine(bookStitchRoot, "Dialog", "AppInputDialog.xaml"));
        var setup = File.ReadAllText(Path.Combine(bookStitchRoot, "Dialog", "DiscProjectSetupDialog.xaml"));
        var addDiscs = File.ReadAllText(Path.Combine(bookStitchRoot, "Dialog", "AddMoreDiscsDialog.xaml"));
        var messageCode = File.ReadAllText(Path.Combine(bookStitchRoot, "Dialog", "AppMessageDialog.xaml.cs"));
        var main = File.ReadAllText(Path.Combine(bookStitchRoot, "MainWindow.xaml"));
        var concept = File.ReadAllText(Path.Combine(bookStitchRoot, "Dialog", "MetadataDialog.xaml"));

        Assert.Contains("{DynamicResource ErrorTextBrush}", appInput, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource ErrorTextBrush}", setup, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource ErrorTextBrush}", addDiscs, StringComparison.Ordinal);
        Assert.Contains("\"ErrorTextBrush\"", messageCode, StringComparison.Ordinal);
        Assert.Contains("\"ErrorSurfaceBrush\"", messageCode, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource WarningProgressTextBrush}", main, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource QuietDangerTextBrush}", main, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource BlockingWarningRowBackgroundBrush}", main, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource DuplicateWarningBackgroundBrush}", concept, StringComparison.Ordinal);

        foreach (var forbidden in new[] { "#FF7070", "#F1C8CB", "#D58B91", "#6B3F46" })
        {
            Assert.DoesNotContain(forbidden, appInput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, setup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, addDiscs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, main, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(forbidden, concept, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("Color.FromRgb(255, 112, 112)", messageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Color.FromRgb(45, 20, 20)", messageCode, StringComparison.Ordinal);

        var allowedCentralFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(bookStitchRoot, "Themes", "AppearanceTheme.xaml")),
            Path.GetFullPath(Path.Combine(bookStitchRoot, "Services", "Appearance", "AppearanceModePalette.cs"))
        };
        var knownLegacyRedLiterals = new[]
        {
            "#7A2630", "#B95C67", "#96313D", "#E07A85", "#652029", "#C95F6B",
            "#FF7070", "#F1C8CB", "#D58B91", "#6B3F46",
            "Color.FromRgb(255, 112, 112)", "Color.FromRgb(45, 20, 20)"
        };

        foreach (var path in Directory.EnumerateFiles(bookStitchRoot, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var fullPath = Path.GetFullPath(path);
            if (allowedCentralFiles.Contains(fullPath) ||
                fullPath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                fullPath.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(fullPath);
            foreach (var legacy in knownLegacyRedLiterals)
                Assert.DoesNotContain(legacy, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Concept_author_remove_micro_controls_remain_a_separate_non_danger_button_class()
    {
        var bookStitchRoot = FindBookStitchRoot();
        var conceptStyles = XDocument.Load(Path.Combine(bookStitchRoot, "Themes", "ConceptDialogStyles.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var removeStyle = FindStyle(conceptStyles, presentation, x, "ConceptAuthorBadgeRemoveButtonStyle");
        Assert.Equal("Button", (string?)removeStyle.Attribute("TargetType"));
        Assert.Equal("{StaticResource StandardButtonGeometryStyle}", (string?)removeStyle.Attribute("BasedOn"));
        var serialized = removeStyle.ToString(SaveOptions.DisableFormatting);
        Assert.DoesNotContain("DangerButtonStyle", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("QuietDangerButtonStyle", serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ConceptValueBadgeRemoveButtonStyle")]
    [InlineData("ConceptValueBadgeEditButtonStyle")]
    [InlineData("ConceptAuthorBadgeRemoveButtonStyle")]
    public void Badge_micro_buttons_override_both_standard_height_constraints(string key)
    {
        var document = XDocument.Load(Path.Combine(FindBookStitchRoot(), "Themes", "ConceptDialogStyles.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = FindStyle(document, p, x, key);
        Assert.Equal("{StaticResource StandardButtonGeometryStyle}", (string?)style.Attribute("BasedOn"));
        foreach (var property in new[] { "Height", "MinHeight", "Width" })
            Assert.Equal("17", (string?)style.Elements(p + "Setter").Single(e => (string?)e.Attribute("Property") == property).Attribute("Value"));
    }

    private static XElement FindButtonByClick(XDocument document, XNamespace presentation, string clickHandler) =>
        document.Descendants(presentation + "Button").Single(element => (string?)element.Attribute("Click") == clickHandler);

    private static XElement FindStyle(XDocument document, XNamespace presentation, XNamespace x, string key) =>
        document.Descendants(presentation + "Style").Single(element => (string?)element.Attribute(x + "Key") == key);

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
