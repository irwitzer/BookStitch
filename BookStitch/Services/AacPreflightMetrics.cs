using System.Collections.Concurrent;
using System.Diagnostics;

namespace BookStitch.Services;

/// <summary>Per-operation counters; no paths, and no shared mutable measurement dictionaries.</summary>
internal sealed class AacPreflightMetrics(PerformanceTraceService? trace, Guid operationId)
{
    private readonly ConcurrentDictionary<string, double> _values = new(StringComparer.Ordinal);
    private readonly bool _enabled = trace?.IsEnabled == true && operationId != Guid.Empty;
    public void Add(string name, double value = 1)
    {
        if (_enabled) _values.AddOrUpdate(name, value, (_, previous) => previous + value);
    }
    public IDisposable? Measure(string name, bool planningCpu = false) => _enabled ? new Duration(this, name, planningCpu) : null;
    public void Publish()
    {
        if (!_enabled) return;
        foreach (var name in new[] { "CacheHits", "CacheMisses", "PacketMeasurements", "PacketMeasurementMilliseconds",
            "AdtsRemuxes", "FullAdtsRemuxes", "AdtsRemuxOutputBytes", "AdtsRemuxMilliseconds", "Calibrations", "CalibrationCacheHits",
            "CalibrationMilliseconds", "RoutineDecodeTests", "ZeroCandidateSkips", "PlanningProcessCpuMs", "PlanningElapsedMs" })
            _values.TryAdd(name, 0);
        foreach (var item in _values)
            trace!.RecordMetric(operationId, item.Key, item.Value,
                item.Key.EndsWith("Ms", StringComparison.Ordinal) || item.Key.EndsWith("Milliseconds", StringComparison.Ordinal) ? "ms" : item.Key.EndsWith("Bytes", StringComparison.Ordinal) ? "bytes" : "count");
    }
    private sealed class Duration : IDisposable
    {
        private readonly AacPreflightMetrics _owner;
        private readonly string _name;
        private readonly long _start = Stopwatch.GetTimestamp();
        private readonly Process? _process;
        private readonly TimeSpan _cpu;
        public Duration(AacPreflightMetrics owner, string name, bool planningCpu)
        {
            _owner = owner; _name = name;
            if (planningCpu)
            {
                try { _process = Process.GetCurrentProcess(); _cpu = _process.TotalProcessorTime; }
                catch (InvalidOperationException) { _process?.Dispose(); _process = null; }
                catch (System.ComponentModel.Win32Exception) { _process?.Dispose(); _process = null; }
            }
        }
        public void Dispose()
        {
            _owner.Add(_name, Stopwatch.GetElapsedTime(_start).TotalMilliseconds);
            if (_process is null) return;
            try { _owner.Add("PlanningProcessCpuMs", (_process.TotalProcessorTime - _cpu).TotalMilliseconds); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { _process.Dispose(); }
        }
    }
}
