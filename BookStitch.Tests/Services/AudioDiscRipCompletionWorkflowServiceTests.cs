using System.IO;

using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscRipCompletionWorkflowServiceTests
{
    [Fact]
    public async Task RunAsync_LoadsRippedTracksAndAppliesCompletionStateInOrder()
    {
        using var folder = new TemporaryFolder();
        var manifest = CreateManifest(folder.Path);
        var tracks = new List<TrackInfo>
        {
            new()
            {
                FileName = "0001_001_Track.flac",
                Warning = "Noch nicht gerippt",
                ProcessingAction = "FLAC rippen"
            }
        };
        var calls = new List<string>();
        var service = new AudioDiscRipCompletionWorkflowService(new AudioDiscProjectService());

        var result = await service.RunAsync(new AudioDiscRipCompletionWorkflowRequest(
            manifest,
            tracks,
            new AudioDiscLiveConversionSessionSnapshot(1, 1, 0, 0, 0, 0),
            path =>
            {
                calls.Add($"load:{path}");
                return Task.CompletedTask;
            },
            path => calls.Add($"state:{path}"),
            () => calls.Add("indexes"),
            () => calls.Add("view"),
            () => calls.Add("preview"),
            () => calls.Add("ui")));

        var rippedFolder = Path.Combine(folder.Path, AudioDiscProjectService.RippedFolderName);
        Assert.Equal(rippedFolder, result.RippedFolder);
        Assert.Equal(
            new[]
            {
                $"load:{rippedFolder}",
                $"state:{folder.Path}",
                "indexes",
                "view",
                "preview",
                "ui"
            },
            calls);
        Assert.Equal(1, tracks[0].Index);
        Assert.Equal(1, tracks[0].DiscNumber);
        Assert.Equal(1, tracks[0].TrackNumber);
        Assert.Equal("Autor", tracks[0].Artist);
        Assert.Equal("Kapitel 1", tracks[0].ChapterTitle);
        Assert.Equal(string.Empty, tracks[0].Warning);
    }

    [Fact]
    public async Task RunAsync_ReportsSuccessfulParallelConversionSummary()
    {
        using var folder = new TemporaryFolder();
        var manifest = CreateManifest(folder.Path);
        manifest.RipDuration = TimeSpan.FromMinutes(12);
        var service = new AudioDiscRipCompletionWorkflowService(new AudioDiscProjectService());

        var result = await service.RunAsync(CreateRequest(
            manifest,
            new AudioDiscLiveConversionSessionSnapshot(4, 3, 0, 0, 0, 1)));

        Assert.Equal("Alle 1 Audio-CDs vollständig nach FLAC gerippt.", result.StatusText);
        Assert.Contains("00:12:00", result.ProgressText, StringComparison.Ordinal);
        Assert.Contains("3 AAC-Track(s)", result.ProgressText, StringComparison.Ordinal);
        Assert.Contains("1 vorhandene AAC-Track(s)", result.ProgressText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ReportsFailedAndCanceledConversionsForExportRetry()
    {
        using var folder = new TemporaryFolder();
        var manifest = CreateManifest(folder.Path);
        var service = new AudioDiscRipCompletionWorkflowService(new AudioDiscProjectService());

        var result = await service.RunAsync(CreateRequest(
            manifest,
            new AudioDiscLiveConversionSessionSnapshot(4, 1, 2, 1, 0, 0)));

        Assert.Contains("3 werden im Export erneut versucht", result.ProgressText, StringComparison.Ordinal);
    }

    private static AudioDiscRipCompletionWorkflowRequest CreateRequest(
        AudioDiscProjectManifest manifest,
        AudioDiscLiveConversionSessionSnapshot snapshot)
    {
        return new AudioDiscRipCompletionWorkflowRequest(
            manifest,
            new List<TrackInfo>(),
            snapshot,
            _ => Task.CompletedTask,
            _ => { },
            () => { },
            () => { },
            () => { },
            () => { });
    }

    private static AudioDiscProjectManifest CreateManifest(string projectFolder)
    {
        return new AudioDiscProjectManifest
        {
            ProjectFolder = projectFolder,
            Author = "Autor",
            TotalDiscs = 1,
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = 1,
                            DiscNumber = 1,
                            TrackNumber = 1,
                            FileName = "0001_001_Track.flac",
                            ChapterTitle = "Kapitel 1",
                            Status = AudioDiscTrackStatus.Ripped
                        }
                    ]
                }
            ]
        };
    }
}
