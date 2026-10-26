using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

/// <summary>
/// Keeps the localization migration from regressing: production XAML must not reintroduce hardcoded
/// user-visible text, and the satellite catalogues must not reintroduce machine-translation artefacts
/// (zero-width spaces, leaked German wording) or the terminology mistakes that were corrected once.
/// </summary>
public sealed class LocalizationHardcodeGuardTests
{
    [Theory]
    [InlineData("", @"\b(?:Ihnen|Ihre[nrms]?|[Vv]erwenden Sie|[Pp]rüfen Sie|[Ww]ählen Sie|[Hh]aben Sie|[Kk]önnen Sie)\b")]
    [InlineData("fr", @"\b(?:vous|votre|vos|veuillez)\b")]
    [InlineData("es", @"\b(?:usted|seleccione|compruebe|utilice|haga clic|revise|vuelva a cargar)\b")]
    [InlineData("cs", @"\b(?:můžete|chcete|zkontrolujte|vyberte|vložte|použijte|počkejte|vašemu)\b")]
    public void CataloguesDoNotReintroduceFormalUserAddress(string culture, string pattern)
    {
        var filename = "LocalizationResources" + (culture.Length == 0 ? ".de" : "." + culture) + ".resx";
        foreach (var (key, value) in ReadResx(filename))
            Assert.False(Regex.IsMatch(value, pattern, culture.Length == 0 ? RegexOptions.None : RegexOptions.IgnoreCase),
                $"{filename}/{key}: formal address: {value}");
    }

    [Fact]
    public void ProductionCSharpDisplayAssignmentsDoNotContainGermanSentences()
    {
        var root = Path.GetDirectoryName(RepositorySource.Find(["BookStitch", "App.xaml"]))!;
        // Technical IDs, paths, log/exception details and units are not display assignments.
        // Only direct literals at presentation sinks are guarded here; structured/dynamic
        // presentation contracts are covered by the service tests below.
        var sink = new Regex(@"\b(?:StatusText|ProgressText|ErrorMessage|Warning|Title|Content|Header)\s*(?:=|=>|:)\s*\$?""([^""\r\n]*)""");
        var german = new Regex(@"\b(?:Bitte|wurde|konnte|nicht|keine|Datei|Laufwerk|erkannt|abgebrochen|ausgeworfen|gestartet)\b", RegexOptions.IgnoreCase);
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            foreach (Match match in sink.Matches(File.ReadAllText(path)))
                Assert.False(german.IsMatch(match.Groups[1].Value), $"{path}: hardcoded display text: {match.Value}");
    }

    private static readonly string[] SatelliteCultures = ["en", "es", "fr", "cs"];

    /// <summary>Display properties whose literal values would end up in front of a user.</summary>
    private static readonly string[] LocalizableAttributes =
    [
        "Text", "Content", "Header", "Title", "ToolTip", "Watermark", "PlaceholderText",
        "AutomationProperties.Name", "AutomationProperties.HelpText"
    ];

    /// <summary>
    /// Developer-only surfaces use the same resources and guards as the regular UI.
    /// </summary>
    private static readonly string[] DeferredFiles =
    [
    ];

    /// <summary>
    /// Literals that are not language-dependent: product names, file extensions, codec and unit names.
    /// Keep this list short and justified - it is the only escape hatch from the hardcode check.
    /// </summary>
    private static readonly HashSet<string> AllowedLiterals = new(StringComparer.Ordinal)
    {
        "BookStitch", "irwitzer", "FFmpeg", "Mp3tag", "MusicBrainz", "DriveCircle", "Chapter Master",
        "AAC", "FLAC", "MP3", "M4A", "M4B", "Auto Merge", "Bass", "Retro", "Pass-through",
        ".m4a", ".m4b", ".mp3", ".flac", "OK"
    };

    [Fact]
    public void LocalizedCSharpPresentationOwnersDoNotReintroduceGermanUiSentences()
    {
        var guarded = new Dictionary<string[], string[]>
        {
            [["BookStitch", "Services", "MetadataSummaryPresentationService.cs"]] = ["Hörbuch-Metadaten"],
            [["BookStitch", "Services", "ProjectIndexService.cs"]] = ["Die erforderliche Projektdatei", "Die optionale Export-Projektdatei", "Die autoritative Metadatendatei", "\"MP3-CD-Projekt\"", "\"Audio-CD-Projekt\"", "\"Ordnerprojekt\"", "\"BookStitch-Projekt\""],
            [["BookStitch", "Services", "AppDialogService.cs"]] = ["\"Ja\"", "\"Nein\"", "weitere Einträge"],
            [["BookStitch", "Services", "ApplicationShutdownService.cs"]] = ["läuft noch", "wird beendet"],
            [["BookStitch", "Services", "ExportPreviewCountPresentationService.cs"]] = ["Kapitel:", "geplant"],
            [["BookStitch", "Services", "ExportDurationPresentationService.cs"]] = ["Gesamtdauer", "bekannt"],
            [["BookStitch", "Services", "ExportPreviewFileNamePresentationService.cs"]] = ["Ausgabe:"],
            [["BookStitch", "Services", "FfmpegService.cs"]] = ["FFmpeg bereit.", "FFmpeg nicht gefunden."],
            [["BookStitch", "Services", "MainWindowSourcePresentationService.cs"]] = ["Noch kein Ordner ausgewählt", "CD-Laufwerk"],
            [["BookStitch", "Models", "TrackInfo.cs"]] = ["eingebettete Kapitel", "Kapitel anzeigen", "Kapitel ausblenden"],
            [["BookStitch", "Dialog", "ResumeProjectDialog.xaml.cs"]] = ["Projekte werden geladen", "Beschädigtes Projekt", "Projektordner öffnen"],
            [["BookStitch", "Dialog", "DiscProjectSetupDialog.xaml.cs"]] = ["Bitte ein Export-Preset auswählen", "Ausgabeordner auswählen", "Nicht bestätigte Eingaben", "Export-Preset prüfen"],
            [["BookStitch", "Services", "ExportWorkflowService.cs"]] = ["Konvertierung abgeschlossen", "Zusammenfügen:", "Finale Tags"],
        };

        foreach (var (path, forbidden) in guarded)
        {
            var source = RepositorySource.Read(path);
            foreach (var text in forbidden)
                Assert.DoesNotContain(text, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MainWindowPresentationSinksUseLocalizationResources()
    {
        var source = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        var members = new[]
        {
            "private void UpdateManualMergeReviewPresetState(bool forceRefresh = false, bool updateStatusText = true)",
            "private void SetTrackActionDisplayOverride(string value)",
            "private void RefreshResumeProjects(bool showStatus)",
            "private async Task WarnIfMp3DiscEjectFailedAsync(string sourceFolder, int discNumber, int totalDiscs)",
            "private void MarkMp3DiscImportFailed(",
            "private async Task<bool> PrepareCompletedMp3DiscProjectForCurrentPresetAsync(",
            "private void ShowUnsupportedDiscSourceMessage(DiscSourceAnalysis analysis)",
            "private void Cover_Drop(object sender, DragEventArgs e)",
            "private void SetCoverFromFile(string filePath, Window? owner = null)",
            "private ProjectCleanupResult DeleteOldProjects(int olderThanDays, bool showUserMessage)",
            "private IReadOnlyList<string> DeleteOldUnreferencedCoverFiles(DateTime nowUtc, int olderThanDays)",
            "private void OpenFolderInExplorer(string folderPath, string displayName, Window? owner = null, bool warnForProjectFolder = false)",
            "private async void CancelExport_Click(object sender, RoutedEventArgs e)",
            "private string BuildIdleExportProgressText()",
            "private void BuildTrackGridRowContextMenu(ContextMenu menu)",
            "private void BuildTrackGridColumnContextMenu(ContextMenu menu)",
            "private bool ToggleSelectedTracksFromDeleteKey()"
        };

        var resourceKeys = ReadResx("LocalizationResources.de.resx").Keys.ToHashSet(StringComparer.Ordinal);
        var stringLiteral = new Regex(@"""((?:\\.|[^""\\])*)""");

        foreach (var signature in members)
        {
            var member = SourceContract.ExtractMember(source, signature);
            foreach (Match match in stringLiteral.Matches(member))
            {
                var literal = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(literal) || literal == "*")
                    continue;

                Assert.Contains(literal, resourceKeys);
            }
        }

        var resumeProbe = SourceContract.ExtractMember(source, "private async Task<bool> EnrichResumeTracksWithFfprobeAsync(");
        Assert.Contains("LocalizationText.Get(\"MainWindow.AudioCheck.Warning\")", resumeProbe, StringComparison.Ordinal);
        Assert.Contains("MainWindow.AudioCheck.ProjectLoaded.One", resumeProbe, StringComparison.Ordinal);
        Assert.Contains("MainWindow.AudioCheck.ProjectLoaded.Many", resumeProbe, StringComparison.Ordinal);

        var technicalProbe = SourceContract.ExtractMember(source, "private async Task<bool> EnrichTracksWithFfprobeAsync(");
        Assert.Contains("ErrorMessage = LocalizationText.Get(\"MainWindow.AudioCheck.Warning\")", technicalProbe, StringComparison.Ordinal);
        Assert.Contains("MainWindow.AudioCheck.Technical.One", technicalProbe, StringComparison.Ordinal);
        Assert.Contains("MainWindow.AudioCheck.Technical.Many", technicalProbe, StringComparison.Ordinal);

        Assert.Contains("LocalizationText.Format(\"MainWindow.Progress.Idle\", 0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessibilityAndCoverPresentationOwnersUseLocalizationResources()
    {
        var metadata = RepositorySource.Read(["BookStitch", "Dialog", "ConceptMetadataFieldViewModel.cs"]);
        foreach (var key in new[]
                 {
                     "Metadata.Automation.Editor", "Metadata.Automation.Output", "Metadata.Automation.Lock",
                     "Metadata.Automation.Values", "Metadata.Automation.EditValue", "Metadata.Automation.RemoveValue"
                 })
        {
            Assert.Contains($"LocalizationText.Format(\"{key}\", Label)", metadata, StringComparison.Ordinal);
        }
        Assert.DoesNotMatch(new Regex("AutomationName\\s*=>\\s*\\$?\""), metadata);

        var cover = RepositorySource.Read(["BookStitch", "Services", "CoverImageService.cs"]);
        var qualityWarning = SourceContract.ExtractMember(cover, "private static string BuildQualityWarning(");
        Assert.Contains("LocalizationText.Format(\"Cover.QualityWarning\"", qualityWarning, StringComparison.Ordinal);
        Assert.DoesNotContain("Das Cover wurde übernommen", qualityWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataChapterAutoFitMeasuresTheLocalizedHeaders()
    {
        var source = RepositorySource.Read(["BookStitch", "Dialog", "MetadataDialog.xaml.cs"]);
        var start = source.IndexOf("private void ApplyChapterAutoFit", StringComparison.Ordinal);
        var end = source.IndexOf("private string BuildChapterAutoFitSignature", start, StringComparison.Ordinal);
        var method = source[start..end];

        Assert.Contains("LocalizationText.Get(\"UiKapitelvorschlag\")", method);
        Assert.Contains("LocalizationText.Get(\"UiDauer\")", method);
        Assert.Contains("LocalizationText.Get(\"UiKapitelwarnung\")", method);
        Assert.DoesNotContain("\"Kapitelvorschlag\"", method);
    }

    [Fact]
    public void ProductionXamlHasNoHardcodedUserVisibleText()
    {
        var offenders = new List<string>();

        foreach (var path in ProductionXamlFiles())
        {
            var source = File.ReadAllText(path);
            var name = Path.GetFileName(path);

            foreach (var attribute in LocalizableAttributes)
            {
                var pattern = new Regex($@"(?<![\w.]){Regex.Escape(attribute)}\s*=\s*""([^""]*)""");
                foreach (Match match in pattern.Matches(source))
                {
                    var value = match.Groups[1].Value.Trim();
                    if (IsIgnorableLiteral(value))
                        continue;
                    offenders.Add($"{name}: {attribute}=\"{value}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Hardcoded user-visible text in production XAML:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void SatelliteCataloguesContainNoZeroWidthCharacters()
    {
        foreach (var culture in SatelliteCultures)
        {
            foreach (var (key, value) in ReadResx($"LocalizationResources.{culture}.resx"))
            {
                Assert.False(value.Contains('\u200b') || value.Contains('\u200c') || value.Contains('\u200d'),
                    $"LocalizationResources.{culture}.resx / {key} contains a zero-width character. " +
                    "That is a machine-translation artefact - retype the affected passage.");
            }
        }
    }

    [Fact]
    public void SatelliteCataloguesDoNotLeakGermanWording()
    {
        // German words that, left untranslated, previously slipped through into en/es/fr.
        var german = new Regex(@"\b(Sprecher|Reihe|Reihennummer|Ausgabe|Ordner|Datei|Kapitel|Hörbuch)\b");

        using var master = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            RepositorySource.Find(["BookStitch", "Resources", "BookStitch.LanguageMaster.json"])));
        foreach (var culture in SatelliteCultures)
        {
            var document = XDocument.Load(RepositorySource.Find(["BookStitch", "Resources", $"LocalizationResources.{culture}.resx"]));
            foreach (var node in document.Root!.Elements("data"))
            {
                var key = node.Attribute("name")!.Value;
                var value = node.Element("value")!.Value;
                // Development exports explicitly mark untranslated targets. This is a
                // temporary source fallback, not a purported translation leaking German.
                if ((node.Element("comment")?.Value ?? "").StartsWith("DevelopmentFallback: de;", StringComparison.Ordinal))
                {
                    var entry = master.RootElement.GetProperty("entries").GetProperty(key);
                    Assert.False(entry.GetProperty("translations").TryGetProperty(culture, out var target) &&
                        target.TryGetProperty("value", out var text) && !string.IsNullOrWhiteSpace(text.GetString()));
                    Assert.Equal(entry.GetProperty("source").GetProperty("value").GetString(), value);
                    continue;
                }
                Assert.False(german.IsMatch(value),
                    $"LocalizationResources.{culture}.resx / {key} still contains German wording: {value}");
            }
        }
    }

    /// <summary>
    /// The machine translation rendered "Sprecher" as loudspeaker/orator, "Reihe" as table row and
    /// "Player" as gamer. Those readings are wrong in an audiobook tool, so they stay banned.
    /// </summary>
    [Theory]
    [InlineData("en", @"\bloudspeaker|\brow no\.|\bRow No\.")]
    [InlineData("es", @"\baltavoc\w*|\bjugador\w*|\bnúmero de fila")]
    [InlineData("fr", @"\bhaut-parleur\w*|\bjoueur\w*|\bnuméro de ligne")]
    [InlineData("cs", @"\breproduktor\w*|\bhráč\w*|\bŘádek č\.")]
    public void SatelliteCataloguesAvoidKnownMistranslations(string culture, string forbiddenPattern)
    {
        var forbidden = new Regex(forbiddenPattern, RegexOptions.IgnoreCase);

        foreach (var (key, value) in ReadResx($"LocalizationResources.{culture}.resx"))
        {
            Assert.False(forbidden.IsMatch(value),
                $"LocalizationResources.{culture}.resx / {key} uses a known mistranslation " +
                $"(loudspeaker / gamer / table row) instead of narrator, media player or series number: {value}");
        }
    }

    /// <summary>True when the literal is not a user-visible text that needs localizing.</summary>
    private static bool IsIgnorableLiteral(string value)
    {
        if (value.Length < 2) return true;
        if (value.StartsWith('{')) return true;                  // binding or markup extension
        if (value.StartsWith("&#x")) return true;                // glyph
        if (value.StartsWith("{DynamicResource", StringComparison.Ordinal)) return true;
        if (AllowedLiterals.Contains(value)) return true;
        // no run of three letters -> punctuation, digits, units, glyphs
        return !Regex.IsMatch(value, "[A-Za-zÄÖÜäöüß]{3}");
    }

    private static IEnumerable<string> ProductionXamlFiles()
    {
        var root = Path.GetDirectoryName(RepositorySource.Find(["BookStitch", "App.xaml"]))!;
        return Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => !DeferredFiles.Contains(Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

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
