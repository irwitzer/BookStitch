using System.Diagnostics;
using System.IO;

namespace BookStitch.Services;

/// <summary>Best-effort, session aggregates. Disabled measurements allocate nothing.</summary>
public sealed class PerformanceMeasurement : IDisposable
{
    private readonly PerformanceTraceService _trace;
    private readonly Guid _sessionId;
    private readonly string _name;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Dictionary<string, double> _counters = new(StringComparer.Ordinal);
    private bool _completed;

    internal PerformanceMeasurement(PerformanceTraceService trace, Guid sessionId, string name)
    {
        _trace = trace;
        _sessionId = sessionId;
        _name = name;
    }

    // Application-owned provider also covers short-lived manifest readers. Tests can
    // inject a trace explicitly without changing this process-wide application owner.
    public static PerformanceTraceService? ApplicationTrace { get; set; }

    public static PerformanceMeasurement? Start(string name, PerformanceTraceService? trace = null)
    {
        try { return (trace ?? ApplicationTrace)?.StartMeasurement(name); }
        catch { return null; }
    }

    public void Add(string name, double value = 1)
    {
        try { _counters[name] = _counters.GetValueOrDefault(name) + value; }
        catch { /* Diagnostics cannot affect the caller. */ }
    }

    public void FileBytes(string path)
    {
        try { Add("Bytes", new FileInfo(path).Length); }
        catch { /* Missing, locked or invalid paths provide no byte evidence. */ }
    }

    public void SerializedBytes(string json)
    {
        try { Add("SerializedBytes", System.Text.Encoding.UTF8.GetByteCount(json)); }
        catch { /* Measuring the existing serialization is best effort. */ }
    }

    public void Dispose()
    {
        if (_completed) return;
        _completed = true;
        try
        {
            _trace.RecordMeasurement(_sessionId, _name,
                Stopwatch.GetElapsedTime(_started).TotalMilliseconds, _counters);
        }
        catch { /* Diagnostics cannot affect the caller. */ }
    }
}
