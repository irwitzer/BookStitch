using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class AppearanceAssetBoundaryTests
{
    [Theory]
    [InlineData("Dialog", "AppMessageDialog.xaml")]
    [InlineData("Dialog", "BoxedDiscWaitDialog.xaml")]
    public void Visible_brand_surfaces_use_semantic_runtime_logo_resource(params string[] relativePath)
    {
        var document = LoadXaml("BookStitch", relativePath);
        Assert.Contains(
            document.Descendants(),
            element =>
                element.Name.LocalName == "Image" &&
                Attr(element, "Source") == "{DynamicResource BrandLogoImageSource}");
    }

    [Fact]
    public void Main_window_uses_its_own_theme_header_artwork_without_replacing_dialog_logos()
    {
        var document = LoadXaml("BookStitch", "MainWindow.xaml");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "Image" &&
            Attr(element, "Source") == "{DynamicResource HeaderBrandImageSource}");
        Assert.DoesNotContain(document.Descendants(), element => element.Name.LocalName == "Image" &&
            Attr(element, "Source") == "{DynamicResource BrandLogoImageSource}");
        var brand = document.Descendants().Single(element => Attr(element, "Name") == "BookStitchHeaderBrandImage");
        Assert.Equal("12,8,0,0", Attr(brand, "Margin"));
        Assert.Equal("241.826", Attr(brand, "Width"));
        Assert.Equal("72", Attr(brand, "Height"));
    }

    [Fact]
    public void Settings_info_surface_uses_its_semantic_runtime_image()
    {
        var document = LoadXaml("BookStitch", "Controls", "SettingsInfoControl.xaml");
        Assert.Contains(
            document.Descendants(),
            element =>
                element.Name.LocalName == "Image" &&
                Attr(element, "Source") == "{DynamicResource InfoSquareImageSource}");
        Assert.DoesNotContain(
            document.Descendants(),
            element =>
                element.Name.LocalName == "Image" &&
                Attr(element, "Source") == "{DynamicResource BrandLogoImageSource}");
    }

    [Theory]
    [InlineData("Dialog", "AddMoreDiscsDialog.xaml")]
    [InlineData("Dialog", "AppInputDialog.xaml")]
    [InlineData("Dialog", "AppMessageDialog.xaml")]
    [InlineData("Dialog", "BoxedDiscWaitDialog.xaml")]
    [InlineData("Dialog", "DialogPreviewWindow.xaml")]
    [InlineData("Dialog", "DiscProjectSetupDialog.xaml")]
    [InlineData("Dialog", "DiscSourceSelectionDialog.xaml")]
    [InlineData("Dialog", "ResumeProjectDialog.xaml")]
    [InlineData("Windows", "SettingsWindow.xaml")]
    public void Dialog_surfaces_bind_to_runtime_application_background(params string[] relativePath)
    {
        var document = LoadXaml("BookStitch", relativePath);
        var root = document.Root!;

        if (Attr(root, "Style") == "{StaticResource LargeResizableWindowStyle}")
        {
            Assert.Empty(Attr(root, "Background"));
            var chrome = LoadXaml("BookStitch", "Themes", "WindowChromeStyles.xaml");
            var largeWindowStyle = chrome.Descendants()
                .Single(element => element.Name.LocalName == "Style" && Attr(element, "Key") == "LargeResizableWindowStyle");
            var backgroundSetter = largeWindowStyle.Elements()
                .Single(element => element.Name.LocalName == "Setter" && Attr(element, "Property") == "Background");
            Assert.Equal("{DynamicResource AppBackgroundBrush}", Attr(backgroundSetter, "Value"));
            return;
        }

        Assert.Equal("{DynamicResource AppBackgroundBrush}", Attr(root, "Background"));
    }

    [Theory]
    [InlineData("MainWindow.xaml.cs", "CoverPreviewPresentationService.GetPlaceholderSource")]
    [InlineData("Dialog", "DiscProjectSetupDialog.xaml.cs", "CoverPreviewPresentationService.GetPlaceholderSource")]
    [InlineData("Services", "ProjectBrowserPresentationService.cs", "CoverPreviewPresentationService.GetPlaceholderPackUri")]
    public void Cover_placeholder_consumers_delegate_to_presentation_service(params string[] pathAndContract)
    {
        var contract = pathAndContract[^1];
        var relativePath = pathAndContract[..^1];

        Assert.Contains(contract, LoadText(["BookStitch", .. relativePath]), StringComparison.Ordinal);
    }


    [Fact]
    public void Semantic_appearance_assets_are_packaged_as_wpf_resources()
    {
        var project = XDocument.Load(FindRepositoryFile(["BookStitch", "BookStitch.csproj"]));
        var packagedResources = project.Descendants("Resource")
            .Select(element => ((string?)element.Attribute("Include"))?.Replace('\\', '/'))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var catalogPaths = Enum.GetValues<AppearanceAssetRole>()
            .SelectMany(role => Enum.GetValues<AppearanceMode>()
                .Select(mode => AppearanceAssetCatalog.GetOptional(role, mode)))
            .OfType<AppearanceAssetDefinition>()
            .Select(asset => asset.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => path.Split(";component/", StringSplitOptions.None)[^1]);

        Assert.All(catalogPaths, path => Assert.Contains(packagedResources, pattern =>
            System.Text.RegularExpressions.Regex.IsMatch(path,
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)));
    }

    [Fact]
    public void Brand_logo_modes_use_feather_while_cover_placeholders_keep_their_existing_mode_assets()
    {
        const string feather = "/BookStitch;component/Assets/Icons/icon/BookStitch-Icon-Feather.ico";

        var darkBrand = AppearanceAssetCatalog.Get(AppearanceAssetRole.BrandLogo, AppearanceMode.Dark);
        var lightBrand = AppearanceAssetCatalog.Get(AppearanceAssetRole.BrandLogo, AppearanceMode.Light);
        var darkCover = AppearanceAssetCatalog.Get(AppearanceAssetRole.CoverPlaceholder, AppearanceMode.Dark);
        var lightCover = AppearanceAssetCatalog.Get(AppearanceAssetRole.CoverPlaceholder, AppearanceMode.Light);

        Assert.Equal(feather, darkBrand.RelativePath);
        Assert.Equal(feather, lightBrand.RelativePath);
        Assert.Equal(
            "/BookStitch;component/Assets/Icons/Placeholder/CoverPlacHolder Old (1).png",
            darkCover.RelativePath);
        Assert.Equal(
            "/BookStitch;component/Assets/Icons/Placeholder/CoverPlacHolder Old (2).png",
            lightCover.RelativePath);

        var theme = LoadXaml("BookStitch", "Themes", "AppearanceTheme.xaml");
        Assert.Contains(
            theme.Descendants(),
            element =>
                element.Name.LocalName == "BitmapImage" &&
                Attr(element, "Key") == "BrandLogoImageSource" &&
                Attr(element, "UriSource") == feather);
    }

    [Fact]
    public void Cover_placeholder_and_brand_logo_remain_distinct_semantic_role_mode_entries()
    {
        var entries = new[] { AppearanceAssetRole.CoverPlaceholder, AppearanceAssetRole.BrandLogo }
            .SelectMany(role => Enum.GetValues<AppearanceMode>().Select(mode => AppearanceAssetCatalog.Get(role, mode)))
            .ToArray();

        Assert.Equal(4, entries.Length);
        Assert.Equal(4, entries.Select(entry => (entry.Role, entry.Mode)).Distinct().Count());
        Assert.All(entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.PackUriString)));
        Assert.NotEqual(
            AppearanceAssetCatalog.Get(AppearanceAssetRole.CoverPlaceholder, AppearanceMode.Dark).PackUriString,
            AppearanceAssetCatalog.Get(AppearanceAssetRole.BrandLogo, AppearanceMode.Dark).PackUriString);
    }

    [Fact]
    public void Open_cover_surfaces_requery_the_active_mode_after_appearance_changes()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var setup = LoadText("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml.cs");
        var metadata = LoadText("BookStitch", "Dialog", "MetadataDialog.xaml.cs");

        Assert.Contains("OnPropertyChanged(nameof(CoverPreviewSource));", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Func<AppearanceMode> _getAppearanceMode", setup, StringComparison.Ordinal);
        Assert.Contains("getAppearanceMode: () => AppearanceSettingsService.GetEffectiveMode(_settings)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("CoverPreviewPresentationService.GetPlaceholderSource(_getAppearanceMode())", setup, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(CoverPreviewSource));", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_reset_resolves_cover_placeholder_for_active_appearance_mode()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var reset = ExtractBetween(
            mainWindow,
            "private void ResetMetadataForNewDiscProject()",
            "private void InitializeGenreStateForEmptySession()");

        Assert.Contains("CoverPreviewPresentationService.GetPlaceholderSource(", reset, StringComparison.Ordinal);
        Assert.Contains("AppearanceSettingsService.GetEffectiveMode(_settings)", reset, StringComparison.Ordinal);
    }

    private static string ExtractBetween(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected source boundary '{startMarker}' was not found.");
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Expected source boundary '{endMarker}' was not found.");
        return text[start..end];
    }

    private static XDocument LoadXaml(string project, params string[] relativePath) =>
        XDocument.Load(FindRepositoryFile([project, .. relativePath]));

    private static string LoadText(params string[] relativePath) =>
        File.ReadAllText(FindRepositoryFile(relativePath));

    private static string Attr(XElement element, string attributeName) =>
        element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == attributeName)
            ?.Value ?? string.Empty;

    private static string FindRepositoryFile(
        string[] relativePath,
        [CallerFilePath] string testFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(testFilePath)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BookStitch.slnx")))
                return Path.Combine([directory.FullName, .. relativePath]);

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("BookStitch repository root was not found.");
    }
}
