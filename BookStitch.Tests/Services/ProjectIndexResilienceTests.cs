using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using System.Globalization;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectIndexResilienceTests
{
    [Theory]
    [InlineData("future-schema", "en")]
    [InlineData("null-state", "en")]
    [InlineData("future-schema", "es")]
    [InlineData("null-state", "es")]
    public void A_broken_project_does_not_abort_indexing_of_healthy_projects(string damage, string language)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var root = new TemporaryFolder();
            var healthy = Path.Combine(root.Path, "Projects", "LocalProjects", "Healthy");
            var broken = Path.Combine(root.Path, "Projects", "LocalProjects", "Broken");
            WriteJson(ProjectFolderLayout.GetWorkManifestPath(healthy), new ExportWorkManifest
            {
                ProjectType = ProjectManifestTypes.FolderProject,
                ProjectWorkFolder = healthy,
                State = new ExportManifestState { Status = ProjectManifestStatuses.Completed }
            });
            var document = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new ExportWorkManifest
            {
                ProjectType = ProjectManifestTypes.FolderProject,
                ProjectWorkFolder = broken
            }))!;
            if (damage == "future-schema") document["schemaVersion"] = int.MaxValue;
            else document["State"] = null;
            var path = ProjectFolderLayout.GetWorkManifestPath(broken);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var original = document.ToJsonString();
            File.WriteAllText(path, original);
            var failures = new List<DamagedProjectInfo>();

            var selected = new ProjectIndexService().ScanSelectableProjects(root.Path, readFailures: failures);

            Assert.Equal(healthy, Assert.Single(selected).ProjectFolder);
            Assert.Equal(broken, Assert.Single(failures).ProjectFolder);
            Assert.Equal(LocalizationText.Get("Project.Browser.ProjectUnreadable"), failures[0].Reason);
            Assert.DoesNotContain("Projekt", failures[0].Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("schemaVersion", failures[0].Reason, StringComparison.OrdinalIgnoreCase);
            var persisted = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            if (damage == "future-schema") Assert.Equal(int.MaxValue, (int)persisted["schemaVersion"]!);
            else Assert.Null(persisted["State"]);
            // Secondary discovery also remains usable when the schema itself is unsupported.
            var damaged = new ProjectIndexService().ScanDamagedProjects(root.Path);
            if (damage == "future-schema") Assert.Equal(broken, Assert.Single(damaged).ProjectFolder);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData(ProjectHealthStatus.RebuildRequired, false, false)]
    [InlineData(ProjectHealthStatus.Damaged, false, false)]
    [InlineData(ProjectHealthStatus.RebuildRequired, true, false)]
    [InlineData(ProjectHealthStatus.RebuildRequired, false, true)]
    public async Task Recovery_fallback_preserves_persisted_health_safety_and_canonical_display_metadata(
        ProjectHealthStatus health, bool safety, bool corruptMetadata)
    {
        using var root = new TemporaryFolder();
        var folder = Path.Combine(WorkFolderStructure.FromRoot(root.Path).LocalProjectsFolder, "Broken");
        var workPath = ProjectFolderLayout.GetWorkManifestPath(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(workPath)!);
        File.WriteAllText(workPath, "{broken");
        var report = new ProjectIntegrityResult(folder, health, false, false,
            [new(safety ? IntegrityIssueCode.UnsafeReparsePoint : IntegrityIssueCode.ManifestConflict,
                health, RepairActionCode.None, "Invalid legacy work manifest.")]);
        File.WriteAllText(Path.Combine(ProjectFolderLayout.GetSettingsFolder(folder), "project-integrity.json"), JsonSerializer.Serialize(report));
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Title", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["Author"], MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder, metadata);
        if (corruptMetadata) File.WriteAllText(ProjectFolderLayout.GetProjectMetadataPath(folder), "{broken");
        var index = new ProjectIndexService();
        var item = Assert.Single((await index.Catalog.RefreshAsync(root.Path, 180)).ProjectIndexItems);
        Assert.Equal(health, item.HealthStatus); Assert.Equal(report.Findings, item.IntegrityFindings);
        Assert.False(item.IsSelectableProject); Assert.True(item.IsVisibleInProjectBrowser);
        Assert.Equal(safety, item.SafetyBlocked); Assert.Equal(!safety, item.CanAttemptLoad);
        Assert.Equal(!safety && health == ProjectHealthStatus.RebuildRequired, item.CanRebuild);
        Assert.Equal(ProjectManifestTypes.FolderProject, item.ProjectType);
        Assert.Equal(default, item.CreatedUtc); Assert.Equal(default, item.UpdatedUtc);
        Assert.Equal(corruptMetadata ? "Broken" : "Author - Title", item.DisplayName);
        var resumeItem = Assert.Single(index.ScanSelectableProjects(root.Path));
        Assert.Equal(item.HealthStatus, resumeItem.HealthStatus); Assert.Equal(item.ListDisplayState, resumeItem.ListDisplayState);
        Assert.Equal("{broken", File.ReadAllText(workPath));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"Health\":\"FutureStatus\",\"Findings\":[]}")]
    [InlineData("{\"Findings\":[]}")]
    public void Unusable_integrity_report_keeps_conservative_damaged_fallback(string report)
    {
        using var root = new TemporaryFolder();
        var path = ProjectFolderLayout.GetWorkManifestPath(root.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "{broken");
        File.WriteAllText(Path.Combine(ProjectFolderLayout.GetSettingsFolder(root.Path), "project-integrity.json"), report);
        var item = Assert.Single(new ProjectIndexService().ScanSelectableProjects(root.Path));
        Assert.Equal(ProjectHealthStatus.Damaged, item.HealthStatus); Assert.False(item.CanRebuild);
    }

    [Fact]
    public void Fallback_uses_readable_primary_type_dates_and_explicit_persisted_safety_block()
    {
        using var root = new TemporaryFolder();
        var settings = ProjectFolderLayout.GetSettingsFolder(root.Path);
        Directory.CreateDirectory(settings);
        File.WriteAllText(ProjectFolderLayout.GetWorkManifestPath(root.Path), "{broken");
        var created = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(ProjectFolderLayout.GetFolderManifestPath(root.Path), JsonSerializer.Serialize(new {
            ProjectType = ProjectManifestTypes.FolderProject, ProjectFormatVersion = 0, CreatedUtc = created, UpdatedUtc = created.AddDays(1) }));
        File.WriteAllText(Path.Combine(settings, "project-integrity.json"), "{\"Health\":\"RebuildRequired\",\"Findings\":[],\"SafetyBlocked\":true}");
        var item = Assert.Single(new ProjectIndexService().ScanSelectableProjects(root.Path));
        Assert.Equal(ProjectManifestTypes.FolderProject, item.ProjectType);
        Assert.Equal(created, item.CreatedUtc); Assert.Equal(created.AddDays(1), item.UpdatedUtc);
        Assert.Equal(ProjectHealthStatus.RebuildRequired, item.HealthStatus);
        Assert.True(item.SafetyBlocked); Assert.False(item.CanAttemptLoad); Assert.False(item.CanRebuild);
    }

    [Fact]
    public async Task Legacy_folder_rebuild_refreshes_the_same_catalog_project_with_canonical_metadata()
    {
        var tool = Environment.GetEnvironmentVariable("BOOKSTITCH_INTEGRITY_FFPROBE");
        if (string.IsNullOrWhiteSpace(tool)) return;
        using var fixture = new ProjectIntegrityTests.Fixture(true);
        var folder = fixture.Root;
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Canonical title", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["Canonical author"], MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder, metadata);
        var mainPath = ProjectFolderLayout.GetFolderManifestPath(folder);
        var main = ProjectIntegrityValidator.Read(mainPath); main["ProjectFormatVersion"] = 0;
        new AtomicJsonStore().Save(mainPath, main, new());
        File.WriteAllText(ProjectFolderLayout.GetWorkManifestPath(folder), "{\"schemaVersion\":1,\"ProjectType\":\"FolderProject\"}");
        var probes = new AudioProbeBatchService(new AudioInfoService(), new FfprobeConcurrencyPolicy(), new FfprobeCoordinator());
        Assert.Equal(ProjectHealthStatus.RebuildRequired, (await new ProjectIntegrityService(probes).InspectAsync(folder, new(tool))).Health);
        var index = new ProjectIndexService();
        var before = Assert.Single((await index.Catalog.RefreshAsync(folder, 180)).ProjectIndexItems);
        Assert.Equal(ProjectHealthStatus.RebuildRequired, before.HealthStatus); Assert.True(before.CanRebuild);
        Assert.Equal("Canonical author - Canonical title", before.DisplayName); Assert.False(before.IsSelectableProject);
        var rebuilt = await new ProjectRebuildService(probes).RebuildAsync(folder, new(tool));
        Assert.True(rebuilt.Health is ProjectHealthStatus.OK or ProjectHealthStatus.Warning);
        index.Catalog.MarkDirty();
        var after = Assert.Single((await index.Catalog.RefreshAsync(folder, 180)).ProjectIndexItems);
        Assert.NotSame(before, after); Assert.Equal(before.ProjectFolder, after.ProjectFolder);
        Assert.Equal(before.DisplayName, after.DisplayName); Assert.True(after.IsSelectableProject);
        Assert.True(after.CanAttemptLoad); Assert.False(after.CanRebuild); Assert.Equal(rebuilt.Health, after.HealthStatus);
    }

    private static void WriteJson(string path, ExportWorkManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));
    }
}
