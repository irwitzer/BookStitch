using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectIndexLocalizationTests
{
    public static IEnumerable<object[]> DamagedProjects()
    {
        foreach (var culture in new[] { "de", "en", "es", "fr", "cs" })
        foreach (var type in new[] { ProjectManifestTypes.FolderProject, ProjectManifestTypes.Mp3DiscProject, ProjectManifestTypes.AudioCdProject })
        foreach (var damage in new[] { "missing", "unreadable", "metadata" })
            yield return [culture, type, damage];
    }

    [Theory]
    [MemberData(nameof(DamagedProjects))]
    public void Damage_reasons_use_the_active_ui_language(string culture, string type, string damage)
    {
        WithUiCulture(culture, () =>
        {
            using var root = new TemporaryFolder();
            var category = type == ProjectManifestTypes.AudioCdProject ? "AudioDiscProjects"
                : type == ProjectManifestTypes.Mp3DiscProject ? "MP3DiscProjects" : "LocalProjects";
            var folder = Path.Combine(root.Path, "Projects", category, "Broken");
            var fileName = type == ProjectManifestTypes.AudioCdProject
                ? ProjectFolderLayout.AudioDiscManifestFileName : ProjectFolderLayout.WorkManifestFileName;
            var path = type == ProjectManifestTypes.AudioCdProject
                ? ProjectFolderLayout.GetAudioDiscManifestPath(folder) : ProjectFolderLayout.GetWorkManifestPath(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (damage == "missing")
                File.WriteAllText(Path.Combine(folder, "source.mp3"), "fixture");
            else if (damage == "unreadable")
                File.WriteAllText(path, "{ invalid");
            else
            {
                WriteValidManifest(path, folder, type);
                File.WriteAllText(ProjectFolderLayout.GetProjectMetadataPath(folder), "{ invalid");
            }

            var reason = Assert.Single(new ProjectIndexService().ScanDamagedProjects(root.Path)).Reason;
            var expected = damage switch
            {
                "missing" => LocalizationText.Format("Project.Damage.RequiredFileMissing", fileName),
                "unreadable" => LocalizationText.Format("Project.Damage.RequiredManifestUnreadable", fileName,
                    LocalizationText.Get(type == ProjectManifestTypes.AudioCdProject ? "Project.Damage.Type.AudioCd"
                        : type == ProjectManifestTypes.Mp3DiscProject ? "Project.Damage.Type.Mp3Disc" : "Project.Damage.Type.Folder")),
                _ => LocalizationText.Format("Project.Damage.MetadataUnreadable", ProjectFolderLayout.ProjectMetadataFileName)
            };
            Assert.Equal(expected, reason);
            AssertLanguage(culture, reason);
            if (damage == "unreadable") Assert.Equal("{ invalid", File.ReadAllText(path));
        });
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("cs")]
    public void Audio_project_combines_optional_export_and_metadata_problems_in_ui_language(string culture)
    {
        WithUiCulture(culture, () =>
        {
            using var root = new TemporaryFolder();
            var folder = Path.Combine(root.Path, "Projects", "AudioDiscProjects", "Broken");
            var path = ProjectFolderLayout.GetAudioDiscManifestPath(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteValidManifest(path, folder, ProjectManifestTypes.AudioCdProject);
            File.WriteAllText(ProjectFolderLayout.GetWorkManifestPath(folder), "{ invalid");
            File.WriteAllText(ProjectFolderLayout.GetProjectMetadataPath(folder), "{ invalid");

            var reason = Assert.Single(new ProjectIndexService().ScanDamagedProjects(root.Path)).Reason;
            Assert.Equal(LocalizationText.Format("Project.Damage.OptionalExportUnreadable", ProjectFolderLayout.WorkManifestFileName)
                + " " + LocalizationText.Format("Project.Damage.MetadataUnreadable", ProjectFolderLayout.ProjectMetadataFileName), reason);
            AssertLanguage(culture, reason);
        });
    }

    private static void AssertLanguage(string culture, string reason)
    {
        // Independent language anchors also catch a satellite key silently falling back to German.
        Assert.Contains(culture switch { "de" => "datei", "en" => "file", "es" => "archivo", "fr" => "fichier", _ => "soubor" },
            reason, StringComparison.OrdinalIgnoreCase);
        if (culture != "de")
            Assert.DoesNotMatch(new Regex(@"\b(Die|erforderliche|Projektdatei|fehlt|lesbar|gültiges|autoritative|Metadatendatei|Ordnerprojekt)\b"), reason);
        if (culture is "en" or "es" or "fr") Assert.DoesNotContain("Projekt", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Project.Damage.", reason);
    }

    private static void WriteValidManifest(string path, string folder, string type)
    {
        object manifest = type == ProjectManifestTypes.AudioCdProject
            ? new AudioDiscProjectManifest { ProjectFolder = folder, TotalDiscs = 1 }
            : type == ProjectManifestTypes.Mp3DiscProject
                ? new Mp3DiscProjectManifest { ProjectFolder = folder }
                : new ExportWorkManifest { ProjectWorkFolder = folder, ProjectType = type };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, manifest.GetType()));
    }

    private static void WithUiCulture(string language, Action action)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            // Keep CurrentCulture untouched: resource selection must follow the UI language.
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            action();
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
