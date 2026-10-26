using BookStitch.Models;

namespace BookStitch.Services;

public sealed record AudioProbeBatchRequest(int Index, string FilePath);

public sealed class AudioProbeBatchService
{
    private readonly AudioInfoService _audioInfoService;
    private readonly FfprobeConcurrencyPolicy _policy;
    private readonly FfprobeCoordinator _coordinator;

    public AudioProbeBatchService(
        AudioInfoService audioInfoService,
        FfprobeConcurrencyPolicy policy,
        FfprobeCoordinator coordinator)
    {
        _audioInfoService = audioInfoService;
        _policy = policy;
        _coordinator = coordinator;
    }

    public async Task<AudioProbeInfo?[]> ProbeAsync(
        IReadOnlyList<AudioProbeBatchRequest> requests,
        string? ffprobePath,
        string? configuredParallelJobs,
        PerformanceExternalProcessContext? performanceContext,
        IProgress<int>? progress = null,
        int initialCompleted = 0,
        CancellationToken token = default)
    {
        var results = new AudioProbeInfo?[requests.Count == 0 ? 0 : requests.Max(request => request.Index) + 1];
        if (requests.Count == 0)
            return results;

        var completed = initialCompleted;
        var maxDegreeOfParallelism = _policy.GetEffectiveConcurrency(
            requests.Count,
            configuredParallelJobs);

        if (performanceContext is not null)
        {
            performanceContext.TraceService.RecordParallelism(performanceContext.OperationId,
                configuredParallelJobs ?? "Auto", maxDegreeOfParallelism, "Probe", performanceContext.PhaseId);
        }

        try
        {
            await Parallel.ForEachAsync(
                requests,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDegreeOfParallelism,
                    CancellationToken = token
                },
                async (request, cancellationToken) =>
                {
                    using var lease = await _coordinator.EnterAsync(cancellationToken).ConfigureAwait(false);
                    results[request.Index] = await _audioInfoService.ProbeAsync(
                            request.FilePath,
                            ffprobePath,
                            performanceContext,
                            cancellationToken)
                        .ConfigureAwait(false);

                    var currentCompleted = Interlocked.Increment(ref completed);
                    progress?.Report(currentCompleted);
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return results;
        }

        return results;
    }
}
