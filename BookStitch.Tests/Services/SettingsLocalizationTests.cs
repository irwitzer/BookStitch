using System.Globalization;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SettingsLocalizationTests
{
    [Theory]
    [InlineData("de-DE")][InlineData("en-US")][InlineData("es-ES")][InlineData("fr-FR")][InlineData("cs-CZ")]
    public void DriveCircle_product_name_is_preserved_in_settings_and_workflow(string language)
    {
        var previous=CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(language);
            foreach(var key in new[]{"Settings.DriveCircle.Overview","Settings.DriveCircle.ProjectStart",
                "Settings.DriveCircle.DiscHandling","Disc.DriveRound.InsertInstruction",
                "Disc.DriveRound.Checking.Progress","Disc.DriveRound.Waiting.Status"})
                {
                var text = LocalizationText.Get(key);
                Assert.Contains("Drive Circle", text);
                Assert.DoesNotContain("DriveCircle", text);
                Assert.DoesNotContain("Drive-Circle", text);
                }
        }
        finally {CultureInfo.CurrentUICulture=previous;}
    }

    [Fact]
    public void DriveCircle_settings_group_drives_on_left_and_help_and_counts_on_right()
    {
        var xaml=System.Xml.Linq.XDocument.Parse(RepositorySource.Read(["BookStitch","Controls","SettingsDriveControl.xaml"]));
        var elements=xaml.Descendants().ToArray();
        int At(string token)
        {
            var index=Array.FindIndex(elements,e=>e.Attributes().Any(a=>a.Value.Contains(token,StringComparison.Ordinal)));
            Assert.True(index>=0,"Missing settings element: "+token);return index;
        }
        Assert.True(At("ExperimentalDriveRoundEnabledCheckBox")<At("Settings.DriveCircle.Overview"));
        Assert.True(At("Settings.DriveCircle.Overview")<At("Settings.DriveCircle.Order.Label"));
        var drives=elements[At("DiscDriveSlotPanel")].Parent!;
        var help=elements[At("Settings.DriveCircle.Order.Label")].Ancestors().First(e=>e.Name.LocalName=="StackPanel");
        var counts=elements[At("DiscDriveRoundHintTextBlock")].Parent!;
        Assert.Same(help,counts);
        Assert.Same(drives.Parent,help.Parent);
        Assert.Equal("Grid",drives.Parent!.Name.LocalName);
        Assert.Equal("0",drives.Attribute("Grid.Column")?.Value);
        Assert.Equal("2",help.Attribute("Grid.Column")?.Value);
        Assert.Equal("Left",drives.Attribute("HorizontalAlignment")?.Value);
        Assert.Contains(drives.Elements(),e=>e.Attributes().Any(a=>a.Value=="DriveRoundUpdateButton"));
        Assert.Contains(drives.Elements(),e=>e.Attributes().Any(a=>a.Value=="DriveRoundRefreshButton"));
        Assert.True(At("Settings.DriveCircle.Actions.Help")<At("DiscDriveRoundHintTextBlock"));
        Assert.True(At("DiscDriveRoundHintTextBlock")<At("Settings.DriveCircle.ProjectStart"));
        Assert.True(At("Settings.DriveCircle.ProjectStart")<At("Settings.DriveCircle.DiscHandling"));
        Assert.DoesNotContain(elements,e=>e.Attributes().Any(a=>a.Value.Contains("Key=UiReihenfolge",StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Drive_counts_always_show_three_lines_including_zero(string language)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Assert.Equal(3, SettingsDriveTextService.BuildCircleHint(0, 0, 0, 5).Split(Environment.NewLine).Length);
            var lines = SettingsDriveTextService.BuildCircleHint(2, 3, 4, 5).Split(Environment.NewLine);
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("2", lines[0]);
            Assert.StartsWith("3", lines[1]);
            Assert.StartsWith("4", lines[2]);
            Assert.Equal(3, SettingsDriveTextService.BuildCircleHint(6, 0, 0, 5).Split(Environment.NewLine).Length);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
    [Fact]
    public void Saved_project_status_uses_existing_project_terms_and_omits_redundant_import_wording()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("Projekt gespeichert. Du kannst es später über ‚Projekte‘ fortsetzen.",
            LocalizationText.Get("Disc.StopSave.Saved", german));
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = german;
            Assert.Equal("Unterbrochen – 1 von 2 CDs", LocalizationText.Format("Disc.StopSave.Status", 1, 2));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Interrupted – 1 of 2 CDs", LocalizationText.Format("Disc.StopSave.Status", 1, 2));
            Assert.Equal("Always warn", LocalizationText.Get("Warnings.AlwaysWarn"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void DriveCircleHintAndAccessibilityStatesResolveInSpanish()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            var hint = SettingsDriveTextService.BuildCircleHint(3, 0, 1, 2);
            Assert.Contains("3 unidades activas conectadas", hint);
            Assert.DoesNotContain("clic izquierdo", hint);
            Assert.Contains("0 unidades conectadas desactivadas", hint);
            Assert.Contains("1 unidad no conectada", hint);
            Assert.Contains("2 unidades activas", hint);
            Assert.Equal("desconectada", LocalizationText.Get("Settings.Drive.Disconnected"));
            Assert.Equal("inactiva", LocalizationText.Get("Settings.Drive.Inactive"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void OutputPreviewUsesLocalizedSamplesWithUnchangedTemplateSyntax()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var author = LocalizationText.Get("Settings.Preview.Author");
            var title = LocalizationText.Get("Settings.Preview.Title");
            var narrator = LocalizationText.Get("Settings.Preview.Narrator");
            var album = LocalizationText.Get("Settings.Preview.Album");
            var series = LocalizationText.Get("Settings.Preview.Series");
            var filename = FileNameTemplateService.BuildOutputFileName(title, author, narrator,
                "{Autor} - {Sprecher} - {Titel}", ".m4b", album, series);
            var output = new OutputFolderLayoutService().BuildOutputPath(
                LocalizationText.Get("Settings.Preview.OutputFolder"), author, title, filename,
                "AuthorTitleNested", album, series);

            Assert.Contains("Cornelia Funke", output);
            Assert.Contains("Rainer Strecker", output);
            Assert.Contains(LocalizationText.Get("Settings.Preview.Title", CultureInfo.GetCultureInfo("fr-FR")), output);
            Assert.DoesNotContain("Tintenherz", output);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void ProjectManagementAndInfoMessagesResolveInFrench()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Contains("30 jours", LocalizationText.Format("Settings.Projects.Retention.OlderThan", 30));
            Assert.Contains("3 projets", LocalizationText.Format("Settings.Projects.Cleanup.All.Many", 3));
            Assert.Contains("projets", LocalizationText.Get("Settings.Projects.DeleteAll.Heading"));
            Assert.Contains("https://example.test", LocalizationText.Format(
                "Settings.Info.OpenLink.Message", "https://example.test", "technical failure"));
            Assert.DoesNotContain("konnte", LocalizationText.Get("Settings.Info.OpenLink.Heading"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void SettingsCodeBehindUsesResourcesForDialogsAndSampleValues()
    {
        var root = new[] { "BookStitch", "Controls" };
        var drive = RepositorySource.Read([.. root, "SettingsDriveControl.xaml.cs"]);
        var driveSlot = RepositorySource.Read([.. root, "DriveCircleSlot.xaml.cs"]);
        var projects = RepositorySource.Read([.. root, "SettingsProjectManagementControl.xaml.cs"]);
        var info = RepositorySource.Read([.. root, "SettingsInfoControl.xaml.cs"]);
        var preview = RepositorySource.Read([.. root, "SettingsTagsChaptersControl.xaml.cs"]);
        var chapter = RepositorySource.Read(["BookStitch", "Services", "ChapterSettingsPreviewService.cs"]);

        Assert.Contains("SettingsDriveTextService.BuildCircleHint", drive);
        Assert.Contains("Settings.Drive.Connected", drive);
        Assert.Contains("Settings.Drive.Rename", driveSlot);
        Assert.DoesNotContain("Header = \"Umbenennen\"", driveSlot);
        Assert.Contains("Settings.Projects.Retention.OlderThan", projects);
        Assert.Contains("Settings.Projects.Cleanup.Old.Many", projects);
        Assert.Contains("Settings.Info.OpenLink.Message", info);
        Assert.Contains("Settings.Preview.Author", preview);
        Assert.Contains("Settings.ChapterSample.BookTitle", chapter);
        Assert.DoesNotContain("Cornelia Funke", preview);
        Assert.DoesNotContain("Tintenherz", preview);
        Assert.DoesNotContain("Der Garten der verlorenen Dinge", chapter);
        Assert.DoesNotContain("Projekte älter als", projects);
        Assert.DoesNotContain("Keine CD-/DVD-Laufwerke erkannt", drive);
    }
}
