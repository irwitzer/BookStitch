using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Globalization;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectBrowserPresentationServiceTests
{
    [Theory][InlineData("de")][InlineData("en")][InlineData("fr")][InlineData("es")][InlineData("cs")]
    public void Preview_places_format_in_selection_and_omits_technical_findings_without_changing_evidence(string language)
    {
        var previous=CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo(language);using var folder=new TemporaryFolder();
            IntegrityFinding[] findings=[new(IntegrityIssueCode.UnknownTrackSourceId,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"technical sourceid conflict"),
                new(IntegrityIssueCode.UnsupportedSchema,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"technical schema conflict"),
                new(IntegrityIssueCode.CoverMissing,ProjectHealthStatus.Warning,RepairActionCode.None,"missing cover")];
            var project=new ProjectIndexItem {ProjectFolder=folder.Path,ProjectFormatVersion=1,HealthStatus=ProjectHealthStatus.RebuildRequired,IntegrityFindings=findings};
            var presentation=new ProjectBrowserPresentationService(new ProjectIndexService()).Create(project,ProjectRetentionModes.Never,AppearanceMode.Dark);
            var details=presentation.Details;var format="• "+LocalizationText.Format("Integrity.Format",1);
            Assert.True(details.IndexOf(LocalizationText.Get("Project.Browser.UpdatedLabel"),StringComparison.Ordinal)<details.IndexOf(format,StringComparison.Ordinal));
            Assert.True(details.IndexOf(format,StringComparison.Ordinal)<details.IndexOf(LocalizationText.Get("Project.Browser.StatusLabel"),StringComparison.Ordinal));
            foreach(var finding in findings){Assert.DoesNotContain(finding.TechnicalDetail,details);Assert.DoesNotContain(LocalizationText.Get(finding.UserMessageKey),details);Assert.DoesNotContain(LocalizationText.Get(finding.UserActionKey),details);}
            Assert.Equal(LocalizationText.Get("Integrity.RebuildRequired"),project.HealthMessage);Assert.Same(findings,project.IntegrityFindings);
        }
        finally {CultureInfo.CurrentUICulture=previous;}
    }

    [Fact]
    public void Rebuild_completion_shows_only_user_actionable_consequences()
    {
        IntegrityFinding[] findings=[new(IntegrityIssueCode.ManifestConflict,ProjectHealthStatus.RebuildRequired,RepairActionCode.None,"technical"),
            new(IntegrityIssueCode.CoverMissing,ProjectHealthStatus.Warning,RepairActionCode.None,"cover"),
            new(IntegrityIssueCode.RebuildTrackStateLost,ProjectHealthStatus.Warning,RepairActionCode.None,"lost"),
            new(IntegrityIssueCode.RebuildMetadataLost,ProjectHealthStatus.Warning,RepairActionCode.None,"metadata")];
        var text=ProjectIntegrityPresentation.FormatRebuild(new("",ProjectHealthStatus.Warning,false,true,findings),true);
        Assert.Contains(LocalizationText.Get("Rebuild.Success"),text);Assert.Contains(LocalizationText.Get("Rebuild.SuccessDetails"),text);
        Assert.Contains(LocalizationText.Get("Rebuild.CheckCover"),text);Assert.Contains(LocalizationText.Get("Integrity.Issue.RebuildTrackStateLost"),text);Assert.Contains(LocalizationText.Get("Integrity.Issue.RebuildMetadataLost"),text);
        Assert.DoesNotContain(LocalizationText.Get("Integrity.Issue.ManifestConflict"),text);Assert.DoesNotContain(LocalizationText.Get("Integrity.Action.None"),text);
    }

    [Fact]
    public void Preview_uses_the_index_snapshot_without_loading_or_repairing_the_project()
    {
        using var root = new TemporaryFolder();
        var manifest = ProjectFolderLayout.GetWorkManifestPath(root.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, "{ invalid manifest");
        var service = new ProjectBrowserPresentationService(new ProjectIndexService());
        var result = service.Create(new ProjectIndexItem
        {
            ProjectFolder = root.Path, Title = "Saved title", Author = "Saved author",
            IsSelectableProject = true, TrackCount = 382, SelectedPreset = "AAC Mono 64 kbps",
            SavedConvertedTrackCounts = new Dictionary<string, int> { ["AAC Mono 64 kbps"] = 381 }
        }, ProjectRetentionModes.Never, AppearanceMode.Dark);
        Assert.True(result.CanLoad);
        Assert.Equal("Saved title - Saved author", result.HeaderTitle);
        Assert.Contains("382", result.Details);
        Assert.Contains(LocalizationText.Format("Project.Browser.SavedConvertedCount", 381), result.Details);
        Assert.Equal("{ invalid manifest", File.ReadAllText(manifest));
        Assert.False(File.Exists(ProjectFolderLayout.GetProjectMetadataPath(root.Path)));
    }

    [Fact]
    public void Create_WithoutSelectionUsesTheActiveUiCulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            var service = new ProjectBrowserPresentationService(new ProjectIndexService());
            var result = service.Create(null, ProjectRetentionModes.Never, AppearanceMode.Dark);

            Assert.Equal("Ningún proyecto seleccionado", result.HeaderTitle);
            Assert.Equal("No se encontraron proyectos completos.", result.Details);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Create_UsesSharedNeverExpirationPresentationWithoutChangingProjectDates()
    {
        using var folder = new TemporaryFolder();
        var created = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var updated = new DateTime(2026, 2, 2, 11, 0, 0, DateTimeKind.Utc);
        var project = new ProjectIndexItem
        {
            ProjectFolder = folder.Path,
            DisplayName = "Testprojekt",
            Title = "Titel",
            Author = "Autor",
            CreatedUtc = created,
            UpdatedUtc = updated,
            ExpiresUtc = updated.AddDays(180),
            Status = ProjectPipelineStateNames.Completed,
            ProjectType = ProjectManifestTypes.FolderProject
        };
        var service = new ProjectBrowserPresentationService(new ProjectIndexService());

        var presentation = service.Create(project, ProjectRetentionModes.Never, AppearanceMode.Dark);

        Assert.Contains("• Ablauf: Nie", presentation.Details, StringComparison.Ordinal);
        Assert.Equal(created, project.CreatedUtc);
        Assert.Equal(updated, project.UpdatedUtc);
    }
}
