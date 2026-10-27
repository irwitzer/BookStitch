using CoverImageDrop.Core.Output;

namespace CoverImageDrop.Core.Normalization;

internal sealed record CoverImageNormalizationOptions
{
    public int? TargetSizePixels { get; init; }
    public CoverOutputFormat Format { get; init; } = CoverOutputFormat.Jpeg;

    // null means Auto. A resolved white/black matte is reported in the output only
    // when the final JPEG actually had transparency to flatten.
    public CoverImageMatte? JpegMatte { get; init; }
}
