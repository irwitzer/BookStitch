using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConvertedTrackPreparationPlanServiceTests
{
    private readonly WorkManifestService _workManifestService = new();

    [Fact]
    public void Build_ReconcilesAllTracksBeforePreparationStarts()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        var convertedFolder = Path.Combine(folder.Path, "converted");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(convertedFolder);

        var preset = new ExportPreset("AAC Stereo 96 kbps", 96, 2);
        var tracks = new[]
        {
            CreateTrack("001.flac", 1),
            CreateTrack("002.flac", 2)
        };

        var firstSource = Path.Combine(sourceFolder, tracks[0].FileName);
        var secondSource = Path.Combine(sourceFolder, tracks[1].FileName);
        File.WriteAllBytes(firstSource, [1, 2, 3]);
        File.WriteAllBytes(secondSource, [4, 5, 6]);

        var firstConverted = ConvertedTrackPathService.GetConvertedTrackPath(
            convertedFolder,
            firstSource,
            tracks[0]);
        File.WriteAllBytes(firstConverted, [7, 8, 9]);
        File.SetLastWriteTimeUtc(firstSource, DateTime.UtcNow.AddMinutes(-2));
        File.SetLastWriteTimeUtc(firstConverted, DateTime.UtcNow.AddMinutes(-1));

        var secondConverted = ConvertedTrackPathService.GetConvertedTrackPath(
            convertedFolder,
            secondSource,
            tracks[1]);
        File.WriteAllBytes(secondConverted, [10, 11, 12]);
        File.SetLastWriteTimeUtc(secondSource, DateTime.UtcNow.AddMinutes(-2));
        File.SetLastWriteTimeUtc(secondConverted, DateTime.UtcNow.AddMinutes(-1));

        var manifest = new ExportWorkManifest();
        _workManifestService.UpdateTrack(
            manifest,
            1,
            tracks[1],
            secondSource,
            secondConverted,
            preset);
        File.Delete(secondConverted);

        var service = CreateService();

        var plan = service.Build(
            manifest,
            ProjectManifestTypes.AudioCdProject,
            tracks,
            sourceFolder,
            convertedFolder,
            preset);

        Assert.True(plan.ManifestChanged);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(1, plan.ReusableCount);
        Assert.Equal(1, plan.PendingCount);
        Assert.Equal(TimeSpan.FromMinutes(1).Ticks, plan.ReusableDurationTicks);
        Assert.Equal(ConvertedTrackPreparationPlanStatus.PartiallyReusable, plan.Status);
        Assert.True(plan.IsResume);
        Assert.Equal(2, plan.ResumeState.TotalCount);
        Assert.Equal(1, plan.ResumeState.ReusableCount);
        Assert.Equal(1, plan.ResumeState.PendingCount);
        Assert.True(plan.ResumeState.IsPartialResume);
        Assert.True(plan.ResumeState.RequiresPreparation);
        Assert.False(plan.ResumeState.IsFullyReusable);
        Assert.Single(plan.ReusableItems);
        Assert.Single(plan.PendingItems);
        Assert.True(plan.Items[0].CanReuse);
        Assert.False(plan.Items[1].CanReuse);
        Assert.Equal(firstSource, plan.Items[0].SourcePath);
        Assert.Equal(firstConverted, plan.Items[0].ConvertedPath);

        Assert.Equal(2, manifest.Tracks.Count);
        Assert.Contains(manifest.Tracks, entry =>
            entry.TrackIndex == 1 &&
            entry.Status == ProjectManifestTrackStatuses.Converted);
        Assert.Contains(manifest.Tracks, entry =>
            entry.TrackIndex == 2 &&
            entry.Status == ProjectManifestTrackStatuses.Pending);
    }

    [Fact]
    public void Build_ReportsFullyReusableWhenEveryPreparedFileCanBeReused()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        var convertedFolder = Path.Combine(folder.Path, "converted");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(convertedFolder);

        var preset = new ExportPreset("AAC Stereo 96 kbps", 96, 2);
        var tracks = new[]
        {
            CreateTrack("001.flac", 1),
            CreateTrack("002.flac", 2)
        };

        foreach (var track in tracks)
        {
            var sourcePath = Path.Combine(sourceFolder, track.FileName);
            File.WriteAllBytes(sourcePath, [1, 2, 3]);

            var convertedPath = ConvertedTrackPathService.GetConvertedTrackPath(
                convertedFolder,
                sourcePath,
                track);
            File.WriteAllBytes(convertedPath, [4, 5, 6]);
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(convertedPath, DateTime.UtcNow.AddMinutes(-1));
        }

        var plan = CreateService().Build(
            new ExportWorkManifest(),
            ProjectManifestTypes.AudioCdProject,
            tracks,
            sourceFolder,
            convertedFolder,
            preset);

        Assert.Equal(2, plan.ReusableCount);
        Assert.Equal(0, plan.PendingCount);
        Assert.Equal(TimeSpan.FromMinutes(2).Ticks, plan.ReusableDurationTicks);
        Assert.Equal(ConvertedTrackPreparationPlanStatus.FullyReusable, plan.Status);
        Assert.False(plan.IsResume);
        Assert.True(plan.ResumeState.IsFullyReusable);
        Assert.False(plan.ResumeState.RequiresPreparation);
        Assert.False(plan.ResumeState.IsPartialResume);
        Assert.Equal(2, plan.ReusableItems.Count);
        Assert.Empty(plan.PendingItems);
        Assert.All(plan.Items, item => Assert.True(item.CanReuse));
    }

    [Fact]
    public void Build_ReportsRequiresPreparationWhenNoPreparedFileCanBeReused()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        var convertedFolder = Path.Combine(folder.Path, "converted");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(convertedFolder);

        var tracks = new[]
        {
            CreateTrack("001.flac", 1),
            CreateTrack("002.flac", 2)
        };

        foreach (var track in tracks)
            File.WriteAllBytes(Path.Combine(sourceFolder, track.FileName), [1, 2, 3]);

        var plan = CreateService().Build(
            new ExportWorkManifest(),
            ProjectManifestTypes.FolderProject,
            tracks,
            sourceFolder,
            convertedFolder,
            new ExportPreset("AAC Stereo 96 kbps", 96, 2));

        Assert.Equal(0, plan.ReusableCount);
        Assert.Equal(2, plan.PendingCount);
        Assert.Equal(0, plan.ReusableDurationTicks);
        Assert.Equal(ConvertedTrackPreparationPlanStatus.RequiresPreparation, plan.Status);
        Assert.False(plan.IsResume);
        Assert.False(plan.ResumeState.IsFullyReusable);
        Assert.True(plan.ResumeState.RequiresPreparation);
        Assert.False(plan.ResumeState.IsPartialResume);
        Assert.Empty(plan.ReusableItems);
        Assert.Equal(2, plan.PendingItems.Count);
        Assert.All(plan.Items, item => Assert.False(item.CanReuse));
    }

    [Fact]
    public void Build_PreservesInputOrderAndStablePaths()
    {
        using var folder = new TemporaryFolder();
        var sourceFolder = Path.Combine(folder.Path, "source");
        var convertedFolder = Path.Combine(folder.Path, "converted");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(convertedFolder);

        var tracks = new[]
        {
            CreateTrack("010.flac", 10),
            CreateTrack("002.flac", 2),
            CreateTrack("007.flac", 7)
        };

        foreach (var track in tracks)
            File.WriteAllBytes(Path.Combine(sourceFolder, track.FileName), [1]);

        var plan = CreateService().Build(
            new ExportWorkManifest(),
            ProjectManifestTypes.FolderProject,
            tracks,
            sourceFolder,
            convertedFolder,
            new ExportPreset("AAC Stereo 96 kbps", 96, 2));

        Assert.Equal(new[] { 0, 1, 2 }, plan.Items.Select(item => item.Index).ToArray());
        Assert.Equal(tracks, plan.Items.Select(item => item.Track).ToArray());
        Assert.All(plan.Items, item => Assert.StartsWith(convertedFolder, item.ConvertedPath));
    }

    private ConvertedTrackPreparationPlanService CreateService()
    {
        return new ConvertedTrackPreparationPlanService(
            new ConvertedTrackReconciliationService(_workManifestService));
    }

    private static TrackInfo CreateTrack(string fileName, int index)
    {
        return new TrackInfo
        {
            Index = index,
            FileName = fileName,
            Extension = ".flac",
            Codec = "FLAC",
            ProcessingAction = "Konvertieren",
            Duration = "00:01:00",
            DurationTicks = TimeSpan.FromMinutes(1).Ticks,
            ChapterTitle = $"{index:000} Kapitel"
        };
    }
}
