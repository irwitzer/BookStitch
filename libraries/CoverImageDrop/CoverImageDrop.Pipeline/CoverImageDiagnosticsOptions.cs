namespace CoverImageDrop.Pipeline;

public sealed record CoverImageDiagnosticsOptions
{
    public bool Enabled { get; init; }
    public string? RootFolder { get; init; }
    public bool SaveInputBytes { get; init; } = true;
    public bool SaveNormalizedImage { get; init; } = true;
    public bool SaveTextArtifacts { get; init; } = true;
    public int DebugReportTextLimit { get; init; } = 2_000;
    public string SelectedSource { get; init; } = "Unbekannt";
}
