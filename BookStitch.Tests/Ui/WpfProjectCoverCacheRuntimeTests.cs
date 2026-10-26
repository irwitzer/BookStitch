using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Fact]
    public void Browser_cover_cache_reuses_small_frozen_images_and_invalidates_changed_or_deleted_files()
    {
        RunOnStaThread(() =>
        {
            using var folder = new TemporaryFolder();
            var path = Path.Combine(folder.Path, "cover.png");
            static void Write(string path, byte color)
            {
                var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Gray8, null,
                    new byte[] { color, color, color, color }, 2);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using var output = File.Create(path);
                encoder.Save(output);
            }
            Write(path, 10);
            var cache = new ProjectCoverThumbnailCache();
            var uri = new Uri(path);
            var first = cache.Load(uri);
            Assert.True(first.IsFrozen);
            Assert.Equal(320, first.DecodePixelWidth);
            Assert.Same(first, cache.Load(uri));
            Write(path, 200);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            Assert.NotSame(first, cache.Load(uri));
            File.Delete(path);
            cache.InvalidateDeletedFiles();
            Assert.ThrowsAny<Exception>(() => cache.Load(uri));
        });
    }
}
