using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public class ProjectIndexServiceTests
{
    private readonly ProjectIndexService _service = new();

    [Fact]
    public void ScanProjects_WithMissingWorkFolder_ReturnsEmptyList()
    {
        var missingFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var projects = _service.ScanProjects(missingFolder);

        Assert.Empty(projects);
    }

    [Fact]
    public void ScanProjects_ReadsIncompleteMp3DiscProjectAsResumable()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "DiscProjects", "Book");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = "Mp3Disc",
            ProjectFolder = projectFolder,
            SourceFolder = @"E:\",
            TotalDiscs = 4,
            OutputFolder = @"D:\Export",
            CreatedUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc),
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new Mp3DiscProjectManifestDisc { DiscNumber = 2, Status = Mp3DiscImportStatus.Completed }
            ]
        });

        var projects = _service.ScanProjects(workFolder.Path, new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));

        var project = Assert.Single(projects);
        Assert.True(project.CanResume);
        Assert.Equal(ProjectManifestTypes.Mp3DiscProject, project.ProjectType);
        Assert.Equal(ProjectPipelineStateNames.AcquiringSources, project.Status);
        Assert.Equal(4, project.TotalDiscs);
        Assert.Equal(2, project.ImportedDiscCount);
        Assert.Equal(projectFolder, project.ProjectFolder);
    }


    [Fact]
    public void ScanProjects_UsesMp3DiscMetadataWhenExportManifestIsMissing()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "DiscProjects", "Book");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = "Mp3Disc",
            ProjectFolder = projectFolder,
            SourceFolder = @"E:\",
            TotalDiscs = 2,
            OutputFolder = @"D:\Export",
            OutputFileName = "Kathrin Quast - Kummer aller Art.m4a",
            Title = "Kummer aller Art",
            Author = "Kathrin Quast",
            Narrator = "Ute zart",
            Genre = "iBook Hörbuch",
            CreatedUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc),
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed }
            ]
        });

        var projects = _service.ScanProjects(workFolder.Path, new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));

        var project = Assert.Single(projects);
        Assert.Equal("Kathrin Quast - Kummer aller Art", project.DisplayName);
        Assert.Equal("Kathrin Quast", project.Author);
        Assert.Equal("Kummer aller Art", project.Title);
        Assert.Equal("Ute zart", project.Narrator);
        Assert.Equal("iBook Hörbuch", project.Genre);
        Assert.Equal("Kathrin Quast - Kummer aller Art.m4a", project.OutputFileName);
    }

    [Fact]
    public void ScanProjects_MergesMp3DiscAndExportManifestFromSameFolder()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "DiscProjects", "Book");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = "Mp3Disc",
            ProjectFolder = projectFolder,
            SourceFolder = @"E:\",
            TotalDiscs = 2,
            CreatedUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc),
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new Mp3DiscProjectManifestDisc { DiscNumber = 2, Status = Mp3DiscImportStatus.Completed }
            ]
        });

        WriteJson(System.IO.Path.Combine(projectFolder, "export-project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectWorkFolder = projectFolder,
            SourceFolder = @"E:\",
            CreatedUtc = new DateTime(2026, 7, 1, 10, 30, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            Metadata = new ExportManifestBookMetadata
            {
                Author = "Terry Pratchett",
                Title = "Wachen! Wachen!"
            },
            Export = new ExportManifestExportSettings
            {
                OutputFolder = @"D:\Export\Terry Pratchett\Wachen! Wachen!",
                OutputFileName = "Terry Pratchett - Wachen! Wachen!.m4b"
            },
            State = new ExportManifestState
            {
                Status = ProjectManifestStatuses.Completed
            },
            Resume = new ExportManifestResume
            {
                CanResume = false
            }
        });

        var projects = _service.ScanProjects(workFolder.Path, new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));

        var project = Assert.Single(projects);
        Assert.False(project.CanResume);
        Assert.Equal(ProjectManifestStatuses.Completed, project.Status);
        Assert.Equal("Terry Pratchett - Wachen! Wachen!", project.DisplayName);
        Assert.Equal("Terry Pratchett", project.Author);
        Assert.Equal("Wachen! Wachen!", project.Title);
        Assert.Equal(2, project.ImportedDiscCount);
        Assert.Equal(ProjectFolderLayout.GetExportManifestPath(projectFolder), project.PrimaryManifestPath);
    }

    [Fact]
    public void ScanProjects_ReadsCanceledFolderExportAsResumable()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "FolderProject");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder,
            CreatedUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc),
            Metadata = new ExportManifestBookMetadata
            {
                Author = "Autor",
                Title = "Titel"
            },
            State = new ExportManifestState
            {
                Status = ProjectManifestStatuses.Canceled
            },
            Resume = new ExportManifestResume
            {
                CanResume = true,
                Reason = "Export wurde abgebrochen."
            }
        });

        var projects = _service.ScanProjects(workFolder.Path, new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));

        var project = Assert.Single(projects);
        Assert.True(project.CanResume);
        Assert.Equal(ProjectManifestTypes.FolderProject, project.ProjectType);
        Assert.Equal(ProjectPipelineStateNames.Preparing, project.Status);
        Assert.Equal("Autor - Titel", project.DisplayName);
    }


    [Fact]
    public void ScanSelectableProjects_HidesIncompleteMp3DiscImportProjects()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "DiscProjects", "Book");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = "Mp3Disc",
            ProjectFolder = projectFolder,
            TotalDiscs = 3,
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed }
            ]
        });

        var projects = _service.ScanSelectableProjects(workFolder.Path, new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.Empty(projects);
    }

    [Fact]
    public void ScanProjects_ReadsIncompleteAudioDiscProjectFromAudioManifest()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "AudioDiscProjects", "Book");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "audio-disc-project.json"), new AudioDiscProjectManifest
        {
            ProjectFolder = projectFolder,
            Status = AudioDiscProjectStatus.WaitingForDisc,
            ExportStatus = AudioDiscExportStatus.NotStarted,
            TotalDiscs = 3,
            Title = "Hörbuch",
            Author = "Autor",
            OutputFolder = @"D:\Export",
            CreatedUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc),
            Discs =
            [
                new AudioDiscProjectManifestDisc { DiscNumber = 1, Status = AudioDiscStatus.Completed },
                new AudioDiscProjectManifestDisc { DiscNumber = 2, Status = AudioDiscStatus.Pending }
            ]
        });

        var projects = _service.ScanProjects(workFolder.Path, new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));

        var project = Assert.Single(projects);
        Assert.Equal(ProjectManifestTypes.AudioCdProject, project.ProjectType);
        Assert.Equal(ProjectPipelineStateNames.AcquiringSources, project.Status);
        Assert.Equal("Autor - Hörbuch", project.DisplayName);
        Assert.Equal(3, project.TotalDiscs);
        Assert.Equal(1, project.ImportedDiscCount);
        Assert.True(project.CanResume);
        Assert.False(project.IsCompletedProject);
        Assert.Equal(ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder), project.PrimaryManifestPath);
    }



    [Fact]
    public void ScanSelectableProjects_HidesCanceledFolderExports()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "FolderProject");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder,
            Metadata = new ExportManifestBookMetadata
            {
                Author = "Autor",
                Title = "Titel"
            },
            State = new ExportManifestState
            {
                Status = ProjectManifestStatuses.Canceled
            },
            Resume = new ExportManifestResume
            {
                CanResume = true
            },
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = @"D:\Work\converted\001.m4a"
                },
                new ExportWorkManifestTrack
                {
                    TrackIndex = 2,
                    Status = ProjectManifestTrackStatuses.Pending,
                    ConvertedPath = ""
                }
            ]
        });

        var projects = _service.ScanSelectableProjects(workFolder.Path, new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.Empty(projects);
    }

    [Fact]
    public void ScanSelectableProjects_IncludesSourceCompleteProjectWithoutCompletedPreset()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "SourceComplete");
        var originalsFolder = System.IO.Path.Combine(projectFolder, "originals");
        Directory.CreateDirectory(originalsFolder);
        var firstSource = System.IO.Path.Combine(originalsFolder, "001.mp3");
        var secondSource = System.IO.Path.Combine(originalsFolder, "002.mp3");
        File.WriteAllBytes(firstSource, [1]);
        File.WriteAllBytes(secondSource, [2]);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder,
            SourceFolder = originalsFolder,
            State = new ExportManifestState
            {
                Status = ProjectManifestStatuses.ReviewBeforeMerge
            },
            Tracks =
            [
                new ExportWorkManifestTrack { TrackIndex = 1, SourcePath = firstSource, Status = ProjectManifestTrackStatuses.Pending },
                new ExportWorkManifestTrack { TrackIndex = 2, SourcePath = secondSource, Status = ProjectManifestTrackStatuses.Pending }
            ]
        });

        var project = Assert.Single(_service.ScanSelectableProjects(workFolder.Path));

        Assert.True(project.IsSelectableProject);
        Assert.False(project.IsCompletedProject);
        Assert.Equal(ProjectPipelineStateNames.ReviewBeforeMerge, project.Status);
    }

    [Fact]
    public void ScanSelectableProjects_IncludesCompletedExports()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "Completed");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), ExportManifest(projectFolder, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)));

        var projects = _service.ScanSelectableProjects(workFolder.Path, new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));

        var project = Assert.Single(projects);
        Assert.True(project.IsSelectableProject);
        Assert.Equal(ProjectManifestStatuses.Completed, project.Status);
    }

    [Fact]
    public void ScanSelectableProjects_IncludesFullyConvertedProjectsBeforeMerge()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "Ready");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder,
            Metadata = new ExportManifestBookMetadata
            {
                Author = "Autor",
                Title = "Titel"
            },
            State = new ExportManifestState
            {
                Status = ProjectManifestStatuses.Ready,
                LastSuccessfulStep = "ConversionCompleted"
            },
            Resume = new ExportManifestResume
            {
                CanResume = false
            },
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = @"D:\Work\converted\001.m4a"
                },
                new ExportWorkManifestTrack
                {
                    TrackIndex = 2,
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = @"D:\Work\converted\002.m4a"
                }
            ]
        });

        var projects = _service.ScanSelectableProjects(workFolder.Path, new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc));

        var project = Assert.Single(projects);
        Assert.True(project.IsSelectableProject);
        Assert.Equal(ProjectPipelineStateNames.ReviewBeforeMerge, project.Status);
    }

    [Fact]
    public void ScanProjects_IgnoresBrokenJsonFiles()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "Broken");
        Directory.CreateDirectory(projectFolder);
        File.WriteAllText(System.IO.Path.Combine(projectFolder, "project.json"), "not json");

        var projects = _service.ScanProjects(workFolder.Path);

        Assert.Empty(projects);
    }

    [Fact]
    public void FindExpiredProjects_UsesCreatedUtcAndClampedRetention()
    {
        using var workFolder = new TemporaryFolder();
        var oldProjectFolder = System.IO.Path.Combine(workFolder.Path, "Old");
        var newProjectFolder = System.IO.Path.Combine(workFolder.Path, "New");
        Directory.CreateDirectory(oldProjectFolder);
        Directory.CreateDirectory(newProjectFolder);

        WriteJson(System.IO.Path.Combine(oldProjectFolder, "project.json"), ExportManifest(oldProjectFolder, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        WriteJson(System.IO.Path.Combine(newProjectFolder, "project.json"), ExportManifest(newProjectFolder, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));

        var expired = _service.FindExpiredProjects(workFolder.Path, new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc), retentionDays: 180);

        var project = Assert.Single(expired);
        Assert.Equal(oldProjectFolder, project.ProjectFolder);
        Assert.True(project.IsExpired);
        var expectedExpirationUtc = DateTime
            .SpecifyKind(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToLocalTime().Date.AddDays(180), DateTimeKind.Local)
            .ToUniversalTime();
        Assert.Equal(expectedExpirationUtc, project.ExpiresUtc);
    }

    [Fact]
    public void NormalizeRetentionDays_UsesMaximum180Days()
    {
        Assert.Equal(180, ProjectIndexService.NormalizeRetentionDays(999));
        Assert.Equal(0, ProjectIndexService.NormalizeRetentionDays(0));
        Assert.Equal(30, ProjectIndexService.NormalizeRetentionDays(30));
    }


    [Fact]
    public void ScanProjects_AudioDiscManifestOverridesLegacyLocalExportIdentityAndMetadata()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "AudioDiscProjects", "OriginalFolderName");
        Directory.CreateDirectory(projectFolder);

        var exportManifest = ExportManifest(projectFolder, new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc));
        exportManifest.ProjectType = ProjectManifestTypes.FolderProject;
        exportManifest.Metadata.Author = "Alter Autor";
        exportManifest.Metadata.Title = "Alter Titel";
        exportManifest.UpdatedUtc = new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc);
        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), exportManifest);

        WriteJson(System.IO.Path.Combine(projectFolder, "audio-disc-project.json"), new AudioDiscProjectManifest
        {
            ProjectFolder = projectFolder,
            ProjectType = ProjectManifestTypes.AudioCdProject,
            Status = AudioDiscProjectStatus.RippingCompleted,
            ExportStatus = AudioDiscExportStatus.Completed,
            TotalDiscs = 1,
            Title = "Neuer Titel",
            Author = "Neuer Autor",
            Narrator = "Neue Sprecherin",
            Genre = "Hörbuch",
            CreatedUtc = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            Discs =
            [
                new AudioDiscProjectManifestDisc { DiscNumber = 1, Status = AudioDiscStatus.Completed }
            ]
        });

        var project = Assert.Single(_service.ScanProjects(
            workFolder.Path,
            new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(ProjectManifestTypes.AudioCdProject, project.ProjectType);
        Assert.Equal("Neuer Autor - Neuer Titel", project.DisplayName);
        Assert.Equal("Neuer Titel", project.Title);
        Assert.Equal("Neuer Autor", project.Author);
        Assert.Equal("Neue Sprecherin", project.Narrator);
        Assert.Equal("Hörbuch", project.Genre);
    }

    [Fact]
    public void DeleteProject_RemovesSelectedProjectFolder()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "Projects", "Selected");
        Directory.CreateDirectory(projectFolder);
        File.WriteAllText(System.IO.Path.Combine(projectFolder, "project.json"), "{}");
        File.WriteAllText(System.IO.Path.Combine(projectFolder, "source.mp3"), "audio");

        var result = _service.DeleteProject(workFolder.Path, projectFolder);

        Assert.True(result.Deleted);
        Assert.Null(result.ErrorMessage);
        Assert.False(Directory.Exists(projectFolder));
        Assert.True(Directory.Exists(workFolder.Path));
    }

    [Fact]
    public void DeleteProject_RejectsFolderOutsideWorkingRoot()
    {
        using var workFolder = new TemporaryFolder();
        using var outsideFolder = new TemporaryFolder();
        File.WriteAllText(System.IO.Path.Combine(outsideFolder.Path, "keep.txt"), "keep");

        var result = _service.DeleteProject(workFolder.Path, outsideFolder.Path);

        Assert.False(result.Deleted);
        Assert.Contains("nicht innerhalb", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(outsideFolder.Path));
        Assert.True(File.Exists(System.IO.Path.Combine(outsideFolder.Path, "keep.txt")));
    }

    [Fact]
    public void DeleteProject_RejectsWorkingRootItself()
    {
        using var workFolder = new TemporaryFolder();

        var result = _service.DeleteProject(workFolder.Path, workFolder.Path);

        Assert.False(result.Deleted);
        Assert.True(Directory.Exists(workFolder.Path));
    }

    [Fact]
    public void NormalizeDeleteOlderThanDays_AllowsZeroAndMaximum180Days()
    {
        Assert.Equal(0, ProjectIndexService.NormalizeDeleteOlderThanDays(-5));
        Assert.Equal(0, ProjectIndexService.NormalizeDeleteOlderThanDays(0));
        Assert.Equal(30, ProjectIndexService.NormalizeDeleteOlderThanDays(30));
        Assert.Equal(180, ProjectIndexService.NormalizeDeleteOlderThanDays(999));
    }

    [Fact]
    public void DeleteProjectsOlderThan_RemovesExpiredProjectFolders()
    {
        using var workFolder = new TemporaryFolder();
        var oldProjectFolder = System.IO.Path.Combine(workFolder.Path, "Old");
        var newProjectFolder = System.IO.Path.Combine(workFolder.Path, "New");
        Directory.CreateDirectory(oldProjectFolder);
        Directory.CreateDirectory(newProjectFolder);

        WriteJson(System.IO.Path.Combine(oldProjectFolder, "project.json"), ExportManifest(oldProjectFolder, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        WriteJson(System.IO.Path.Combine(newProjectFolder, "project.json"), ExportManifest(newProjectFolder, new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc)));

        var result = _service.DeleteProjectsOlderThan(workFolder.Path, new DateTime(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc), olderThanDays: 30);

        Assert.Equal(1, result.MatchedCount);
        Assert.Equal(1, result.DeletedCount);
        Assert.Empty(result.Failures);
        Assert.False(Directory.Exists(oldProjectFolder));
        Assert.True(Directory.Exists(newProjectFolder));
    }


    [Fact]
    public void DeleteProjectsOlderThan_WithZeroDays_RemovesAllProjectFolders()
    {
        using var workFolder = new TemporaryFolder();
        var firstProjectFolder = System.IO.Path.Combine(workFolder.Path, "First");
        var secondProjectFolder = System.IO.Path.Combine(workFolder.Path, "Second");
        Directory.CreateDirectory(firstProjectFolder);
        Directory.CreateDirectory(secondProjectFolder);

        WriteJson(System.IO.Path.Combine(firstProjectFolder, "project.json"), ExportManifest(firstProjectFolder, new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc)));
        WriteJson(System.IO.Path.Combine(secondProjectFolder, "project.json"), ExportManifest(secondProjectFolder, new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc)));

        var result = _service.DeleteProjectsOlderThan(workFolder.Path, new DateTime(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc), olderThanDays: 0);

        Assert.Equal(2, result.MatchedCount);
        Assert.Equal(2, result.DeletedCount);
        Assert.Empty(result.Failures);
        Assert.False(Directory.Exists(firstProjectFolder));
        Assert.False(Directory.Exists(secondProjectFolder));
    }


    [Fact]
    public void ScanProjects_OrdersNewestUpdatedProjectFirstRegardlessOfResumeState()
    {
        using var workFolder = new TemporaryFolder();
        var olderResumableFolder = System.IO.Path.Combine(workFolder.Path, "OlderResumable");
        var newerCompletedFolder = System.IO.Path.Combine(workFolder.Path, "NewerCompleted");
        Directory.CreateDirectory(olderResumableFolder);
        Directory.CreateDirectory(newerCompletedFolder);

        var older = ExportManifest(olderResumableFolder, new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc));
        older.State.Status = ProjectManifestStatuses.Canceled;
        older.Resume.CanResume = true;
        older.UpdatedUtc = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc);

        var newer = ExportManifest(newerCompletedFolder, new DateTime(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc));
        newer.UpdatedUtc = new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc);

        WriteJson(System.IO.Path.Combine(olderResumableFolder, "project.json"), older);
        WriteJson(System.IO.Path.Combine(newerCompletedFolder, "project.json"), newer);

        var projects = _service.ScanProjects(workFolder.Path, new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(newerCompletedFolder, projects[0].ProjectFolder);
        Assert.Equal(olderResumableFolder, projects[1].ProjectFolder);
    }


    [Fact]
    public void ScanProjects_IncompleteAudioDiscWithPartiallyConvertedExport_IsNotCompleted()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "AudioDiscProjects", "Partial");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "audio-disc-project.json"), new AudioDiscProjectManifest
        {
            ProjectFolder = projectFolder,
            Status = AudioDiscProjectStatus.Canceled,
            ExportStatus = AudioDiscExportStatus.NotStarted,
            TotalDiscs = 1,
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = AudioDiscStatus.Pending,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack { GlobalIndex = 1, DiscNumber = 1, TrackNumber = 1, Status = AudioDiscTrackStatus.Ripped },
                        new AudioDiscProjectManifestTrack { GlobalIndex = 2, DiscNumber = 1, TrackNumber = 2, Status = AudioDiscTrackStatus.Pending }
                    ]
                }
            ]
        });

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = projectFolder,
            State = new ExportManifestState { Status = ProjectManifestStatuses.Canceled },
            Resume = new ExportManifestResume { CanResume = true },
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = System.IO.Path.Combine(projectFolder, "converted", "001.m4a")
                }
            ]
        });

        var project = Assert.Single(_service.ScanProjects(workFolder.Path));

        Assert.Equal(ProjectManifestTypes.AudioCdProject, project.ProjectType);
        Assert.False(project.IsCompletedProject);
        Assert.True(project.CanResume);
    }

    [Fact]
    public void ScanProjects_ExportManifestWithStaleAudioDiscTypeButNoAudioManifest_IsFolderProject()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "LocalProjects", "Folder");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = projectFolder,
            SourceFolder = System.IO.Path.Combine(workFolder.Path, "Source"),
            State = new ExportManifestState { Status = ProjectManifestStatuses.Canceled },
            Resume = new ExportManifestResume { CanResume = true }
        });

        var project = Assert.Single(_service.ScanProjects(workFolder.Path));

        Assert.Equal(ProjectManifestTypes.FolderProject, project.ProjectType);
        Assert.False(project.IsCompletedProject);
        Assert.True(project.CanResume);
    }


    [Fact]
    public void ScanDamagedProjects_FindsMissingLocalProjectManifest()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = Path.Combine(workFolder.Path, "Projects", "LocalProjects", "BrokenLocal");
        Directory.CreateDirectory(Path.Combine(projectFolder, "originals"));
        File.WriteAllText(Path.Combine(projectFolder, "originals", "track.mp3"), "audio");

        var damaged = Assert.Single(_service.ScanDamagedProjects(workFolder.Path));

        Assert.Equal(projectFolder, damaged.ProjectFolder);
        Assert.Equal(ProjectManifestTypes.FolderProject, damaged.ProjectType);
        Assert.Equal(ProjectFolderLayout.WorkManifestFileName, damaged.RequiredManifestFileName);
        Assert.Contains("fehlt", damaged.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScanDamagedProjects_FindsUnreadableAudioDiscManifest()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = Path.Combine(workFolder.Path, "Projects", "AudioDiscProjects", "BrokenAudio");
        var settingsFolder = Path.Combine(projectFolder, ProjectFolderLayout.SettingsFolderName);
        Directory.CreateDirectory(settingsFolder);
        File.WriteAllText(Path.Combine(settingsFolder, ProjectFolderLayout.AudioDiscManifestFileName), "{ invalid json");

        var damaged = Assert.Single(_service.ScanDamagedProjects(workFolder.Path));

        Assert.Equal(projectFolder, damaged.ProjectFolder);
        Assert.Equal(ProjectManifestTypes.AudioCdProject, damaged.ProjectType);
        Assert.Contains("nicht lesbar", damaged.Reason, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void ScanDamagedProjects_IgnoresAudioProjectWithMissingExportManifestWhenAudioManifestIsReadable()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = Path.Combine(workFolder.Path, "Projects", "AudioDiscProjects", "MissingExportManifest");
        WriteJson(ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder), new AudioDiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectFolder = projectFolder,
            TotalDiscs = 1
        });

        Assert.Empty(_service.ScanDamagedProjects(workFolder.Path));
    }

    [Fact]
    public void ScanDamagedProjects_IgnoresReadableExportManifestStoredBelowDifferentTypeFolder()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = Path.Combine(workFolder.Path, "Projects", "MP3DiscProjects", "AudioStoredAsMp3");
        Directory.CreateDirectory(Path.Combine(projectFolder, "originals", "CD 01"));
        File.WriteAllText(Path.Combine(projectFolder, "originals", "CD 01", "001.flac"), "audio");

        WriteJson(ProjectFolderLayout.GetWorkManifestPath(projectFolder), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = projectFolder,
            SourceFolder = Path.Combine(projectFolder, "originals"),
            State = new ExportManifestState { Status = ProjectManifestStatuses.AcquiringSources },
            Resume = new ExportManifestResume { CanResume = true },
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = Path.Combine(projectFolder, "originals", "CD 01", "001.flac"),
                    SourceFileName = "001.flac",
                    Status = "Converted"
                }
            ]
        });

        Assert.Empty(_service.ScanDamagedProjects(workFolder.Path));
    }

    [Fact]
    public void ScanDamagedProjects_IgnoresValidProjectsAndEmptyFolders()
    {
        using var workFolder = new TemporaryFolder();
        var emptyFolder = Path.Combine(workFolder.Path, "Projects", "MP3DiscProjects", "Empty");
        Directory.CreateDirectory(emptyFolder);

        var projectFolder = Path.Combine(workFolder.Path, "Projects", "LocalProjects", "Valid");
        WriteJson(ProjectFolderLayout.GetWorkManifestPath(projectFolder), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder,
            State = new ExportManifestState { Status = ProjectManifestStatuses.Completed }
        });

        Assert.Empty(_service.ScanDamagedProjects(workFolder.Path));
    }

    private static ExportWorkManifest ExportManifest(string projectFolder, DateTime createdUtc)
    {
        return new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder,
            CreatedUtc = createdUtc,
            UpdatedUtc = createdUtc,
            Metadata = new ExportManifestBookMetadata
            {
                Author = new DirectoryInfo(projectFolder).Name,
                Title = "Projekt"
            },
            State = new ExportManifestState
            {
                Status = ProjectManifestStatuses.Completed
            }
        };
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
