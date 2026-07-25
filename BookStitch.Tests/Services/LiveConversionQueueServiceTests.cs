using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public class LiveConversionQueueServiceTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("20", 20)]
    [InlineData("40", 40)]
    [InlineData("999", 40)]
    [InlineData("0", 1)]
    [InlineData("-5", 1)]
    [InlineData("ungueltig", 2)]
    public void GetLiveWorkerLimit_ParsesAndClampsUserInput(string input, int expected)
    {
        var service = new LiveConversionQueueService();

        var result = service.GetLiveWorkerLimit(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Auto")]
    [InlineData("auto")]
    public void GetLiveWorkerLimit_AutoUsesConservativeFallback(string? input)
    {
        var service = new LiveConversionQueueService();

        var result = service.GetLiveWorkerLimit(input!);

        Assert.InRange(result, 1, 8);
        Assert.InRange(result, 1, 40);
    }

    [Fact]
    public void TryEnqueue_WithValidItem_AddsItemToQueue()
    {
        using var folder = new TemporaryFolder();
        var service = new LiveConversionQueueService();
        var item = CreateItem(folder.Path, "CD 01/Track 01.mp3", "converted/AAC Stereo 192 kbps/Track 01.m4a");

        var added = service.TryEnqueue(item);

        Assert.True(added);
        Assert.True(service.TryDequeue(out var dequeued));
        Assert.Equal(item, dequeued);

        var snapshot = service.CreateSnapshot();
        Assert.Equal(0, snapshot.QueuedCount);
        Assert.Equal(0, snapshot.SkippedCount);
        Assert.Equal(0, snapshot.DuplicateCount);
    }

    [Fact]
    public void TryEnqueue_WithDuplicateSourcePath_DoesNotAddSecondItem()
    {
        using var folder = new TemporaryFolder();
        var service = new LiveConversionQueueService();
        var first = CreateItem(folder.Path, "CD 01/Track 01.mp3", "converted/preset-a/Track 01.m4a");
        var duplicate = CreateItem(folder.Path, "CD 01/Track 01.mp3", "converted/preset-b/Track 01.m4a");

        Assert.True(service.TryEnqueue(first));
        Assert.False(service.TryEnqueue(duplicate));

        var snapshot = service.CreateSnapshot();
        Assert.Equal(1, snapshot.QueuedCount);
        Assert.Equal(1, snapshot.DuplicateCount);
    }

    [Fact]
    public void TryEnqueue_WhenConvertedFileAlreadyExistsAndHasContent_SkipsItem()
    {
        using var folder = new TemporaryFolder();
        var service = new LiveConversionQueueService();
        var convertedPath = System.IO.Path.Combine(folder.Path, "converted", "Track 01.m4a");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(convertedPath)!);
        System.IO.File.WriteAllText(convertedPath, "already converted");

        var item = new LiveConversionQueueItem(
            SourcePath: System.IO.Path.Combine(folder.Path, "CD 01", "Track 01.mp3"),
            ConvertedPath: convertedPath,
            PresetName: "AAC Stereo 192 kbps",
            DiscNumber: 1,
            TrackNumber: 1);

        var added = service.TryEnqueue(item);

        Assert.False(added);
        var snapshot = service.CreateSnapshot();
        Assert.Equal(0, snapshot.QueuedCount);
        Assert.Equal(1, snapshot.SkippedCount);
        Assert.Equal(0, snapshot.DuplicateCount);
    }

    [Theory]
    [InlineData("Track 01.part", "Track 01.m4a")]
    [InlineData("Track 01.copying", "Track 01.m4a")]
    [InlineData("Track 01.mp3", "Track 01.part")]
    [InlineData("Track 01.mp3", "Track 01.copying")]
    public void TryEnqueue_WithTemporaryOrPartFiles_DoesNotAddItem(string sourceFileName, string convertedFileName)
    {
        using var folder = new TemporaryFolder();
        var service = new LiveConversionQueueService();
        var item = CreateItem(folder.Path, System.IO.Path.Combine("CD 01", sourceFileName), System.IO.Path.Combine("converted", convertedFileName));

        var added = service.TryEnqueue(item);

        Assert.False(added);
        var snapshot = service.CreateSnapshot();
        Assert.Equal(0, snapshot.QueuedCount);
        Assert.Equal(0, snapshot.SkippedCount);
        Assert.Equal(0, snapshot.DuplicateCount);
    }

    [Fact]
    public void MarkCompleted_IncrementsCompletedCount()
    {
        var service = new LiveConversionQueueService();

        service.MarkCompleted();
        service.MarkCompleted();

        var snapshot = service.CreateSnapshot();
        Assert.Equal(2, snapshot.CompletedCount);
    }

    private static LiveConversionQueueItem CreateItem(string root, string sourceRelativePath, string convertedRelativePath)
    {
        return new LiveConversionQueueItem(
            SourcePath: System.IO.Path.Combine(root, sourceRelativePath),
            ConvertedPath: System.IO.Path.Combine(root, convertedRelativePath),
            PresetName: "AAC Stereo 192 kbps",
            DiscNumber: 1,
            TrackNumber: 1);
    }
}
