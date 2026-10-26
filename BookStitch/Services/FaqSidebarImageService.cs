using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using BookStitch.Models;

namespace BookStitch.Services;

// WPF-only thumbnail rendition. Originals remain untouched; small JPEG renditions
// are created once in memory and frozen for reuse across settings windows.
public static class FaqSidebarImageService
{
    public const string BrandLogoUri = "/BookStitch;component/Assets/Faq/BookMachin Bauplan.png";
    public const int ThumbnailPixels = 192;
    private static readonly Dictionary<FaqSidebarImage, BitmapSource> Cache = [];
    private static readonly Lazy<BitmapSource> BrandLogo = new(() => Load(BrandLogoUri, 256));

    public static BitmapSource GetImage(FaqSidebarImage? preview)
    {
        if (preview is null) return BrandLogo.Value;
        lock (Cache)
        {
            if (Cache.TryGetValue(preview, out var cached)) return cached;
            var source = Load(preview.Source, 320);
            var zoom = double.IsFinite(preview.Zoom) ? Math.Clamp(preview.Zoom, 1, 4) : 1;
            var side = Math.Max(1, (int)(Math.Min(source.PixelWidth, source.PixelHeight) / zoom));
            var left = Math.Clamp((int)(source.PixelWidth * preview.FocusX - side / 2d), 0, source.PixelWidth - side);
            var top = Math.Clamp((int)(source.PixelHeight * preview.FocusY - side / 2d), 0, source.PixelHeight - side);
            var crop = new CroppedBitmap(source, new Int32Rect(left, top, side, side));
            var encoder = new JpegBitmapEncoder { QualityLevel = 78 };
            encoder.Frames.Add(BitmapFrame.Create(crop));
            using var buffer = new MemoryStream();
            encoder.Save(buffer);
            buffer.Position = 0;
            var thumbnail = new BitmapImage();
            thumbnail.BeginInit();
            thumbnail.CacheOption = BitmapCacheOption.OnLoad;
            thumbnail.DecodePixelWidth = ThumbnailPixels;
            thumbnail.StreamSource = buffer;
            thumbnail.EndInit();
            thumbnail.Freeze();
            Cache.Add(preview, thumbnail);
            return thumbnail;
        }
    }

    private static BitmapSource Load(string uri, int width)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,," + uri, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = width;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
