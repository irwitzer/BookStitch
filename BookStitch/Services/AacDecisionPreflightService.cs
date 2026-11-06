using BookStitch.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace BookStitch.Services;

public sealed record AacDecisionOptions(AacDecisionPlan Optimized, AacDecisionPlan UserValues, AacDecisionPlan EncodeAll);

/// <summary>Project evidence, bounded supplemental measurements and ranked compatibility validation.</summary>
public sealed class AacDecisionPreflightService
{
    private static readonly ConcurrentDictionary<string, string> Calibrations = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CalibrationGates = new(StringComparer.Ordinal);
    public static string ToolchainIdentity(string ffmpeg, string ffprobe) =>
        AacToolchainIdentityCache.Identity(ffmpeg) + "/" + AacToolchainIdentityCache.Identity(ffprobe);

    public async Task<AacDecisionOptions> AnalyzeAsync(IReadOnlyList<TrackInfo> tracks, string root,
        ExportPreset user, int standardRate, string ffmpeg, string ffprobe, CancellationToken token,
        int? fixedRate = null, bool forceEncodeAll = false, AudioEvidenceCache? evidenceCache = null,
        string? configuredParallelJobs = null)
    {
        var trace = PerformanceMeasurement.ApplicationTrace;
        using var operation = trace?.StartOperation("aac.preflight", projectType: PerformanceProjectType.LocalFolder,
            workload: new PerformanceWorkloadSnapshot { TrackCount = tracks.Count(t => !t.IsExcluded) }) ?? default;
        var measurement = new AacPreflightMetrics(trace, operation.OperationId);
        var context = trace is not null && operation.IsActive
            ? new PerformanceExternalProcessContext(trace, operation.OperationId, Purpose: "aac.source.probe") : null;
        var cache = evidenceCache ?? new AudioEvidenceCache();
        var toolchain = ToolchainIdentity(ffmpeg, ffprobe);
        var probeIdentity = AudioEvidenceCache.ProbeSchemaIdentity + AacToolchainIdentityCache.Identity(ffprobe);
        var directory = Path.Combine(Path.GetTempPath(), "BookStitch", "aac-preflight", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var detailSlots = new SemaphoreSlim(Math.Clamp(new FfprobeConcurrencyPolicy().GetRequestedConcurrency(configuredParallelJobs), 1, 2));
        try
        {
            var sources = tracks.Where(track => !track.IsExcluded)
                .GroupBy(track => AudioEvidenceFileIdentity.Read(TrackPathService.GetTrackPath(root, track)).CacheKey,
                    StringComparer.OrdinalIgnoreCase).ToArray();
            measurement.Add("PhysicalFiles", sources.Length);
            trace?.UpdateOperationWorkload(operation.OperationId, new PerformanceWorkloadSnapshot
                { TrackCount = tracks.Count(track => !track.IsExcluded), FileCount = sources.Length, ProjectType = PerformanceProjectType.LocalFolder });
            var workers = new FfprobeConcurrencyPolicy().GetEffectiveConcurrency(sources.Length, configuredParallelJobs);
            measurement.Add("WorkerLimit", workers);
            trace?.RecordParallelism(operation.OperationId, configuredParallelJobs ?? "Auto", workers);
            measurement.Add("DetailWorkerLimit", Math.Clamp(new FfprobeConcurrencyPolicy().GetRequestedConcurrency(configuredParallelJobs), 1, 2));
            using (var phase = trace?.StartPhase(operation.OperationId, "aac.evidence") ?? default)
            {
                await Parallel.ForEachAsync(sources, new ParallelOptions { CancellationToken = token,
                    MaxDegreeOfParallelism = Math.Max(1, workers) }, async (group, cancellation) =>
                {
                    var source = TrackPathService.GetTrackPath(root, group.First());
                    var probe = await new AudioInfoService().ProbeAsync(source, ffprobe, context, cancellation, cache,
                        hit => measurement.Add(hit ? "CacheHits" : "CacheMisses"));
                    using var sourceLease = await cache.EnterSupplementAsync(source, cancellation);
                    if (cache.TryGet(source, probeIdentity, out var cached)) probe = cached!.Probe;
                    AudioProbeInfo? prepared = cached?.PreparedToolchainIdentity == toolchain ? cached.PreparedAacProbe : null;
                    if (!probe.Success || probe.HasProbeErrors) throw SourceError();
                    if (probe.SampleRateHz is not > 0 || probe.Channels is not > 0)
                    {
                        var deeper = await new FfprobeRunnerService().RunAsync(ffprobe,
                            ["-v", "error", "-analyzeduration", "2147483647", "-probesize", "100000000", "-show_format",
                             "-show_streams", "-show_chapters", "-show_data_hash", "sha256", "-of", "json", source],
                            TimeSpan.FromMinutes(2), cancellation, context is null ? null : context with { Purpose = "aac.source.deep" });
                        cancellation.ThrowIfCancellationRequested();
                        if (deeper.ExitCode != 0 || deeper.TimedOut || deeper.Canceled) throw SourceError();
                        probe = new AudioProbeJsonParser().Parse(source, deeper.Stdout);
                        if (AudioProbeDiagnosticService.HasAudioErrors(probe, deeper.Stderr)) throw SourceError();
                    }
                    if (!probe.Success || probe.SampleRateHz is not > 0 || probe.Channels is not > 0) throw SourceError();
                    var rawAdts = probe.IsAac && string.Equals(probe.FormatName, "aac", StringComparison.OrdinalIgnoreCase);
                    var supportedContainer = new[] { ".m4a", ".m4b", ".aac" }.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase);
                    var possible = !forceEncodeAll && supportedContainer && probe.IsAac && probe.AudioProfile == "LC" &&
                        probe.AudioStreamCount == 1 && !probe.HasNonCoverVideo && probe.SampleFormat == "fltp" &&
                        probe.Channels is 1 or 2 && probe.AudioChannelLayout == (probe.Channels == 1 ? "mono" : "stereo") &&
                        (rawAdts || (probe.AacExtradataSize > 0 && !string.IsNullOrWhiteSpace(probe.AacConfigurationId) &&
                            probe.AudioTimeBase == $"1/{probe.SampleRateHz}"));
                    if (probe.Duration is not { Ticks: > 0 } || probe.IsDurationEstimated ||
                        possible && probe.AudioBitrateBitsPerSecond is not > 0)
                    {
                        await detailSlots.WaitAsync(cancellation);
                        try { await CompleteMeasurementsAsync(probe, ffprobe, cancellation,
                            context is null ? null : context with { Purpose = "aac.source.packets" }, measurement); }
                        finally { detailSlots.Release(); }
                    }
                    if (probe.Duration is not { Ticks: > 0 } || probe.IsDurationEstimated) throw SourceError();
                    if (possible && rawAdts && prepared is null && probe.AudioBitrateBitsPerSecond is > 0)
                    {
                        await detailSlots.WaitAsync(cancellation);
                        try
                        {
                            using var remuxMeasure = PerformanceMeasurement.Start("aac.adts.probe");
                            var remux = Path.Combine(directory, $"adts-{Guid.NewGuid():N}.m4a");
                            // Only establish ASC/timebase/packet representation; no full input remux.
                            measurement.Add("AdtsRemuxes");
                            var remuxStart = Stopwatch.GetTimestamp();
                            var result = await RunAsync(ffmpeg, ["-v", "error", "-xerror", "-y", "-i", source,
                                "-t", "0.2", "-map", "0:a:0", "-vn", "-c:a", "copy", "-bsf:a", "aac_adtstoasc", remux], cancellation,
                                context is null ? null : context with { Purpose = "aac.adts.sample-remux" });
                            cancellation.ThrowIfCancellationRequested();
                            if (result.ExitCode != 0) throw SourceError();
                            measurement.Add("AdtsRemuxMilliseconds", Stopwatch.GetElapsedTime(remuxStart).TotalMilliseconds);
                            measurement.Add("AdtsRemuxOutputBytes", new FileInfo(remux).Length);
                            remuxMeasure?.FileBytes(remux);
                            prepared = await new AudioInfoService().ProbeAsync(remux, ffprobe,
                                context is null ? null : context with { Purpose = "aac.adts.sample-probe" }, cancellation);
                            if (!prepared.Success || prepared.HasProbeErrors) throw SourceError();
                            prepared.Duration = prepared.AudioStreamDuration = probe.Duration;
                            prepared.AudioBitrateBitsPerSecond = probe.AudioBitrateBitsPerSecond;
                        }
                        finally { detailSlots.Release(); }
                    }
                    var effective = possible && prepared is not null ? prepared : probe;
                    foreach (var track in group)
                    {
                        new TrackAudioProbeApplicationService().ApplySuccessfulProbe(track, effective, user, true);
                        track.AacOptimizationEligible = possible && effective.SupportsAacStreamCopy && probe.AudioBitrateBitsPerSecond is > 0;
                        cache.Update(source, probeIdentity, probe, prepared, track.SourceId, toolchain);
                    }
                });
                phase.Complete();
            }
            token.ThrowIfCancellationRequested();
            var included = tracks.Where(track => !track.IsExcluded).ToArray();
            var configurations = new Dictionary<string, string?>();
            AacDecisionPlan optimized;
            IReadOnlyList<AacDecisionPlan> bounds;
            using (measurement.Measure("PlanningElapsedMs", planningCpu: true))
            {
                optimized = AacDecisionPlanner.Build(tracks, user, standardRate, configurations);
                bounds = AacDecisionPlanner.PossibleReusePlans(tracks, user, standardRate);
            }
            if (included.All(track => !track.AacOptimizationEligible)) measurement.Add("ZeroCandidateSkips");
            if (!forceEncodeAll && optimized.ReusedCount != included.Length)
            {
                using var phase = trace?.StartPhase(operation.OperationId, "aac.compatibility") ?? default;
                var profileBounds = bounds.GroupBy(plan => AacDecisionPlanner.CalibrationKey(plan.SampleRateHz, plan.Preset))
                    .Select(group => AacDecisionPlanner.Rank(group, user, standardRate).First());
                foreach (var bound in AacDecisionPlanner.Rank(profileBounds, user, standardRate))
                {
                    if (fixedRate.HasValue && (bound.SampleRateHz != fixedRate || bound.BitrateKbps != user.BitrateKbps || bound.Channels != user.Channels)) continue;
                    if (!fixedRate.HasValue && ReferenceEquals(AacDecisionPlanner.Rank(new[] { optimized, bound }, user, standardRate).First(), optimized))
                        continue; // All later bounds are no better, including deterministic ties.
                    var key = AacDecisionPlanner.CalibrationKey(bound.SampleRateHz, bound.Preset);
                    configurations[key] = await CalibrateAsync(toolchain, bound.SampleRateHz, bound.Preset,
                        ffmpeg, ffprobe, directory, token, context, measurement);
                    using (measurement.Measure("PlanningElapsedMs", planningCpu: true))
                        optimized = AacDecisionPlanner.Build(tracks, user, standardRate, configurations);
                }
                phase.Complete();
            }
            var requested = AacDecisionPlanner.Build(tracks, user, standardRate, configurations, true, fixedRate);
            var encodeAll = AacDecisionPlanner.EncodeAll(tracks, user, fixedRate ?? standardRate, explicitChoice: true);
            if (toolchain != ToolchainIdentity(ffmpeg, ffprobe)) throw SourceError();
            measurement.Publish();
            operation.Complete();
            return new(forceEncodeAll ? encodeAll : optimized, requested, encodeAll);
        }
        catch (OperationCanceledException) { measurement.Publish(); operation.Cancel(); throw; }
        catch (Exception error) { measurement.Publish(); operation.Fail(error); throw; }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<string?> CalibrateAsync(string toolchain, int rate, ExportPreset preset, string ffmpeg,
        string ffprobe, string directory, CancellationToken token, PerformanceExternalProcessContext? context, AacPreflightMetrics owner)
    {
        // Version binds the exact encoder/filter/mapping/container options below.
        var cacheKey = toolchain + "/aac-lavfi-ipod-faststart-map-a0-v1/" + AacDecisionPlanner.CalibrationKey(rate, preset);
        var gate = CalibrationGates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            using var timing = owner.Measure("CalibrationMilliseconds");
            using var measurement = PerformanceMeasurement.Start("aac.calibration");
            if (Calibrations.TryGetValue(cacheKey, out var cached)) { measurement?.Add("CacheHits"); owner.Add("CalibrationCacheHits"); return cached.Length == 0 ? null : cached; }
            owner.Add("Calibrations");
            var path = Path.Combine(directory, $"encoder-{rate}-{preset.BitrateKbps}.m4a");
            var result = await RunAsync(ffmpeg, ["-v", "error", "-y", "-f", "lavfi", "-i",
                $"anullsrc=r={rate}:cl={(preset.Channels == 1 ? "mono" : "stereo")}", "-t", "1.5", "-map", "0:a:0", "-vn",
                "-c:a", "aac", "-b:a", $"{preset.BitrateKbps}k", "-ac", preset.Channels.ToString(CultureInfo.InvariantCulture),
                "-ar", rate.ToString(CultureInfo.InvariantCulture), "-movflags", "+faststart", "-f", "ipod", path], token,
                context is null ? null : context with { Purpose = "aac.calibration.encode" });
            token.ThrowIfCancellationRequested();
            var probe = result.ExitCode == 0 ? await new AudioInfoService().ProbeAsync(path, ffprobe,
                context is null ? null : context with { Purpose = "aac.calibration.probe" }, token) : null;
            token.ThrowIfCancellationRequested();
            var value = probe?.SupportsAacStreamCopy == true && !probe.HasProbeErrors ? probe.AacConfigurationId! : "";
            Calibrations[cacheKey] = value;
            measurement?.Add("Executions");
            return value.Length == 0 ? null : value;
        }
        finally { gate.Release(); }
    }

    private static InvalidDataException SourceError() => new(LocalizationText.Get("Aac.Plan.SourceError"));

    private static async Task<FfmpegRunResult> RunAsync(string ffmpeg, List<string> args, CancellationToken token,
        PerformanceExternalProcessContext? context)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        return await new FfmpegRunnerService().RunAsync(ffmpeg, args, timeout.Token, performanceContext: context);
    }

    private static async Task CompleteMeasurementsAsync(AudioProbeInfo probe, string ffprobe, CancellationToken token,
        PerformanceExternalProcessContext? context, AacPreflightMetrics owner)
    {
        owner.Add("PacketMeasurements");
        using var timing = owner.Measure("PacketMeasurementMilliseconds");
        using var measurement = PerformanceMeasurement.Start("aac.packet.measurement");
        var needDuration = probe.Duration is not { Ticks: > 0 } || probe.IsDurationEstimated;
        var needBitrate = probe.AudioBitrateBitsPerSecond is not > 0 || probe.IsDurationEstimated;
        decimal bytes = 0, seconds = 0;
        var validDuration = true;
        var validBytes = true;
        var timebase = probe.AudioTimeBase?.Split('/');
        var hasTimebase = timebase is { Length: 2 } && decimal.TryParse(timebase[0], NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
            decimal.TryParse(timebase[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var denominator) && denominator > 0;
        using var adts = string.Equals(probe.FormatName, "aac", StringComparison.OrdinalIgnoreCase)
            ? new FileStream(probe.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        var result = await new FfprobeRunnerService().RunAsync(ffprobe,
            ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_entries", "packet=size,duration,duration_time,pos", "-of", "compact", probe.FilePath],
            TimeSpan.FromMinutes(2), token, context, stdoutLineHandler: line =>
            {
                if (!line.StartsWith("packet|", StringComparison.Ordinal)) return;
                decimal? size = null, time = null, units = null;
                long? position = null;
                foreach (var field in line.Split('|'))
                {
                    if (field.StartsWith("size=", StringComparison.Ordinal) && decimal.TryParse(field.AsSpan(5), NumberStyles.Number, CultureInfo.InvariantCulture, out var b)) size = b;
                    if (field.StartsWith("duration_time=", StringComparison.Ordinal) && decimal.TryParse(field.AsSpan(14), NumberStyles.Number, CultureInfo.InvariantCulture, out var s)) time = s;
                    if (field.StartsWith("duration=", StringComparison.Ordinal) && decimal.TryParse(field.AsSpan(9), NumberStyles.Number, CultureInfo.InvariantCulture, out var u)) units = u;
                    if (field.StartsWith("pos=", StringComparison.Ordinal) && long.TryParse(field.AsSpan(4), out var p)) position = p;
                }
                if (hasTimebase && units is > 0)
                    time = units * decimal.Parse(timebase![0], CultureInfo.InvariantCulture) / decimal.Parse(timebase[1], CultureInfo.InvariantCulture);
                if (time is > 0) seconds += time.Value; else validDuration = false;
                if (adts is not null && needBitrate)
                {
                    Span<byte> header = stackalloc byte[7];
                    if (position is not >= 0 || RandomAccess.Read(adts.SafeFileHandle, header, position.Value) != 7 ||
                        header[0] != 0xff || (header[1] & 0xf6) != 0xf0 ||
                        ((header[3] & 3) << 11 | header[4] << 3 | header[5] >> 5) != size)
                        validBytes = false;
                    else size -= (header[1] & 1) == 1 ? 7 : 9;
                }
                if (size is > 0) bytes += size.Value; else validBytes = false;
            }, useInactivityTimeout: false);
        token.ThrowIfCancellationRequested();
        if (AudioProbeDiagnosticService.HasAudioErrors(probe, result.Stderr) && result.ExitCode == 0) throw SourceError();
        if (result.ExitCode != 0 || result.TimedOut || result.Canceled || !validDuration || seconds <= 0)
        {
            if (needDuration) probe.Duration = probe.AudioStreamDuration = null;
            return;
        }
        if (needBitrate) probe.AudioBitrateBitsPerSecond = validBytes ? bytes * 8m / seconds : null;
        if (needDuration)
        {
            probe.Duration = probe.AudioStreamDuration = TimeSpan.FromTicks(decimal.ToInt64(decimal.Round(seconds * TimeSpan.TicksPerSecond)));
            probe.IsDurationEstimated = false;
            probe.DurationPrecision = "Packets";
        }
    }
}
