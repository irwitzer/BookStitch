namespace CoverImageDrop.Core.Policy;

public sealed record StandardCoverPolicy
{
    public int MinUsableSquarePixels { get; init; } = 500;
    public long MaxInputBytes { get; init; } = 80L * 1024 * 1024;
    public int MaxDimension { get; init; } = int.MaxValue;
    public long MaxTotalPixels { get; init; } = 80_000_000;
    public int SmallOutputSizePixels { get; init; } = 1000;
    public int LargeOutputSizePixels { get; init; } = 2000;
    public int LargeOutputMinUsableSquarePixels { get; init; } = 1500;
    public int PreferredOutputSizePixels { get; init; } = 3000;
    public int PreferredOutputMinUsableSquarePixels { get; init; } = 2250;

    public static StandardCoverPolicy Default { get; } = new();

    internal void Validate()
    {
        ValidatePositive(MaxDimension, nameof(MaxDimension));
        ValidatePositive(MinUsableSquarePixels, nameof(MinUsableSquarePixels));
        ValidatePositive(MaxInputBytes, nameof(MaxInputBytes));
        ValidatePositive(MaxTotalPixels, nameof(MaxTotalPixels));
        ValidatePositive(SmallOutputSizePixels, nameof(SmallOutputSizePixels));
        ValidatePositive(LargeOutputSizePixels, nameof(LargeOutputSizePixels));
        ValidatePositive(
            LargeOutputMinUsableSquarePixels,
            nameof(LargeOutputMinUsableSquarePixels));
        ValidatePositive(
            PreferredOutputSizePixels,
            nameof(PreferredOutputSizePixels));
        ValidatePositive(
            PreferredOutputMinUsableSquarePixels,
            nameof(PreferredOutputMinUsableSquarePixels));

        if (LargeOutputSizePixels < SmallOutputSizePixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LargeOutputSizePixels),
                LargeOutputSizePixels,
                "Large output size must be greater than or equal to small output size.");
        }

        if (PreferredOutputSizePixels < LargeOutputSizePixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PreferredOutputSizePixels),
                PreferredOutputSizePixels,
                "Preferred output size must be greater than or equal to large output size.");
        }

        if (LargeOutputMinUsableSquarePixels < MinUsableSquarePixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(LargeOutputMinUsableSquarePixels),
                LargeOutputMinUsableSquarePixels,
                "Large-output threshold must be greater than or equal to the minimum usable square.");
        }

        if (PreferredOutputMinUsableSquarePixels <
            LargeOutputMinUsableSquarePixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PreferredOutputMinUsableSquarePixels),
                PreferredOutputMinUsableSquarePixels,
                "Preferred-output threshold must be greater than or equal to the large-output threshold.");
        }
    }

    private static void ValidatePositive(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "StandardCover policy values must be greater than zero.");
        }
    }
}
