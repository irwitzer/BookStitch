using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoverImageDrop.Core.Imaging;

internal static class CoverImageBitmapOrientation
{
    private const string ExifOrientationQuery = "/app1/ifd/{ushort=274}";

    public static ushort? TryReadExifOrientation(BitmapMetadata? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        try
        {
            return metadata.ContainsQuery(ExifOrientationQuery)
                ? Convert.ToUInt16(metadata.GetQuery(ExifOrientationQuery))
                : null;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static bool SwapsDimensions(ushort? orientation)
    {
        return orientation is 5 or 6 or 7 or 8;
    }

    public static BitmapSource Apply(BitmapFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return Apply(
            frame,
            TryReadExifOrientation(frame.Metadata as BitmapMetadata));
    }

    internal static BitmapSource Apply(BitmapSource source, ushort? orientation)
    {
        ArgumentNullException.ThrowIfNull(source);

        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new MatrixTransform(new Matrix(0, 1, 1, 0, 0, 0)),
            6 => new RotateTransform(90),
            7 => new MatrixTransform(new Matrix(0, -1, -1, 0, 0, 0)),
            8 => new RotateTransform(270),
            _ => null
        };

        if (transform is null)
        {
            return source;
        }

        var transformed = new TransformedBitmap(source, transform);
        transformed.Freeze();
        return transformed;
    }
}
