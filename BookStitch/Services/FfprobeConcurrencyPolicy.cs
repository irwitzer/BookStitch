namespace BookStitch.Services;

public sealed class FfprobeConcurrencyPolicy
{
    public const int MinimumConcurrency = 3;
    public const int MaximumConcurrency = 10;

    public int GetRequestedConcurrency(string? configuredParallelJobs)
    {
        if (int.TryParse(configuredParallelJobs, out var explicitJobs))
            return explicitJobs;

        return Environment.ProcessorCount;
    }

    public int GetEffectiveConcurrency(int jobCount, int requestedConcurrency)
    {
        if (jobCount <= 0)
            return 0;

        var boundedConcurrency = Math.Clamp(
            requestedConcurrency,
            MinimumConcurrency,
            MaximumConcurrency);

        return Math.Min(jobCount, boundedConcurrency);
    }

    public int GetEffectiveConcurrency(int jobCount, string? configuredParallelJobs) =>
        GetEffectiveConcurrency(jobCount, GetRequestedConcurrency(configuredParallelJobs));
}
