using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Pipeline;

public sealed record StandardCoverOutputOptions
{
    public CoverTargetSizeMode TargetSize { get; init; } = CoverTargetSizeMode.Auto;
    public CoverOutputFormat Format { get; init; } = CoverOutputFormat.Jpeg;
    public JpegTransparencyMode Transparency { get; init; } = JpegTransparencyMode.Auto;
}
