namespace CoverImageDrop.Diagnostics;

public sealed record CoverImageDiagnosticOptions
{
    public bool SaveInputBytes { get; init; } = true;
    public bool SaveNormalizedImage { get; init; } = true;
    public bool SaveTextArtifacts { get; init; } = true;
    public int DebugReportTextLimit { get; init; } = 2_000;
}
