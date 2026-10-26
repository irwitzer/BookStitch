using BookStitch.Models;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookStitch.Services;

/// <summary>Resumable core only; scheduling and migration UI deliberately remain outside this service.</summary>
public sealed class ProjectFormatSweepService
{
    public const string StateFileName = "project-format-sweep.json";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(AtomicJsonStore.PathComparer);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly string _statePath;
    private readonly Func<string, IReadOnlyList<string>> _discover;
    private readonly Func<string, ProjectUpgradeResult> _upgrade;
    private Func<string, CancellationToken, Task<ProjectIntegrityResult>>? _inspect;
    private IProgress<IntegrityProgress>? _progress;
    private bool _fullContentHashes;
    private ProjectIntegrityPerformance? _performance;
    private readonly AtomicJsonStore _store = new();

    public ProjectFormatSweepService(string statePath, Func<string, IReadOnlyList<string>> discover) : this(statePath,
        discover, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat) { }

    internal ProjectFormatSweepService(string statePath, Func<string, IReadOnlyList<string>> discover,
        Func<string, ProjectUpgradeResult> upgrade)
    {
        _statePath = Path.GetFullPath(statePath);
        _discover = discover;
        _upgrade = upgrade;
    }

    public ProjectFormatSweepService(string statePath, Func<string, IReadOnlyList<string>> discover,
        ProjectIntegrityService integrity, ProjectIntegrityOptions options, IProgress<IntegrityProgress>? progress = null)
        : this(statePath, discover)
    {
        _inspect = (folder, token) => integrity.InspectAsync(folder, options, progress, token);
        _performance=options.Performance;
        _progress = progress; _fullContentHashes = options.FullContentHashes;
    }

    public bool ShouldStartAutomaticSweep(string workingRoot, Func<bool> confirm)
    {
        var state=ReadState();
        return ((state?.LastProjectFormatSweepVersion??0)<ProjectFormat.CurrentProjectFormatVersion ||
                (state?.LastIntegritySweepVersion??0)<ProjectIntegrityContract.CurrentVersion) && _discover(workingRoot).Count > 0 && confirm();
    }

    // An empty inventory is current without inspecting any media. Never infer
    // this baseline from the age or format version of existing projects.
    public bool TryInitializeEmptyRoot(string workingRoot)
    {
        var gate = Gates.GetOrAdd(_statePath, _ => new(1, 1));
        if (!gate.Wait(0)) return false;
        try
        {
            if (File.Exists(_statePath)) return false;
            var root = Path.GetFullPath(workingRoot);
            if (_discover(root).Count != 0) return false;
            var now = DateTime.UtcNow;
            Persist(new ProjectFormatSweepState
            {
                WorkingRoot = root,
                LastProjectFormatSweepVersion = ProjectFormat.CurrentProjectFormatVersion,
                LastIntegritySweepVersion = ProjectIntegrityContract.CurrentVersion,
                IntegrityContractVersion = ProjectIntegrityContract.CurrentVersion,
                IsIntegrityRun = true,
                DiscoveryCompleted = true,
                LastRun = new()
                {
                    RunStartedUtc = now,
                    RunEndedUtc = now,
                    RunStatus = ProjectFormatSweepRunStatus.Completed
                }
            });
            return true;
        }
        finally { gate.Release(); }
    }

    public ProjectFormatSweepState? ReadState()
    {
        if (!File.Exists(_statePath)) return null;
        var json = File.ReadAllText(_statePath);
        // Read the former classification without exposing it as a current outcome.
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        if (node["FinishedProjects"] is System.Text.Json.Nodes.JsonObject finished)
            foreach (var key in finished.Select(pair => pair.Key).ToArray())
                if (finished[key]?.ToString() == "NeedsAttention") finished[key] = "Damaged";
        if (node["LastRun"] is System.Text.Json.Nodes.JsonObject run && run["CountNeedsAttention"] is { } oldCount)
        { run["CountDamaged"] = (run["CountDamaged"]?.GetValue<int>() ?? 0) + oldCount.GetValue<int>(); run.Remove("CountNeedsAttention"); }
        json = node.ToJsonString();
        PersistentJsonSchema.Read(json, "Project format sweep", _statePath);
        return JsonSerializer.Deserialize<ProjectFormatSweepState>(json, Options);
    }

    public async Task<ProjectFormatSweepSummary> RunAsync(string workingRoot, CancellationToken token = default)
    {
        var gate = Gates.GetOrAdd(_statePath, _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        _performance?.Begin();
        try
        {
            var result=await Task.Run(() => RunAsyncCore(Path.GetFullPath(workingRoot), token), CancellationToken.None).ConfigureAwait(false);
            _performance?.End(result.RunStatus==ProjectFormatSweepRunStatus.Canceled);return result;
        }
        catch(Exception ex){_performance?.End(error:ex);throw;}
        finally { gate.Release(); }
    }

    private async Task<ProjectFormatSweepSummary> RunAsyncCore(string root, CancellationToken token)
    {
        var state = ReadState();
        if (state is not null && !AtomicJsonStore.PathComparer.Equals(state.WorkingRoot, root))
            throw new InvalidOperationException("A sweep state belongs to exactly one working root.");
        if (state is null || _inspect is not null && (!state.IsIntegrityRun || state.IntegrityContractVersion != ProjectIntegrityContract.CurrentVersion || state.FullContentHashes != _fullContentHashes) || !state.DiscoveryCompleted || state.LastRun.RunStatus == ProjectFormatSweepRunStatus.Completed ||
            state.LastRun.TargetProjectFormatVersion != ProjectFormat.CurrentProjectFormatVersion)
        {
            var marker = state?.LastProjectFormatSweepVersion ?? 0;
            state = new() { WorkingRoot = root, LastProjectFormatSweepVersion = marker, LastIntegritySweepVersion = state?.LastIntegritySweepVersion ?? 0,
                IntegrityContractVersion = _inspect is null ? 0 : ProjectIntegrityContract.CurrentVersion, IsIntegrityRun = _inspect is not null, FullContentHashes = _fullContentHashes,
                LastRun = new() { RunStartedUtc = DateTime.UtcNow } };
            try { state.PendingProjects = _discover(root).Select(Path.GetFullPath).Distinct(AtomicJsonStore.PathComparer).ToList(); }
            catch
            {
                Finish(state, ProjectFormatSweepRunStatus.Failed);
                throw;
            }
            state.LastRun.TotalProjects = state.PendingProjects.Count;
            state.DiscoveryCompleted = true;
        }
        state.LastRun.RunStatus = ProjectFormatSweepRunStatus.Running;
        state.LastRun.RunEndedUtc = null;
        Persist(state);
        var previousSweepVersion = state.LastProjectFormatSweepVersion;
        try
        {
            while (state.PendingProjects.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var project = state.PendingProjects[0];
                _progress?.Report(new("Integrity.Phase.Upgrade", project, ProcessedProjects: state.LastRun.ProcessedProjects, TotalProjects: state.LastRun.TotalProjects));
                ProjectUpgradeResult result;
                if (_inspect is null) result = _upgrade(project);
                else
                {
                    var checkedProject = await _inspect(project, token).ConfigureAwait(false);
                    result = new(project, checkedProject.Health == ProjectHealthStatus.Damaged ? ProjectUpgradeOutcome.Damaged
                        : checkedProject.Health == ProjectHealthStatus.RebuildRequired ? ProjectUpgradeOutcome.RebuildRequired
                        : checkedProject.Health == ProjectHealthStatus.Warning ? ProjectUpgradeOutcome.Warning : ProjectUpgradeOutcome.OK,
                        ProjectFormat.CurrentProjectFormatVersion);
                    _performance?.Add("Projects");
                    if(checkedProject.Health==ProjectHealthStatus.Damaged)_performance?.Add("Damaged");
                    if(checkedProject.Health==ProjectHealthStatus.RebuildRequired)_performance?.Add("RebuildRequired");
                    if (checkedProject.WasUpgraded) state.LastRun.CountUpgraded++;
                    if (checkedProject.WasRepaired) state.LastRun.CountRepaired++;
                }
                // A project upgrade is an indivisible cancellation unit: checkpoint its
                // outcome before observing cancellation, even if the token changed inside it.
                state.FinishedProjects[project] = result.Outcome;
                state.PendingProjects.RemoveAt(0);
                Count(state.LastRun, result.Outcome);
                Persist(state);
            }
            state.LastProjectFormatSweepVersion = ProjectFormat.CurrentProjectFormatVersion;
            if (_inspect is not null) state.LastIntegritySweepVersion = ProjectIntegrityContract.CurrentVersion;
            state.FinishedProjects.Clear();
            Finish(state, ProjectFormatSweepRunStatus.Completed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { state.LastProjectFormatSweepVersion = previousSweepVersion; Finish(state, ProjectFormatSweepRunStatus.Canceled); }
        catch
        {
            state.LastProjectFormatSweepVersion = previousSweepVersion;
            Finish(state, ProjectFormatSweepRunStatus.Failed);
            throw;
        }
        return state.LastRun;
    }

    private void Finish(ProjectFormatSweepState state, ProjectFormatSweepRunStatus status)
    {
        state.LastRun.RunStatus = status;
        state.LastRun.RunEndedUtc = DateTime.UtcNow;
        Persist(state);
    }

    private void Persist(ProjectFormatSweepState state) => _store.Save(_statePath, state, Options);

    private static void Count(ProjectFormatSweepSummary summary, ProjectUpgradeOutcome outcome)
    {
        summary.ProcessedProjects++;
        switch (outcome)
        {
            case ProjectUpgradeOutcome.OK: summary.CountOK++; break;
            case ProjectUpgradeOutcome.Upgraded: summary.CountUpgraded++; break;
            case ProjectUpgradeOutcome.Repaired: summary.CountRepaired++; break;
            case ProjectUpgradeOutcome.Warning: summary.CountWarning++; break;
            case ProjectUpgradeOutcome.RebuildRequired: summary.CountRebuildRequired++; break;
            case ProjectUpgradeOutcome.Damaged: summary.CountDamaged++; break;
            default: throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }
}
