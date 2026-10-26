using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectFormatSweepServiceTests
{
    [Fact]
    public void Empty_root_baseline_survives_restart_and_new_projects_without_claiming_media_checks()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "software-settings", ProjectFormatSweepService.StateFileName);
        IReadOnlyList<string> projects = [];
        var service = new ProjectFormatSweepService(path, _ => projects,
            _ => throw new Exception("Initialization must not upgrade projects"));
        Assert.True(service.TryInitializeEmptyRoot(folder.Path));
        var state = service.ReadState()!;
        Assert.Equal(Path.GetFullPath(folder.Path), state.WorkingRoot);
        Assert.Equal(ProjectFormat.CurrentProjectFormatVersion, state.LastProjectFormatSweepVersion);
        Assert.Equal(ProjectIntegrityContract.CurrentVersion, state.LastIntegritySweepVersion);
        Assert.True(state.DiscoveryCompleted);
        Assert.Equal(ProjectFormatSweepRunStatus.Completed, state.LastRun.RunStatus);
        Assert.Equal(0, state.LastRun.TotalProjects);
        Assert.Equal(0, state.LastRun.ProcessedProjects);
        Assert.Empty(state.PendingProjects);
        var before = File.ReadAllBytes(path);
        projects = [Path.Combine(folder.Path, "new-project")];
        var restarted = new ProjectFormatSweepService(path, _ => projects);
        Assert.False(restarted.TryInitializeEmptyRoot(folder.Path));
        Assert.False(restarted.ShouldStartAutomaticSweep(folder.Path,
            () => throw new Exception("Current baseline must not prompt")));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Existing_project_without_marker_is_not_initialized_or_modified()
    {
        using var folder = new TemporaryFolder();
        var project = Path.Combine(folder.Path, "legacy-project");
        Directory.CreateDirectory(ProjectFolderLayout.GetSettingsFolder(project));
        var manifest = ProjectFolderLayout.GetWorkManifestPath(project);
        File.WriteAllText(manifest, "{}");
        var path = Path.Combine(folder.Path, "software-settings", ProjectFormatSweepService.StateFileName);
        var service = new ProjectFormatSweepService(path, _ => [project]);
        Assert.False(service.TryInitializeEmptyRoot(folder.Path));
        Assert.False(File.Exists(path));
        Assert.Equal("{}", File.ReadAllText(manifest));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"LastProjectFormatSweepVersion\":0}")]
    public void Initialization_never_replaces_an_existing_marker_even_if_old_or_damaged(string content)
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "sweep.json");
        File.WriteAllText(path, content);
        var service = new ProjectFormatSweepService(path,
            _ => throw new Exception("Existing state must not trigger discovery"));
        Assert.False(service.TryInitializeEmptyRoot(folder.Path));
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void Failed_initial_discovery_does_not_certify_an_empty_root_and_can_be_retried()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "sweep.json");
        var service = new ProjectFormatSweepService(path, _ => throw new IOException("unreadable"));
        Assert.Throws<IOException>(() => service.TryInitializeEmptyRoot(folder.Path));
        Assert.False(File.Exists(path));
        Assert.True(new ProjectFormatSweepService(path, _ => []).TryInitializeEmptyRoot(folder.Path));
    }

    [Fact]
    public void Empty_startup_discovery_never_confirms_or_persists_and_later_projects_still_prompt()
    {
        using var folder=new TemporaryFolder();var path=Path.Combine(folder.Path,"sweep.json");
        IReadOnlyList<string> projects=[];var confirmations=0;var upgrades=0;
        var service=new ProjectFormatSweepService(path,_=>projects,p=>{upgrades++;return new(p,ProjectUpgradeOutcome.OK,1);});
        Assert.False(service.ShouldStartAutomaticSweep(folder.Path,()=>{confirmations++;return true;}));
        Assert.Equal(0,confirmations);Assert.Equal(0,upgrades);Assert.False(File.Exists(path));
        projects=[Path.Combine(folder.Path,"project")];
        Assert.True(service.ShouldStartAutomaticSweep(folder.Path,()=>{confirmations++;return true;}));
        Assert.Equal(1,confirmations);Assert.Equal(0,upgrades);Assert.False(File.Exists(path));
    }

    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public void Startup_confirmation_is_required_for_pending_versions_and_never_advances_markers(bool currentFormat,bool currentIntegrity)
    {
        using var folder=new TemporaryFolder();var path=Path.Combine(folder.Path,"sweep.json");
        new AtomicJsonStore().Save(path,new ProjectFormatSweepState {LastProjectFormatSweepVersion=currentFormat?ProjectFormat.CurrentProjectFormatVersion:0,
            LastIntegritySweepVersion=currentIntegrity?ProjectIntegrityContract.CurrentVersion:0},new());
        var before=File.ReadAllText(path);var confirmations=0;
        var service=new ProjectFormatSweepService(path,_=>[Path.Combine(folder.Path,"project")]);
        Assert.False(service.ShouldStartAutomaticSweep(folder.Path, ()=>{confirmations++;return false;}));
        Assert.Equal(currentFormat&&currentIntegrity?0:1,confirmations);Assert.Equal(before,File.ReadAllText(path));
        var nextStartup=new ProjectFormatSweepService(path,_=>[Path.Combine(folder.Path,"project")]);
        Assert.Equal(!currentFormat||!currentIntegrity,nextStartup.ShouldStartAutomaticSweep(folder.Path, ()=>{confirmations++;return true;}));
        Assert.Equal(before,File.ReadAllText(path));Assert.Equal(currentFormat&&currentIntegrity?0:2,confirmations);
    }

    [Fact]
    public async Task FailedDiscovery_RetryDiscoversAgainAndDoesNotTreatFailureAsEmptySuccess()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "sweep.json");
        var failed = new ProjectFormatSweepService(path, _ => throw new IOException("Discovery failed"),
            _ => throw new Exception("Not reached"));
        await Assert.ThrowsAsync<IOException>(() => failed.RunAsync(folder.Path));
        Assert.False(failed.ReadState()!.DiscoveryCompleted);
        Assert.Equal(0, failed.ReadState()!.LastProjectFormatSweepVersion);
        var visited = 0;
        var retry = new ProjectFormatSweepService(path, _ => [Path.Combine(folder.Path, "project")], project =>
        { visited++; return new(project, ProjectUpgradeOutcome.Damaged, 0); });
        var summary = await retry.RunAsync(folder.Path);
        Assert.Equal(1, visited);
        Assert.Equal(1, summary.TotalProjects);
        Assert.Equal(ProjectFormatSweepRunStatus.Completed, summary.RunStatus);
        Assert.Equal(1, retry.ReadState()!.LastProjectFormatSweepVersion);
    }

    [Fact]
    public async Task CancelAfterSeven_CheckpointsProgressAndResumeSkipsCompletedProjects()
    {
        using var folder = new TemporaryFolder();
        var projects = Enumerable.Range(1, 12).Select(i => Path.Combine(folder.Path, "project" + i)).ToArray();
        var path = Path.Combine(folder.Path, "sweep.json");
        using var cancellation = new CancellationTokenSource();
        var visited = new List<string>();
        var first = new ProjectFormatSweepService(path, _ => projects, project =>
        {
            visited.Add(project);
            if (visited.Count == 7) cancellation.Cancel();
            return new(project, ProjectUpgradeOutcome.Upgraded, 1);
        });

        var summary = await first.RunAsync(folder.Path, cancellation.Token);
        Assert.Equal(ProjectFormatSweepRunStatus.Canceled, summary.RunStatus);
        Assert.Equal(7, summary.ProcessedProjects);
        Assert.Equal(12, summary.TotalProjects);
        Assert.Equal(0, first.ReadState()!.LastProjectFormatSweepVersion);
        Assert.Equal(7, first.ReadState()!.FinishedProjects.Count);

        var resumed = new ProjectFormatSweepService(path, _ => throw new Exception("Resume must use saved discovery"), project =>
        {
            Assert.DoesNotContain(project, visited);
            visited.Add(project);
            return new(project, ProjectUpgradeOutcome.OK, 1);
        });
        summary = await resumed.RunAsync(folder.Path);
        Assert.Equal(ProjectFormatSweepRunStatus.Completed, summary.RunStatus);
        Assert.Equal(12, summary.ProcessedProjects);
        Assert.Equal(7, summary.CountUpgraded);
        Assert.Equal(5, summary.CountOK);
        Assert.Equal(1, resumed.ReadState()!.LastProjectFormatSweepVersion);
        Assert.Empty(resumed.ReadState()!.FinishedProjects);
        Assert.Empty(resumed.ReadState()!.PendingProjects);
    }

    [Fact]
    public async Task CompleteWithDamagedAndConflictingProjects_StillPublishesSweepMarker()
    {
        using var folder = new TemporaryFolder();
        var outcomes = Enum.GetValues<ProjectUpgradeOutcome>();
        var service = new ProjectFormatSweepService(Path.Combine(folder.Path, "sweep.json"),
            _ => outcomes.Select(v => Path.Combine(folder.Path, v.ToString())).ToArray(),
            project => new(project, Enum.Parse<ProjectUpgradeOutcome>(Path.GetFileName(project)), 1));
        var summary = await service.RunAsync(folder.Path);
        Assert.Equal(ProjectFormatSweepRunStatus.Completed, summary.RunStatus);
        Assert.Equal(6, summary.ProcessedProjects);
        Assert.Equal(1, summary.CountDamaged);
        Assert.Equal(1, service.ReadState()!.LastProjectFormatSweepVersion);
    }

    [Fact]
    public async Task CancelledRealUpgrade_RemainsUpgradedAndResumeFinishesRemainingProject()
    {
        using var folder = new TemporaryFolder();
        var projects = Enumerable.Range(1, 2).Select(i => Path.Combine(folder.Path, "project" + i)).ToArray();
        foreach (var project in projects)
        {
            Directory.CreateDirectory(ProjectFolderLayout.GetSettingsFolder(project));
            File.WriteAllText(ProjectFolderLayout.GetWorkManifestPath(project), JsonSerializer.Serialize(new ExportWorkManifest { ProjectWorkFolder = project }));
        }
        using var cancellation = new CancellationTokenSource();
        var coordinator = new ProjectUpgradeCoordinator();
        var path = Path.Combine(folder.Path, "sweep.json");
        var service = new ProjectFormatSweepService(path, _ => projects, project =>
        {
            var result = coordinator.EnsureCurrentProjectFormat(project);
            cancellation.Cancel();
            return result;
        });
        Assert.Equal(ProjectFormatSweepRunStatus.Canceled, (await service.RunAsync(folder.Path, cancellation.Token)).RunStatus);
        Assert.Equal(ProjectUpgradeOutcome.OK, coordinator.EnsureCurrentProjectFormat(projects[0]).Outcome);
        var mainBefore = File.ReadAllBytes(ProjectFolderLayout.GetFolderManifestPath(projects[0]));
        var resumed = new ProjectFormatSweepService(path, _ => projects);
        Assert.Equal(ProjectFormatSweepRunStatus.Completed, (await resumed.RunAsync(folder.Path)).RunStatus);
        Assert.Equal(mainBefore, File.ReadAllBytes(ProjectFolderLayout.GetFolderManifestPath(projects[0])));
        Assert.Equal(ProjectUpgradeOutcome.OK, coordinator.EnsureCurrentProjectFormat(projects[1]).Outcome);
    }

    [Fact]
    public async Task UnexpectedFailure_PersistsFailedRunWithoutSuccessfulMarker()
    {
        using var folder = new TemporaryFolder();
        var service = new ProjectFormatSweepService(Path.Combine(folder.Path, "sweep.json"),
            _ => [Path.Combine(folder.Path, "one")], _ => throw new InvalidOperationException("failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(folder.Path));
        Assert.Equal(ProjectFormatSweepRunStatus.Failed, service.ReadState()!.LastRun.RunStatus);
        Assert.Equal(0, service.ReadState()!.LastRun.ProcessedProjects);
        Assert.Equal(0, service.ReadState()!.LastProjectFormatSweepVersion);
    }
}
