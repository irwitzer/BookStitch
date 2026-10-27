using CoverImageDrop.Acquisition;
using CoverImageDrop.Core.Policy;

namespace CoverImageDrop.Pipeline;

public sealed record CoverImagePipelineOptions
{
    public StandardCoverPolicy Policy { get; init; } = StandardCoverPolicy.Default;
    public StandardCoverOutputOptions StandardCoverOutput { get; init; } = new();
    public CoverImageDiagnosticsOptions Diagnostics { get; init; } = new();
    public DropImageAcquisitionSafetyLimits AcquisitionSafetyLimits { get; init; } =
        DropImageAcquisitionSafetyLimits.Default;
}
