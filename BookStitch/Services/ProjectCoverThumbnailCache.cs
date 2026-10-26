using System.IO;
using System.Windows.Media.Imaging;

namespace BookStitch.Services;

/// <summary>Bounded, frozen browser thumbnails. Never retains full-size originals.</summary>
public sealed class ProjectCoverThumbnailCache
{
    public static ProjectCoverThumbnailCache Shared { get; } = new();
    private readonly object _gate = new();
    private readonly Dictionary<(string Path, long WriteTicks, long Length, int Width), BitmapImage> _images = new();
    public Func<string, PerformanceOperationScope>? StartOperation { get; set; }

    public BitmapImage Load(Uri uri, int width = 320)
    {
        using var operation = StartOperation?.Invoke("project.catalog.cover-preview") ?? default;
        try
        {
            lock (_gate)
            {
                var file = uri.IsFile ? new FileInfo(uri.LocalPath) : null;
                var path = file?.FullName.ToUpperInvariant() ?? uri.AbsoluteUri;
                var key = (path, file?.LastWriteTimeUtc.Ticks ?? 0, file?.Length ?? 0, width);
                foreach (var old in _images.Keys.Where(old => old.Path == path && old != key).ToArray())
                    _images.Remove(old);
                if (_images.TryGetValue(key, out var cached)) { operation.Complete(); return cached; }
                if (uri.IsFile) CoverInputSafety.ValidateFile(uri.LocalPath);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = width;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = uri;
                image.EndInit();
                image.Freeze();
                if (_images.Count >= 64) _images.Remove(_images.Keys.First());
                _images[key] = image;
                operation.Complete();
                return image;
            }
        }
        catch (Exception error) { operation.Fail(error); throw; }
    }

    public void InvalidateDeletedFiles()
    {
        lock (_gate)
            foreach (var key in _images.Keys.Where(key => Path.IsPathFullyQualified(key.Path) && !File.Exists(key.Path)).ToArray())
                _images.Remove(key);
    }
}
