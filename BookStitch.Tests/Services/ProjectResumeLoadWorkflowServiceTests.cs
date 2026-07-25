using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectResumeLoadWorkflowServiceTests
{
    [Fact]
    public void Prepare_MissingAudioDiscManifestFailsWithoutLocalFallback()
    {
        using var folder = new TempFolder();
        var service = CreateService();
        var plan = new ProjectResumePlan
        {
            ProjectFolder = folder.Path,
            ProjectType = ProjectManifestTypes.AudioCdProject,
            SourceFolder = System.IO.Path.Combine(folder.Path, "ripped")
        };

        var result = service.Prepare(plan);

        Assert.False(result.Success);
        Assert.Contains("Audio-CD-Projektmanifest", result.ErrorMessage);
        Assert.Empty(result.Tracks);
    }

    [Fact]
    public void Prepare_IncompleteAudioDiscRestoresTracksAndAwaitingState()
    {
        using var folder = new TempFolder();
        var audioProjectService = new AudioDiscProjectService();
        var trackListStateService = new TrackListStateService();
        var rippedFolder = System.IO.Path.Combine(folder.Path, AudioDiscProjectService.RippedFolderName);
        Directory.CreateDirectory(rippedFolder);
        var firstPath = System.IO.Path.Combine(rippedFolder, "001.flac");
        File.WriteAllBytes(firstPath, [1, 2, 3]);

        var manifest = new AudioDiscProjectManifest
        {
            ProjectFolder = folder.Path,
            Status = AudioDiscProjectStatus.Canceled,
            TotalDiscs = 1,
            ExportPreset = "AAC Mono 64 kbps",
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = AudioDiscStatus.Pending,
                    TrackCount = 2,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = 1,
                            DiscNumber = 1,
                            TrackNumber = 1,
                            FileName = "001.flac",
                            RelativePath = "ripped\\001.flac",
                            Status = AudioDiscTrackStatus.Ripped
                        },
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = 2,
                            DiscNumber = 1,
                            TrackNumber = 2,
                            FileName = "002.flac",
                            RelativePath = "ripped\\002.flac",
                            Status = AudioDiscTrackStatus.Pending
                        }
                    ]
                }
            ]
        };
        audioProjectService.Save(manifest);

        var plan = new ProjectResumePlan
        {
            ProjectFolder = folder.Path,
            ProjectType = ProjectManifestTypes.AudioCdProject,
            SourceFolder = rippedFolder,
            SelectedPreset = "AAC Mono 64 kbps",
            CanContinueDiscImport = true,
            NextMissingDiscNumber = 1,
            TotalDiscs = 1,
            Tracks =
            [
                new ProjectResumeTrackItem
                {
                    TrackIndex = 1,
                    DiscNumber = 1,
                    TrackNumber = 1,
                    SourcePath = firstPath,
                    SourceFileName = "001.flac",
                    Action = "Konvertieren"
                },
                new ProjectResumeTrackItem
                {
                    TrackIndex = 2,
                    DiscNumber = 1,
                    TrackNumber = 2,
                    SourcePath = System.IO.Path.Combine(rippedFolder, "002.flac"),
                    SourceFileName = "002.flac",
                    Action = "FLAC rippen"
                }
            ]
        };

        var result = new ProjectResumeLoadWorkflowService(audioProjectService, trackListStateService).Prepare(plan);

        Assert.True(result.Success);
        Assert.True(result.IsAudioDiscProject);
        Assert.True(result.IsAudioDiscProjectAwaitingRip);
        Assert.Equal(ProjectPipelineState.AcquiringSources, result.PipelineState);
        Assert.False(result.IsCompletedProject);
        Assert.Equal(rippedFolder, result.CurrentFolderPath);
        Assert.Equal(2, result.Tracks.Count);
        Assert.Empty(result.Tracks[1].Warning);
        Assert.Contains("Fortsetzung ab Disc 1 von 1", result.StatusText);
        Assert.Null(result.LoadStatusSnapshot);
    }

    [Fact]
    public void Prepare_AppliesPersistedTrackOrderAndExclusion()
    {
        using var folder = new TempFolder();
        var audioProjectService = new AudioDiscProjectService();
        var trackListStateService = new TrackListStateService();
        var rippedFolder = System.IO.Path.Combine(folder.Path, AudioDiscProjectService.RippedFolderName);
        Directory.CreateDirectory(rippedFolder);
        var firstPath = System.IO.Path.Combine(rippedFolder, "001.flac");
        var secondPath = System.IO.Path.Combine(rippedFolder, "002.flac");
        File.WriteAllBytes(firstPath, [1]);
        File.WriteAllBytes(secondPath, [2]);

        audioProjectService.Save(new AudioDiscProjectManifest
        {
            ProjectFolder = folder.Path,
            Status = AudioDiscProjectStatus.RippingCompleted,
            TotalDiscs = 1,
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = AudioDiscStatus.Completed,
                    TrackCount = 2,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack { GlobalIndex = 1, DiscNumber = 1, TrackNumber = 1, RelativePath = "ripped\\001.flac", FileName = "001.flac", Status = AudioDiscTrackStatus.Ripped },
                        new AudioDiscProjectManifestTrack { GlobalIndex = 2, DiscNumber = 1, TrackNumber = 2, RelativePath = "ripped\\002.flac", FileName = "002.flac", Status = AudioDiscTrackStatus.Ripped }
                    ]
                }
            ]
        });

        trackListStateService.Save(folder.Path,
        [
            new TrackInfo { FilePath = secondPath, FileName = "002.flac", IsExcluded = true },
            new TrackInfo { FilePath = firstPath, FileName = "001.flac" }
        ]);

        var plan = new ProjectResumePlan
        {
            ProjectFolder = folder.Path,
            ProjectType = ProjectManifestTypes.AudioCdProject,
            SourceFolder = rippedFolder,
            Tracks =
            [
                new ProjectResumeTrackItem { TrackIndex = 1, SourcePath = firstPath, SourceFileName = "001.flac" },
                new ProjectResumeTrackItem { TrackIndex = 2, SourcePath = secondPath, SourceFileName = "002.flac" }
            ]
        };

        var result = new ProjectResumeLoadWorkflowService(audioProjectService, trackListStateService).Prepare(plan);

        Assert.True(result.Success);
        Assert.Equal(ProjectPipelineState.ReviewBeforeMerge, result.PipelineState);
        Assert.True(result.IsWaitingForManualMergeReview);
        Assert.False(result.IsCompletedProject);
        Assert.NotNull(result.LoadStatusSnapshot);
        Assert.True(result.LoadStatusSnapshot.IsLoadedProject);
        Assert.Equal(WorkflowProjectKind.AudioDisc, result.LoadStatusSnapshot.ProjectKind);
        Assert.Equal(ProjectPipelineState.ReviewBeforeMerge, result.LoadStatusSnapshot.ProjectState);
        Assert.Equal("002.flac", result.Tracks[0].FileName);
        Assert.True(result.Tracks[0].IsExcluded);
        Assert.Equal("001.flac", result.Tracks[1].FileName);
    }

    [Fact]
    public void Prepare_SourceCompleteProjectWithSuccessfulExport_LoadsCompletedState()
    {
        using var folder = new TempFolder();
        var sourcePath = System.IO.Path.Combine(folder.Path, "001.mp3");
        File.WriteAllBytes(sourcePath, [1, 2, 3]);

        var plan = new ProjectResumePlan
        {
            ProjectFolder = folder.Path,
            ProjectType = ProjectManifestTypes.FolderProject,
            SourceFolder = folder.Path,
            HasSuccessfulExport = true,
            Tracks =
            [
                new ProjectResumeTrackItem
                {
                    TrackIndex = 1,
                    SourcePath = sourcePath,
                    SourceFileName = "001.mp3"
                }
            ]
        };

        var result = CreateService().Prepare(plan);

        Assert.True(result.Success);
        Assert.Equal(ProjectPipelineState.Completed, result.PipelineState);
        Assert.True(result.IsCompletedProject);
        Assert.True(result.IsWaitingForManualMergeReview);
        Assert.NotNull(result.LoadStatusSnapshot);
        Assert.Equal(WorkflowProjectKind.Folder, result.LoadStatusSnapshot.ProjectKind);
        Assert.Equal(ProjectPipelineState.Completed, result.LoadStatusSnapshot.ProjectState);
        Assert.True(result.LoadStatusSnapshot.IsLoadedProject);
    }

    [Fact]
    public void Prepare_AudioDiscWithHistoricalSuccessfulExport_LoadsCompletedState()
    {
        using var folder = new TempFolder();
        var audioProjectService = new AudioDiscProjectService();
        var rippedFolder = System.IO.Path.Combine(folder.Path, AudioDiscProjectService.RippedFolderName);
        Directory.CreateDirectory(rippedFolder);
        var sourcePath = System.IO.Path.Combine(rippedFolder, "001.flac");
        File.WriteAllBytes(sourcePath, [1, 2, 3]);
        audioProjectService.Save(new AudioDiscProjectManifest
        {
            ProjectFolder = folder.Path,
            TotalDiscs = 1,
            HasSuccessfulExport = true,
            LastSuccessfulOutputPath = System.IO.Path.Combine(folder.Path, "book.m4b"),
            ExportStatus = AudioDiscExportStatus.PausedBeforeMerge,
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = AudioDiscStatus.Completed,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = 1,
                            DiscNumber = 1,
                            TrackNumber = 1,
                            RelativePath = "ripped\\001.flac",
                            FileName = "001.flac",
                            Status = AudioDiscTrackStatus.Ripped
                        }
                    ]
                }
            ]
        });

        var plan = new ProjectResumePlan
        {
            ProjectFolder = folder.Path,
            ProjectType = ProjectManifestTypes.AudioCdProject,
            SourceFolder = rippedFolder,
            HasSuccessfulExport = true,
            Tracks =
            [
                new ProjectResumeTrackItem { TrackIndex = 1, SourcePath = sourcePath, SourceFileName = "001.flac" }
            ]
        };

        var result = CreateService().Prepare(plan);

        Assert.Equal(ProjectPipelineState.Completed, result.PipelineState);
        Assert.True(result.IsCompletedProject);
        Assert.NotNull(result.LoadStatusSnapshot);
        Assert.Equal(WorkflowProjectKind.AudioDisc, result.LoadStatusSnapshot.ProjectKind);
        Assert.Equal(1, result.LoadStatusSnapshot.TotalSourceItems);
    }

    [Fact]
    public void Prepare_CompletedMp3DiscProject_CreatesLoadedStatusSnapshot()
    {
        using var folder = new TempFolder();
        var disc1Track = System.IO.Path.Combine(folder.Path, "CD 01", "001.mp3");
        var disc2Track = System.IO.Path.Combine(folder.Path, "CD 02", "001.mp3");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(disc1Track)!);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(disc2Track)!);
        File.WriteAllBytes(disc1Track, [1]);
        File.WriteAllBytes(disc2Track, [2]);

        var plan = new ProjectResumePlan
        {
            ProjectFolder = folder.Path,
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            SourceFolder = folder.Path,
            HasSuccessfulExport = true,
            TotalDiscs = 2,
            SelectedPreset = "AAC Stereo 128 kbps",
            Tracks =
            [
                new ProjectResumeTrackItem { TrackIndex = 1, DiscNumber = 1, SourcePath = disc1Track, SourceFileName = "001.mp3" },
                new ProjectResumeTrackItem { TrackIndex = 2, DiscNumber = 2, SourcePath = disc2Track, SourceFileName = "001.mp3" }
            ]
        };

        var result = CreateService().Prepare(plan);

        Assert.True(result.Success);
        Assert.NotNull(result.LoadStatusSnapshot);
        Assert.Equal(WorkflowProjectKind.Mp3Disc, result.LoadStatusSnapshot.ProjectKind);
        Assert.Equal(ProjectPipelineState.Completed, result.LoadStatusSnapshot.ProjectState);
        Assert.True(result.LoadStatusSnapshot.IsLoadedProject);
        Assert.Equal(2, result.LoadStatusSnapshot.TotalSourceItems);
        Assert.Equal(2, result.LoadStatusSnapshot.TotalChapters);
    }

    private static ProjectResumeLoadWorkflowService CreateService() =>
        new(new AudioDiscProjectService(), new TrackListStateService());

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
