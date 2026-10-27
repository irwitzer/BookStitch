using CoverImageDrop.Core.Policy;

namespace CoverImageDrop.Core.Normalization;

public static class CoverImageTargetSizeSelector
{
    public static int SelectTargetSize(
        int usableSquarePixels,
        StandardCoverPolicy? policy = null)
    {
        if (usableSquarePixels <= 0)
        {
            return 0;
        }

        var activePolicy = policy ?? StandardCoverPolicy.Default;
        if (usableSquarePixels >=
            activePolicy.PreferredOutputMinUsableSquarePixels)
        {
            return activePolicy.PreferredOutputSizePixels;
        }

        return usableSquarePixels <
               activePolicy.LargeOutputMinUsableSquarePixels
            ? activePolicy.SmallOutputSizePixels
            : activePolicy.LargeOutputSizePixels;
    }
}
