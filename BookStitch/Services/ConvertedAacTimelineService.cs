using BookStitch.Models;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class ConvertedAacTimelineService
{
    // Analyze distinct payloads with bounded concurrency, then restore the exact
    // requested order (including deliberate repetitions of the same track).
    internal static async Task<IReadOnlyList<ConvertedAacTiming>> AnalyzeManyAsync(
        IReadOnlyList<string> paths, int parallelism,
        Func<string, CancellationToken, Task<ConvertedAacTiming>> analyze,
        Action<int, int>? reportProgress, CancellationToken token)
    {
        var normalized = paths.Select(Path.GetFullPath).ToArray();
        var unique = normalized.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, ConvertedAacTiming>(StringComparer.OrdinalIgnoreCase);
        var progressLock = new object();
        var completed = 0;
        reportProgress?.Invoke(0, unique.Length);
        await Parallel.ForEachAsync(unique, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(parallelism, 1, 4),
            CancellationToken = token
        }, async (path, cancellationToken) =>
        {
            var timing = await analyze(path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            results[path] = timing;
            lock (progressLock)
                reportProgress?.Invoke(++completed, unique.Length);
        }).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return normalized.Select(path => results[path]).ToArray();
    }

    private static InvalidDataException InvalidTimeline(string detail) =>
        new(LocalizationText.Get("AacTimeline.Invalid"), new InvalidDataException(detail));

    public static async Task<ConvertedAacTiming> AnalyzeAsync(
        string path, string ffprobePath, ConvertedAacTiming? cached, CancellationToken token)
    {
        var info = new FileInfo(path);
        token.ThrowIfCancellationRequested();
        if (cached is { IsValid: true } && cached.Length == info.Length &&
            cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
            return cached;

        var runner = new FfprobeRunnerService();
        var streamResult = await runner.RunAsync(ffprobePath,
            ["-v", "error", "-select_streams", "a:0", "-show_entries",
             "stream=codec_name,time_base,sample_rate", "-of", "json", path],
            TimeSpan.FromMinutes(2), token, runBelowNormalPriority: true);
        token.ThrowIfCancellationRequested();
        if (streamResult.ExitCode != 0 || streamResult.TimedOut || streamResult.Canceled)
            throw InvalidTimeline("AAC stream analysis failed: " + streamResult.Stderr);
        using var streamDocument = JsonDocument.Parse(streamResult.Stdout);
        var stream = streamDocument.RootElement.GetProperty("streams").EnumerateArray().Single();
        var accumulator = new PacketAccumulator(stream, info.Length, info.LastWriteTimeUtc.Ticks);
        // Compact packet records are consumed as they arrive; no packet list or JSON DOM is retained.
        // The timeout detects stalled output rather than rejecting a productive multi-hour scan.
        var result = await runner.RunAsync(ffprobePath,
            ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_entries",
             "packet=pts,dts,duration:packet_side_data=discard_padding", "-of", "compact", path],
            TimeSpan.FromMinutes(2), token, stdoutLineHandler: accumulator.AddCompactPacket,
            useInactivityTimeout: true, runBelowNormalPriority: true);
        token.ThrowIfCancellationRequested();
        if (result.ExitCode != 0 || result.TimedOut || result.Canceled)
            throw InvalidTimeline("AAC packet timeline analysis failed: " + result.Stderr);
        var timing = accumulator.Complete();
        info.Refresh();
        if (info.Length != timing.Length || info.LastWriteTimeUtc.Ticks != timing.LastWriteUtcTicks)
            throw new IOException(LocalizationText.Get("AacTimeline.FileChanged"));
        return timing;
    }

    public static ConvertedAacTiming Parse(string json, long length, long modified)
    {
        using var document = JsonDocument.Parse(json);
        var accumulator = new PacketAccumulator(document.RootElement.GetProperty("streams").EnumerateArray().Single(), length, modified);
        foreach (var packet in document.RootElement.GetProperty("packets").EnumerateArray())
        {
            long discard = 0;
            if (packet.TryGetProperty("side_data_list", out var sides))
                foreach (var side in sides.EnumerateArray())
                    if (side.TryGetProperty("discard_padding", out var padding)) discard = checked(discard + padding.GetInt64());
            accumulator.Add(packet.GetProperty("pts").GetInt64(), packet.GetProperty("dts").GetInt64(),
                packet.GetProperty("duration").GetInt64(), discard);
        }
        return accumulator.Complete();
    }

    internal sealed class PacketAccumulator
    {
        private readonly long _numerator, _denominator, _rate, _length, _modified;
        private long _first = long.MaxValue, _end = long.MinValue, _previous = long.MinValue;

        public PacketAccumulator(JsonElement stream, long length, long modified)
        {
            if (stream.GetProperty("codec_name").GetString() != "aac") throw InvalidTimeline("The converted stream is not AAC.");
            var timeBase = stream.GetProperty("time_base").GetString()!.Split('/');
            _numerator = long.Parse(timeBase[0], CultureInfo.InvariantCulture);
            _denominator = long.Parse(timeBase[1], CultureInfo.InvariantCulture);
            _rate = long.Parse(stream.GetProperty("sample_rate").GetString()!, CultureInfo.InvariantCulture);
            if (_numerator <= 0 || _denominator <= 0 || _rate <= 0) throw InvalidTimeline("Invalid AAC time base.");
            (_length, _modified) = (length, modified);
        }

        public void AddCompactPacket(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (!line.StartsWith("packet|", StringComparison.Ordinal)) throw InvalidTimeline("Unexpected AAC packet record.");
            long? pts = null, dts = null, duration = null;
            long discard = 0;
            foreach (var field in line.Split('|').Skip(1))
            {
                var separator = field.IndexOf('=');
                if (separator < 0) continue;
                var key = field[..separator];
                if (key is not ("pts" or "dts" or "duration" or "discard_padding")) continue;
                var value = long.Parse(field.AsSpan(separator + 1), CultureInfo.InvariantCulture);
                switch (key)
                {
                    case "pts": pts = value; break;
                    case "dts": dts = value; break;
                    case "duration": duration = value; break;
                    case "discard_padding": discard = checked(discard + value); break;
                }
            }
            if (pts is null || dts is null || duration is null) throw InvalidTimeline("Incomplete AAC packet record.");
            Add(pts.Value, dts.Value, duration.Value, discard);
        }

        public void Add(long pts, long dts, long duration, long discard)
        {
            if (pts != dts || dts <= _previous || duration <= 0) throw InvalidTimeline("Invalid AAC packet timeline.");
            _previous = dts;
            // Stream-copy retains the coded samples that the source edit list discards.
            var discardUnits = (decimal)discard * _denominator / _rate / _numerator;
            if (discard < 0 || discardUnits != decimal.Truncate(discardUnits)) throw InvalidTimeline("Invalid AAC discard padding.");
            _first = Math.Min(_first, pts);
            _end = Math.Max(_end, checked(pts + duration + (long)discardUnits));
        }

        public ConvertedAacTiming Complete()
        {
            if (_first == long.MaxValue) throw InvalidTimeline("Empty AAC packet timeline.");
            var timing = new ConvertedAacTiming(1, _length, _modified, checked(_end - _first), _numerator, _denominator);
            if (!timing.IsValid) throw InvalidTimeline("Empty or invalid AAC timeline.");
            return timing;
        }
    }

    public static IReadOnlyList<AacMergeSegment> BuildSegments(IReadOnlyList<ConvertedAacTiming> timings)
    {
        var segments = new List<AacMergeSegment>(timings.Count);
        decimal seconds = 0;
        long start = 0;
        foreach (var timing in timings)
        {
            if (!timing.IsValid)
                throw InvalidTimeline("Invalid cached AAC timeline.");
            seconds += timing.DurationSeconds;
            var end = checked((long)decimal.Round(seconds * 1_000_000, 0, MidpointRounding.AwayFromZero));
            segments.Add(new AacMergeSegment(start, end));
            start = end;
        }
        return segments;
    }

    public static IReadOnlyList<EffectiveChapterInfo> MapChapters(
        IReadOnlyList<EffectiveChapterInfo> chapters, IReadOnlyList<TrackInfo> tracks,
        IReadOnlyList<AacMergeSegment> segments)
    {
        if (tracks.Count != segments.Count)
            throw new ArgumentException("AAC segment count does not match tracks.");
        var byTrack = tracks.Select((track, index) => (track, segment: segments[index]))
            .ToDictionary(item => item.track, item => item.segment);
        return chapters.Select(chapter =>
        {
            var segment = byTrack[chapter.SourceTrack];
            return chapter with
            {
                StartTicks = checked(segment.StartMicroseconds * 10 + chapter.SourceStartTicks),
                EndTicks = chapter.IsSynthetic ? checked(segment.EndMicroseconds * 10) :
                    checked(segment.StartMicroseconds * 10 + chapter.SourceEndTicks)
            };
        }).ToArray();
    }
}

public sealed record AacMergeSegment(long StartMicroseconds, long EndMicroseconds)
{
    public string ConcatDuration => ((EndMicroseconds - StartMicroseconds) / 1_000_000m)
        .ToString("0.000000", CultureInfo.InvariantCulture);
}
