using CoverImageDrop.Core.Output;
using CoverImageDrop.Core.Policy;
using CoverImageDrop.Core.Normalization;

namespace CoverImageDrop.Pipeline;

public sealed record StandardCoverProcessingPlan(
    int TargetSizePixels,
    CoverTargetSizeMode TargetSizeMode,
    CoverOutputFormat Format,
    JpegTransparencyMode Transparency,
    bool RequiresUpscaling)
{
    public static StandardCoverProcessingPlan Resolve(
        int usableSquarePixels,
        StandardCoverOutputOptions? options = null,
        StandardCoverPolicy? policy = null)
    {
        if (usableSquarePixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(usableSquarePixels));
        }

        var activeOptions = options ?? new StandardCoverOutputOptions();
        var activePolicy = policy ?? StandardCoverPolicy.Default;
        var targetSize = activeOptions.TargetSize switch
        {
            CoverTargetSizeMode.Auto =>
                CoverImageTargetSizeSelector.SelectTargetSize(
                    usableSquarePixels,
                    activePolicy),
            CoverTargetSizeMode.Size1000 => 1000,
            CoverTargetSizeMode.Size2000 => 2000,
            CoverTargetSizeMode.Size3000 => 3000,
            _ => throw new ArgumentOutOfRangeException(
                nameof(activeOptions.TargetSize),
                activeOptions.TargetSize,
                "Unbekannter TargetSize-Modus.")
        };

        return new StandardCoverProcessingPlan(
            targetSize,
            activeOptions.TargetSize,
            activeOptions.Format,
            activeOptions.Transparency,
            usableSquarePixels < targetSize);
    }
}
