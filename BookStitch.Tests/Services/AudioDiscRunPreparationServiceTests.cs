using System.IO;

using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscRunPreparationServiceTests
{
    [Fact]
    public void Prepare_PersistsCurrentlySelectedPresetBeforeCreatingFixedRunContext()
    {
        using var folder = new TemporaryFolder();
        var audioDiscProjectService = new AudioDiscProjectService();
        var projectSnapshotService = new ProjectSnapshotService(
            new Mp3DiscProjectService(),
            audioDiscProjectService,
            new WorkManifestService());
        var service = new AudioDiscRunPreparationService(projectSnapshotService);
        var manifest = new AudioDiscProjectManifest
        {
            ProjectFolder = folder.Path,
            ExportPreset = "AAC Stereo 64 kbps",
            ParallelJobs = "2",
            TotalDiscs = 1
        };
        var snapshot = new ProjectSnapshotUiState(
            "AAC Stereo 128 kbps",
            "20",
            ".m4a",
            folder.Path,
            "{Autor} - {Titel}",
            "Titel",
            "Autor",
            "Titel",
            "Sprecher",
            "iBook Hörbuch",
            "",
            "",
            "Autor - Titel.m4a");

        var preparation = service.Prepare(manifest, snapshot, resolvedParallelJobs: 20);

        Assert.Equal("AAC Stereo 128 kbps", preparation.Preset.DisplayName);
        Assert.Equal(20, preparation.ParallelJobs);
        Assert.Equal("AAC Stereo 128 kbps", manifest.ExportPreset);
        Assert.Equal("20", manifest.ParallelJobs);
        Assert.True(File.Exists(ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path)));

        var reloaded = audioDiscProjectService.TryLoad(folder.Path);
        Assert.NotNull(reloaded);
        Assert.Equal("AAC Stereo 128 kbps", reloaded.ExportPreset);
        Assert.Equal("20", reloaded.ParallelJobs);
    }

    [Fact]
    public void Prepare_ClampsResolvedParallelJobsToAtLeastOne()
    {
        using var folder = new TemporaryFolder();
        var projectSnapshotService = new ProjectSnapshotService(
            new Mp3DiscProjectService(),
            new AudioDiscProjectService(),
            new WorkManifestService());
        var service = new AudioDiscRunPreparationService(projectSnapshotService);
        var manifest = new AudioDiscProjectManifest
        {
            ProjectFolder = folder.Path,
            ExportPreset = "AAC Stereo 128 kbps",
            TotalDiscs = 1
        };
        var snapshot = new ProjectSnapshotUiState(
            "AAC Stereo 128 kbps", "0", ".m4a", folder.Path, "{Titel}",
            "Titel", "", "", "", "iBook Hörbuch", "", "", "Titel.m4a");

        var preparation = service.Prepare(manifest, snapshot, resolvedParallelJobs: 0);

        Assert.Equal(1, preparation.ParallelJobs);
    }
}
