using System.Windows.Media.Imaging;
using CoverImageDrop.Core.Imaging;

namespace CoverImageDrop.Core.Normalization;

internal sealed class CoverImageOrientationService
{
    public BitmapSource Apply(BitmapFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return CoverImageBitmapOrientation.Apply(frame);
    }
}
