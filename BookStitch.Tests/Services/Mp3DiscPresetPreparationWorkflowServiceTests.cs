using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class Mp3DiscPresetPreparationWorkflowServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Newly_prepared_tracks_register_verified_cache_before_completion(bool background)
    {
        using var folder = new TemporaryFolder();
        var source = CreateNonEmptyFile(folder.Path, "CD 01/Track 01.mp3", "original payload");
        var track = CreateTrack("CD 01", "Track 01.mp3", 1, ProcessingActionIds.Reuse);
        track.SourceId = Guid.NewGuid();
        var preset = ExportPreset.Parse("AAC Mono 64 kbps");
        var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(folder.Path, preset.GetFolderName());
        var manifest = new ExportWorkManifest();
        var work = new WorkManifestService();
        var registered = 0;
        void Persist(Mp3DiscPresetPreparationItem item)
        {
            work.UpdateTrack(manifest, item.Index, item.Track, item.SourcePath, item.ConvertedPath, preset);
            registered++;
        }
        var service = CreateService();
        if (background)
        {
            var tasks = new List<Task>();
            using var semaphore = new SemaphoreSlim(1);
            service.StartBackgroundPreparation([track], folder.Path, preset, convertedFolder,
                (_, _) => false, semaphore, tasks, new object(), () => true, 1,
                () => Assert.Equal(1, registered), () => Assert.Fail("Unexpected preparation failure"),
                CancellationToken.None, Persist);
            await Task.WhenAll(tasks);
        }
        else
            await service.PrepareMissingTracksAsync([track], folder.Path, preset, convertedFolder,
                (_, _) => false, 1, (_, _) => Assert.Equal(1, registered), CancellationToken.None,
                persistPreparedTrack: Persist);
        Assert.Equal(1, registered);
        var converted = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, source, track);
        Assert.True(work.CanReuseConvertedTrack(manifest, 0, track, source, converted, preset));
        Assert.False(work.CanReuseConvertedTrack(manifest, 0, track, source, converted, ExportPreset.Parse("AAC Stereo 192 kbps")));
    }
    [Fact]
    public async Task PrepareMissingTracksAsync_CopiesTakeOverTracksAndReportsProgress()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = folder.Path;
        var sourcePath = CreateNonEmptyFile(projectFolder, "CD 01/Track 01.mp3", "payload 1");
        var convertedFolder = Path.Combine(projectFolder, "converted", "aac_stereo_128k");
        var track = CreateTrack("CD 01", "Track 01.mp3", 1, processingAction: "Übernehmen");
        var service = CreateService();
        var progress = new List<(int Completed, int Total)>();

        var completed = await service.PrepareMissingTracksAsync(
            new[] { track },
            projectFolder,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            convertedFolder,
            (_, _) => false,
            maxParallel: 1,
            (done, total) => progress.Add((done, total)),
            CancellationToken.None);

        var expectedConvertedPath = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, sourcePath, track);
        Assert.Equal(1, completed);
        Assert.True(File.Exists(expectedConvertedPath));
        Assert.Equal("payload 1", await File.ReadAllTextAsync(expectedConvertedPath));
        Assert.Equal(new[] { (1, 1) }, progress);
    }

    [Fact]
    public async Task PrepareMissingTracksAsync_RechecksReuseBeforePreparing()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = folder.Path;
        CreateNonEmptyFile(projectFolder, "CD 01/Track 01.mp3", "payload 1");
        var convertedFolder = Path.Combine(projectFolder, "converted", "aac_stereo_128k");
        var track = CreateTrack("CD 01", "Track 01.mp3", 1, processingAction: "Übernehmen");
        var service = CreateService();
        var callCount = 0;

        var completed = await service.PrepareMissingTracksAsync(
            new[] { track },
            projectFolder,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            convertedFolder,
            (_, _) => Interlocked.Increment(ref callCount) >= 2,
            maxParallel: 1,
            onProgress: null,
            CancellationToken.None);

        Assert.Equal(1, completed);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task PrepareMissingTracksAsync_ForwardsPerformanceContextToAudioPreparation()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = CreateNonEmptyFile(folder.Path, "CD 01/Track 01.mp3", "payload");
        var convertedFolder = Path.Combine(folder.Path, "converted", "aac_stereo_128k");
        var track = CreateTrack("CD 01", Path.GetFileName(sourcePath), 1, processingAction: "Konvertieren");
        var processor = new RecordingAudioExportProcessingService();
        var service = new Mp3DiscPresetPreparationWorkflowService(
            new Mp3DiscPreparationService(),
            processor,
            () => "ffmpeg");
        var context = CreatePerformanceContext();

        var completed = await service.PrepareMissingTracksAsync(
            [track],
            folder.Path,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            convertedFolder,
            (_, _) => false,
            maxParallel: 1,
            onProgress: null,
            CancellationToken.None,
            context);

        Assert.Equal(1, completed);
        Assert.Same(context, processor.LastPreparationContext);
    }

    [Fact]
    public async Task StartBackgroundPreparation_AddsWorkerTaskAndCompletesCandidate()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = folder.Path;
        var sourcePath = CreateNonEmptyFile(projectFolder, "CD 01/Track 01.mp3", "payload 1");
        var convertedFolder = Path.Combine(projectFolder, "converted", "aac_stereo_128k");
        var track = CreateTrack("CD 01", "Track 01.mp3", 1, processingAction: "Übernehmen");
        var service = CreateService();
        var tasks = new List<Task>();
        var syncRoot = new object();
        using var semaphore = new SemaphoreSlim(1);
        var completed = 0;
        var failed = 0;

        service.StartBackgroundPreparation(
            new[] { track },
            projectFolder,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            convertedFolder,
            (_, _) => false,
            semaphore,
            tasks,
            syncRoot,
            () => true,
            workerCount: 1,
            () => Interlocked.Increment(ref completed),
            () => Interlocked.Increment(ref failed),
            CancellationToken.None);

        Task[] snapshot;
        lock (syncRoot)
        {
            snapshot = tasks.ToArray();
        }

        var task = Assert.Single(snapshot);
        await task;

        var expectedConvertedPath = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, sourcePath, track);
        Assert.Equal(1, completed);
        Assert.Equal(0, failed);
        Assert.True(File.Exists(expectedConvertedPath));
        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public void StartBackgroundPreparation_DoesNotAddTasksWhenNothingIsMissing()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = folder.Path;
        var sourcePath = CreateNonEmptyFile(projectFolder, "CD 01/Track 01.mp3", "payload 1");
        var convertedFolder = Path.Combine(projectFolder, "converted", "aac_stereo_128k");
        var track = CreateTrack("CD 01", "Track 01.mp3", 1, processingAction: "Übernehmen");
        var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(convertedFolder, sourcePath, track);
        var service = CreateService();
        var tasks = new List<Task>();
        var syncRoot = new object();
        using var semaphore = new SemaphoreSlim(1);

        service.StartBackgroundPreparation(
            new[] { track },
            projectFolder,
            ExportPreset.Parse("AAC Stereo 128 kbps"),
            convertedFolder,
            (_, candidateConvertedPath) => candidateConvertedPath == convertedPath,
            semaphore,
            tasks,
            syncRoot,
            () => true,
            workerCount: 1,
            () => { },
            () => { },
            CancellationToken.None);

        Assert.Empty(tasks);
    }

    private static Mp3DiscPresetPreparationWorkflowService CreateService()
    {
        return new Mp3DiscPresetPreparationWorkflowService(
            new Mp3DiscPreparationService(),
            new AacExportProcessingService(),
            () => string.Empty);
    }

    private static PerformanceExternalProcessContext CreatePerformanceContext() =>
        new(new PerformanceTraceService(enabled: true), Guid.NewGuid());

    private static TrackInfo CreateTrack(string relativeFolder, string fileName, int trackNumber, string processingAction)
    {
        return new TrackInfo
        {
            Index = trackNumber,
            DiscNumber = 1,
            TrackNumber = trackNumber,
            RelativeFolder = relativeFolder,
            FileName = fileName,
            Extension = Path.GetExtension(fileName).TrimStart('.'),
            SampleRateHz = 44100,
            ProcessingAction = processingAction
        };
    }

    private static string CreateNonEmptyFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}
