namespace BookStitch.Services;

/// <summary>Lightweight run counters on the existing performance trace; no per-file journal spam.</summary>
public sealed class ProjectIntegrityPerformance
{
    private readonly PerformanceTraceService? _trace;
    private PerformanceOperationScope _operation;
    private readonly Dictionary<string,long> _counters=new();
    public ProjectIntegrityPerformance(PerformanceTraceService? trace)=>_trace=trace;
    public void Begin()
    {
        _operation=_trace?.StartOperation("project.integrity.sweep")??default;
        foreach(var name in new[]{"Projects","FilesProbed","FilesHashed","ProbeBytes","HashBytes","Repairs","RebuildRequired","Damaged","MaxFfprobeConcurrency"})_counters[name]=0;
    }
    public IDisposable Phase(string name)=>new PhaseCompletion(_trace?.StartPhase(_operation.OperationId,name)??default);
    public PerformanceExternalProcessContext? Processes=>_trace is null?null:new(_trace,_operation.OperationId,Purpose:"project integrity");
    public void Add(string name,long count=1){lock(_counters)_counters[name]=_counters.GetValueOrDefault(name)+count;}
    public void Maximum(string name,long value){lock(_counters)_counters[name]=Math.Max(_counters.GetValueOrDefault(name),value);}
    public void End(bool canceled=false,Exception? error=null)
    {
        foreach(var pair in _counters)_trace?.RecordMetric(_operation.OperationId,pair.Key,pair.Value);
        if(canceled)_operation.Cancel();else if(error is not null)_operation.Fail(error);else _operation.Complete();
        _operation.Dispose();
    }
    private sealed class PhaseCompletion(PerformancePhaseScope phase):IDisposable
    {public void Dispose(){phase.Complete();phase.Dispose();}}
}
