using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class WindowIconMarkupBoundaryTests
{
    private const string ExpectedIcon =
        "/BookStitch;component/Assets/Icons/icon/BookStitch-Icon-OpenBook.ico";
    private const string LegacyIconName =
        "BookStitchAppIcon-Simplified-multisize.ico";

    [Fact]
    public void All_productive_xaml_windows_with_explicit_icons_use_the_openbook_contract()
    {
        var repositoryRoot = Path.GetDirectoryName(RepositorySource.Find(["BookStitch.slnx"]))!;
        var productRoot = Path.Combine(repositoryRoot, "BookStitch");

        var windowsWithExplicitIcons = Directory
            .EnumerateFiles(productRoot, "*.xaml", SearchOption.AllDirectories)
            .Select(path => (Path: path, Document: XDocument.Load(path)))
            .Where(item => item.Document.Root?.Name.LocalName == "Window")
            .Select(item => (
                item.Path,
                Icon: item.Document.Root!.Attributes()
                    .SingleOrDefault(attribute => attribute.Name.LocalName == "Icon")?.Value))
            .Where(item => !string.IsNullOrWhiteSpace(item.Icon))
            .ToArray();

        Assert.NotEmpty(windowsWithExplicitIcons);
        Assert.All(
            windowsWithExplicitIcons,
            item => Assert.True(
                string.Equals(ExpectedIcon, item.Icon, StringComparison.Ordinal),
                $"{Path.GetRelativePath(repositoryRoot, item.Path)} must use the canonical OpenBook application icon."));
    }

    [Fact]
    public void Project_embeds_the_openbook_application_icon_used_by_windows()
    {
        var project = RepositorySource.LoadXaml(["BookStitch", "BookStitch.csproj"]);
        var applicationIcon = project.Descendants("ApplicationIcon").Single().Value.Replace('\\', '/');
        var resources = project.Descendants("Resource")
            .Select(element => ((string?)element.Attribute("Include"))?.Replace('\\', '/'))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal("Assets/Icons/icon/BookStitch-Icon-OpenBook.ico", applicationIcon);
        Assert.Equal(ExpectedIcon, "/BookStitch;component/" + applicationIcon);
        Assert.Contains("Assets/Icons/icon/BookStitch-Icon-OpenBook.ico", resources);
    }

    [Fact]
    public void Productive_window_contracts_no_longer_reference_the_legacy_application_icon()
    {
        var project = RepositorySource.LoadXaml(["BookStitch", "BookStitch.csproj"]);
        var applicationIcon = project.Descendants("ApplicationIcon").Single().Value;
        var repositoryRoot = Path.GetDirectoryName(RepositorySource.Find(["BookStitch.slnx"]))!;
        var productRoot = Path.Combine(repositoryRoot, "BookStitch");
        var xaml = Directory
            .EnumerateFiles(productRoot, "*.xaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

        Assert.DoesNotContain(LegacyIconName, applicationIcon, StringComparison.Ordinal);
        Assert.All(xaml, source => Assert.DoesNotContain(LegacyIconName, source, StringComparison.Ordinal));
    }
}
