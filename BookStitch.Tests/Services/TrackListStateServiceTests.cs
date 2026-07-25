using Xunit;
using BookStitch.Models;
using BookStitch.Services;

namespace BookStitch.Tests.Services;

public sealed class TrackListStateServiceTests
{
    [Fact]
    public void SaveAndApply_PreservesOrderAndExclusions()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var service = new TrackListStateService();
            var first = new TrackInfo { FilePath = Path.Combine(folder, "a.flac"), FileName = "a.flac", ChapterTitle = "001 A" };
            var second = new TrackInfo { FilePath = Path.Combine(folder, "b.flac"), FileName = "b.flac", IsExcluded = true, ExcludedChapterTitle = "002 B" };
            service.Save(folder, [second, first]);

            var applied = service.Apply(folder, [first, second]);

            Assert.Same(second, applied[0]);
            Assert.True(applied[0].IsExcluded);
            Assert.Same(first, applied[1]);
        }
        finally { Directory.Delete(folder, true); }
    }
}
