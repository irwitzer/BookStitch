using System.Globalization;
using System.Text.RegularExpressions;
using System.IO;
using System.Xml.Linq;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

/// <summary>
/// Guards the translation catalogue: every satellite resource file must expose exactly the keys of the
/// German neutral file and must keep the composite format placeholders intact.
/// </summary>
public sealed class LocalizationCatalogTests
{
    private static readonly string[] ResourceBases = ["LocalizationResources"];

    private static readonly Regex Placeholder = new(@"(?<!\{)\{(\d+)(?:,[+-]?\d+)?(?::[^{}]*)?\}(?!\})", RegexOptions.Compiled);

    [Fact]
    public void StaticLocalizationKeysUsedByProductionSourcesExist()
    {
        var keys = ReadResx("LocalizationResources.de.resx");
        var sourceRoot = Path.GetDirectoryName(RepositorySource.Find(["BookStitch", "App.xaml"]))!;
        var xamlKey = new Regex(@"\{[^{}]*Localized(?:Text|ToolTip)\s+Key=([A-Za-z0-9_.-]+)");
        var cSharpKey = new Regex(@"LocalizationText\.(?:Get|Format)\(""([A-Za-z0-9_.-]+)""");
        var statusKey = new Regex("\\bL\\(\\\"([A-Za-z0-9_.-]+)\\\"");
        var appearanceKey = new Regex("\"(Appearance\\.(?:Background|TextColor|RowHeight)\\.[A-Za-z0-9]+)\"");

        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var source = File.ReadAllText(path);
            foreach (Match match in appearanceKey.Matches(source))
                Assert.Contains(match.Groups[1].Value, keys.Keys);
            foreach (Match match in (path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                         ? xamlKey : cSharpKey).Matches(source))
            {
                if (match.Groups[1].Value == "MainWindow.Status.")
                    continue;
                Assert.True(keys.ContainsKey(match.Groups[1].Value), $"{path}: missing key {match.Groups[1].Value}");
            }

            if (!path.EndsWith("WorkflowStatusFormatter.cs", StringComparison.Ordinal))
                continue;
            foreach (Match match in statusKey.Matches(source))
                Assert.True(keys.ContainsKey("MainWindow.Status." + match.Groups[1].Value),
                    $"{path}: missing status key {match.Groups[1].Value}");
        }
    }

    [Fact]
    public void EverySatelliteMirrorsTheGermanKeySet()
    {
        foreach (var resourceBase in ResourceBases)
        {
            var neutral = ReadResx($"{resourceBase}.de.resx");
            Assert.NotEmpty(neutral);

            foreach (var culture in GetSatelliteCultures(resourceBase))
            {
                var satellite = ReadResx($"{resourceBase}.{culture.Name}.resx");
                var missing = neutral.Keys.Except(satellite.Keys).Order(StringComparer.Ordinal).ToArray();
                var unexpected = satellite.Keys.Except(neutral.Keys).Order(StringComparer.Ordinal).ToArray();

                Assert.True(missing.Length == 0,
                    $"{resourceBase}.{culture.Name}.resx is missing: {string.Join(", ", missing)}");
                Assert.True(unexpected.Length == 0,
                    $"{resourceBase}.{culture.Name}.resx has unknown keys: {string.Join(", ", unexpected)}");
            }
        }
    }

    [Fact]
    public void EverySatelliteKeepsTheFormatPlaceholders()
    {
        foreach (var resourceBase in ResourceBases)
        {
            var neutral = ReadResx($"{resourceBase}.de.resx");

            foreach (var culture in GetSatelliteCultures(resourceBase))
            {
                var satellite = ReadResx($"{resourceBase}.{culture.Name}.resx");

                foreach (var (key, germanValue) in neutral)
                {
                    Assert.Equal(PlaceholderSet(germanValue), PlaceholderSet(satellite[key]));
                    Assert.False(string.IsNullOrWhiteSpace(satellite[key]),
                        $"{resourceBase}.{culture.Name}.resx has an empty value for {key}.");
                }
            }
        }
    }

    [Fact]
    public void EveryCatalogueHasNonemptyValuesAndValidCompositeFormats()
    {
        foreach (var culture in new[] { CultureInfo.InvariantCulture }.Concat(GetSatelliteCultures("LocalizationResources")))
        {
            var file = "LocalizationResources" + (culture.Name.Length == 0 ? ".de" : "." + culture.Name) + ".resx";
            foreach (var (key, value) in ReadResx(file))
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{file}: empty value for {key}");
                var error = Record.Exception(() => System.Text.CompositeFormat.Parse(value));
                Assert.True(error is null, $"{file}: invalid composite format for {key}: {error}");
            }
        }
    }

    [Fact]
    public void IntegrityEnumResourceFamiliesExistIncludingUncommonFailurePaths()
    {
        var keys = ReadResx("LocalizationResources.de.resx");
        foreach (var code in Enum.GetNames<IntegrityIssueCode>())
            Assert.Contains("Integrity.Issue." + code, keys.Keys);
        foreach (var code in Enum.GetNames<RepairActionCode>())
            Assert.Contains("Integrity.Action." + code, keys.Keys);
    }

    [Fact]
    public void TextEditingShortcutLabelsKeepCtrlInEveryLanguage()
    {
        var theme = XDocument.Load(RepositorySource.Find(["BookStitch", "Themes", "AppearanceTheme.xaml"]));
        var labels = theme.Descendants().Attributes("InputGestureText").Select(attribute => attribute.Value).ToArray();
        foreach (var gesture in new[] { "Ctrl+X", "Ctrl+C", "Ctrl+V" })
            Assert.Contains(gesture, labels);
        Assert.DoesNotContain(labels, label => label.StartsWith("Strg", StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalSettingsSampleAndMessageKeysExplainTheirTranslationContext()
    {
        var document = XDocument.Load(RepositorySource.Find(["BookStitch", "Resources", "LocalizationResources.de.resx"]));
        var prefixes = new[] { "Settings.Drive.", "Settings.Info.", "Settings.Preview.", "Settings.ChapterSample.", "Settings.Projects." };
        var entries = document.Root!.Elements("data")
            .Where(element => prefixes.Any(prefix => (element.Attribute("name")?.Value ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();
        Assert.NotEmpty(entries);
        foreach (var entry in entries)
            Assert.False(string.IsNullOrWhiteSpace(entry.Element("comment")?.Value),
                $"Missing translation context for {entry.Attribute("name")?.Value}.");
    }

    [Fact]
    public void LanguageChoicesAndShippedSatellitesAreBidirectionallyConsistent()
    {
        var labels = ReadResx("LocalizationResources.de.resx");
        var satellites = GetSatelliteCultures("LocalizationResources");

        foreach (var choice in UiLanguageService.Choices)
        {
            Assert.True(!string.IsNullOrWhiteSpace(choice.NativeName) || labels.ContainsKey(choice.ResourceKey),
                $"LocalizationResources.de.resx has no label for {choice.Id}.");

            if (choice.Id is UiLanguageIds.System or UiLanguageIds.German or "bn-BD" or "fi-FI")
                continue;

            var choiceCulture = CultureInfo.GetCultureInfo(choice.Id);
            Assert.Contains(satellites, satellite => IsSatelliteForChoice(satellite, choiceCulture));
        }

        foreach (var satellite in satellites)
        {
            Assert.Contains(UiLanguageService.Choices, choice =>
                choice.Id is not (UiLanguageIds.System or UiLanguageIds.German) &&
                IsSatelliteForChoice(satellite, CultureInfo.GetCultureInfo(choice.Id)));
        }
    }

    [Fact]
    public void EveryLanguageChoiceKeepsGermanNumberAndDateFormats()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            foreach (var choice in UiLanguageService.Choices)
            {
                UiLanguageService.ApplyAtStartup(choice.Id, CultureInfo.GetCultureInfo("ja-JP"));
                Assert.Equal("de-DE", CultureInfo.CurrentCulture.Name);
                Assert.Equal("de-DE", CultureInfo.DefaultThreadCurrentCulture?.Name);
            }
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }


    private static IReadOnlyList<CultureInfo> GetSatelliteCultures(string resourceBase)
    {
        var neutralPath = RepositorySource.Find(["BookStitch", "Resources", $"{resourceBase}.de.resx"]);
        var resourceDirectory = Path.GetDirectoryName(neutralPath)!;
        return Directory.EnumerateFiles(resourceDirectory, $"{resourceBase}.*.resx", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path)!)
            .Select(fileName => fileName.Substring(resourceBase.Length + 1, fileName.Length - resourceBase.Length - ".resx".Length - 1))
            .Where(name => !string.Equals(name, "de", StringComparison.OrdinalIgnoreCase))
            .Select(CultureInfo.GetCultureInfo)
            .OrderBy(culture => culture.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsSatelliteForChoice(CultureInfo satellite, CultureInfo choice)
    {
        for (var current = choice; current.Name.Length > 0; current = current.Parent)
        {
            if (string.Equals(current.Name, satellite.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string[] PlaceholderSet(string value) =>
        Placeholder.Matches(value).Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal).ToArray();

    private static Dictionary<string, string> ReadResx(string fileName)
    {
        var document = XDocument.Load(RepositorySource.Find(["BookStitch", "Resources", fileName]));
        return document.Root!.Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }
}
