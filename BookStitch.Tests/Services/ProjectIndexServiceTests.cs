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
    public void Preview_summary_keeps_full_disc_inventory_and_saved_presets_without_reading_audio()
    {
        using var root = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(root.Path);
        WriteJson(ProjectFolderLayout.GetWorkManifestPath(root.Path), new Mp3DiscProjectManifest
        {
            TotalDiscs = 2, ExportPreset = "AAC Stereo 192 kbps",
            ImportedDiscs = [new() { DiscNumber = 1, FileCount = 2 }, new() { DiscNumber = 2, FileCount = 2 }]
        });
        WriteJson(ProjectFolderLayout.GetExportManifestPath(root.Path), new ExportWorkManifest
        {
            SelectedPreset = "AAC Mono 64 kbps",
            State = new() { Status = ProjectPipelineStateNames.Completed },
            Metadata = new() { Title = "Saved metadata", ProcessedCoverPath = "saved-cover.png" },
            // No audio files exist. The excluded fourth source is absent from the export inventory.
            Tracks = Enumerable.Range(1, 3).SelectMany(index => new[]
            {
                new ExportWorkManifestTrack { TrackIndex = index, Preset = "AAC Mono 64 kbps",
                    Status = ProjectManifestTrackStatuses.Converted, ConvertedPath = $"missing-mono-{index}.m4a" },
                new ExportWorkManifestTrack { TrackIndex = index, Preset = "AAC Stereo 192 kbps",
                    Status = ProjectManifestTrackStatuses.Converted, ConvertedPath = $"missing-stereo-{index}.m4a" }
            }).ToList()
        });
        var project = Assert.Single(_service.ScanProjects(root.Path));
        Assert.Equal(4, project.TrackCount);
        Assert.Equal("AAC Mono 64 kbps", project.SelectedPreset);
        Assert.Equal(3, project.SavedConvertedTrackCounts["AAC Mono 64 kbps"]);
        Assert.Equal(3, project.SavedConvertedTrackCounts["AAC Stereo 192 kbps"]);
        Assert.Equal("saved-cover.png", project.ProcessedCoverPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Corrupt_disc_manifest_is_reported_as_damaged_instead_of_indexed_as_folder(bool audio)
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        File.WriteAllText(ProjectFolderLayout.GetExportManifestPath(folder.Path), JsonSerializer.Serialize(new ExportWorkManifest
        { ProjectType = ProjectManifestTypes.FolderProject, ProjectWorkFolder = folder.Path }));
        var path = audio ? ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path) : ProjectFolderLayout.GetWorkManifestPath(folder.Path);
        File.WriteAllText(path, "{ damaged");
        var failures = new List<DamagedProjectInfo>();
        Assert.Empty(_service.ScanProjects(folder.Path, readFailures: failures));
        Assert.Single(failures);
        Assert.Equal("{ damaged", File.ReadAllText(path));
    }

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
        Assert.Equal(ProjectFolderLayout.ResolveExportManifestPath(projectFolder), project.PrimaryManifestPath);
    }

    [Fact]
    public void ScanProjects_Mp3DiscManifestOverridesConflictingExportMetadata()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "DiscProjects", "Book");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = "Mp3Disc",
            ProjectFolder = projectFolder,
            TotalDiscs = 1,
            Title = "Disc Titel",
            Author = "Disc Autor",
            Album = "Disc Album",
            Narrator = "Disc Sprecher",
            Genre = "Disc Genre",
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed }
            ]
        });

        WriteJson(System.IO.Path.Combine(projectFolder, "export-project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectWorkFolder = projectFolder,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Export Titel",
                Author = "Export Autor",
                Album = "Export Album",
                Narrator = "Export Sprecher",
                Genre = "Export Genre"
            },
            State = new ExportManifestState { Status = ProjectManifestStatuses.Completed }
        });

        var project = Assert.Single(_service.ScanProjects(workFolder.Path));

        Assert.Equal("Disc Autor - Disc Titel", project.DisplayName);
        Assert.Equal("Disc Titel", project.Title);
        Assert.Equal("Disc Autor", project.Author);
        Assert.Equal("Disc Album", project.Album);
        Assert.Equal("Disc Sprecher", project.Narrator);
        Assert.Equal("Disc Genre", project.Genre);
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
            Album = "Album",
            Narrator = "Sprecher",
            Genre = "Genre",
            CoverSourcePath = @"D:\Cover\source.png",
            ProcessedCoverPath = @"D:\Cover\processed.jpg",
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
        Assert.Equal("Hörbuch", project.Title);
        Assert.Equal("Autor", project.Author);
        Assert.Equal("Album", project.Album);
        Assert.Equal("Sprecher", project.Narrator);
        Assert.Equal("Genre", project.Genre);
        Assert.Equal(3, project.TotalDiscs);
        Assert.Equal(1, project.ImportedDiscCount);
        Assert.True(project.CanResume);
        Assert.False(project.IsCompletedProject);
        Assert.Equal(ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder), project.PrimaryManifestPath);
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
    public void FindExpiredProjects_UsesUpdatedUtcForCompletedProjects()
    {
        using var workFolder = new TemporaryFolder();
        var oldProjectFolder = System.IO.Path.Combine(workFolder.Path, "Old");
        var newProjectFolder = System.IO.Path.Combine(workFolder.Path, "New");
        Directory.CreateDirectory(oldProjectFolder);
        Directory.CreateDirectory(newProjectFolder);

        var oldProject = ExportManifest(oldProjectFolder, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        oldProject.UpdatedUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var recentlyUpdatedProject = ExportManifest(newProjectFolder, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        recentlyUpdatedProject.UpdatedUtc = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc);
        WriteJson(System.IO.Path.Combine(oldProjectFolder, "project.json"), oldProject);
        WriteJson(System.IO.Path.Combine(newProjectFolder, "project.json"), recentlyUpdatedProject);

        var expired = _service.FindExpiredProjects(workFolder.Path, new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc), retentionDays: 180);

        var project = Assert.Single(expired);
        Assert.Equal(oldProjectFolder, project.ProjectFolder);
        Assert.True(project.IsExpired);
        var expectedExpirationUtc = DateTime
            .SpecifyKind(oldProject.UpdatedUtc.ToLocalTime().Date.AddDays(180), DateTimeKind.Local)
            .ToUniversalTime();
        Assert.Equal(expectedExpirationUtc, project.ExpiresUtc);
    }

    [Fact]
    public void FindExpiredProjects_DoesNotExpireIncompleteProjectsByAge()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "DiscProjects", "Incomplete");
        Directory.CreateDirectory(projectFolder);

        WriteJson(System.IO.Path.Combine(projectFolder, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = "Mp3Disc",
            ProjectFolder = projectFolder,
            TotalDiscs = 2,
            CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = Mp3DiscImportStatus.Completed
                }
            ]
        });

        var projects = _service.ScanProjects(
            workFolder.Path,
            new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc),
            retentionDays: 30);

        var project = Assert.Single(projects);
        Assert.False(project.IsExpired);
        Assert.Equal(default, project.ExpiresUtc);
        Assert.Empty(_service.FindExpiredProjects(
            workFolder.Path,
            new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc),
            retentionDays: 30));
    }

    [Fact]
    public void NormalizeRetentionDays_UsesMaximum360Days()
    {
        Assert.Equal(360, ProjectIndexService.NormalizeRetentionDays(999));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteProject_ProtectsActiveProjectAndItsParent(bool deleteParent)
    {
        using var root = new TemporaryFolder();
        var parent = Path.Combine(root.Path, "Projects");
        var active = Path.Combine(parent, "Active");
        Directory.CreateDirectory(active);
        var source = Path.Combine(active, "source.mp3");
        File.WriteAllText(source, "original");
        _service.SetActiveProjectFolderProvider(() => Path.Combine(active, ".") + Path.DirectorySeparatorChar);

        var result = _service.DeleteProject(root.Path, deleteParent ? parent : active);

        Assert.False(result.Deleted);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Equal("original", File.ReadAllText(source));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    public void DeleteProjectsOlderThan_PreservesActiveProjectAndDeletesOtherEligibleProjects(int days, bool useCatalogItems)
    {
        using var root = new TemporaryFolder();
        var active = Path.Combine(root.Path, "Active");
        var other = Path.Combine(root.Path, "Active-other");
        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var folder in new[] { active, other })
        {
            Directory.CreateDirectory(folder);
            WriteJson(Path.Combine(folder, "project.json"), ExportManifest(folder, timestamp));
        }
        string? current = active;
        _service.SetActiveProjectFolderProvider(() => current);

        var now = timestamp.AddDays(90);
        var catalogItems = useCatalogItems ? _service.ScanProjects(root.Path, now, days) : null;
        var result = _service.DeleteProjectsOlderThan(root.Path, now, days, knownProjects: catalogItems);

        Assert.Equal(1, result.DeletedCount);
        Assert.Empty(result.Failures);
        Assert.True(Directory.Exists(active));
        Assert.False(Directory.Exists(other));

        // Protection follows the current owner state; closed projects remain deletable.
        current = null;
        Assert.True(_service.DeleteProject(root.Path, active).Deleted);
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
    public void NormalizeDeleteOlderThanDays_AllowsZeroAndMaximum360Days()
    {
        Assert.Equal(0, ProjectIndexService.NormalizeDeleteOlderThanDays(-5));
        Assert.Equal(0, ProjectIndexService.NormalizeDeleteOlderThanDays(0));
        Assert.Equal(30, ProjectIndexService.NormalizeDeleteOlderThanDays(30));
        Assert.Equal(360, ProjectIndexService.NormalizeDeleteOlderThanDays(999));
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
    public void ScanProjects_WithCorruptProjectMetadataStore_SkipsProjectAndDoesNotRemigrateFromLegacy()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = Path.Combine(workFolder.Path, "Projects", "LocalProjects", "CorruptMetadata");
        WriteJson(ProjectFolderLayout.GetWorkManifestPath(projectFolder), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder,
            Metadata = new ExportManifestBookMetadata
            {
                Author = "Legacy Autor",
                Title = "Legacy Titel"
            },
            State = new ExportManifestState { Status = ProjectManifestStatuses.Completed }
        });
        const string corruptJson = "{ invalid";
        File.WriteAllText(ProjectFolderLayout.GetProjectMetadataPath(projectFolder), corruptJson);

        var projects = _service.ScanProjects(workFolder.Path);

        Assert.Empty(projects);
        Assert.Equal(corruptJson, File.ReadAllText(ProjectFolderLayout.GetProjectMetadataPath(projectFolder)));
    }

    [Fact]
    public void ScanProjects_AndResumePlan_ReadCurrentMetadataFromProjectMetadataStore()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(workFolder.Path, "LocalProjects", "Folder");
        Directory.CreateDirectory(projectFolder);
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Store Titel", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["Store Autor"], MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Store Album", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators, ["Store Sprecher"], MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres, ["Store Genre"], MetadataOrigin.User);
        new ProjectMetadataStore().Save(projectFolder, metadata);

        WriteJson(ProjectFolderLayout.GetWorkManifestPath(projectFolder), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Legacy Titel",
                Author = "Legacy Autor",
                Album = "Legacy Album",
                Narrator = "Legacy Sprecher",
                Genre = "Legacy Genre"
            },
            State = new ExportManifestState { Status = ProjectManifestStatuses.Canceled },
            Resume = new ExportManifestResume { CanResume = true }
        });

        var indexItem = Assert.Single(_service.ScanProjects(workFolder.Path));
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(projectFolder);

        Assert.Equal("Store Autor - Store Titel", indexItem.DisplayName);
        Assert.Equal("Store Titel", indexItem.Title);
        Assert.Equal("Store Autor", indexItem.Author);
        Assert.Equal("Store Album", indexItem.Album);
        Assert.Equal("Store Sprecher", indexItem.Narrator);
        Assert.Equal("Store Genre", indexItem.Genre);
        Assert.NotNull(plan);
        Assert.Equal(indexItem.DisplayName, plan.DisplayName);
        Assert.Equal(indexItem.Title, plan.BookTitle);
        Assert.Equal(indexItem.Author, plan.Author);
        Assert.Equal(indexItem.Album, plan.Album);
        Assert.Equal(indexItem.Narrator, plan.Narrator);
        Assert.Equal(indexItem.Genre, plan.Genre);
    }


    [Fact]
    public void ScanDamagedProjects_FindsUnreadableProjectMetadataStore()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = Path.Combine(workFolder.Path, "Projects", "LocalProjects", "BrokenMetadata");
        WriteJson(ProjectFolderLayout.GetWorkManifestPath(projectFolder), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder,
            State = new ExportManifestState { Status = ProjectManifestStatuses.Completed }
        });
        File.WriteAllText(ProjectFolderLayout.GetProjectMetadataPath(projectFolder), "{ invalid");

        var damaged = Assert.Single(_service.ScanDamagedProjects(workFolder.Path));

        Assert.Equal(projectFolder, damaged.ProjectFolder);
        Assert.Equal(ProjectManifestTypes.FolderProject, damaged.ProjectType);
        Assert.Equal(ProjectFolderLayout.ProjectMetadataFileName, damaged.RequiredManifestFileName);
        Assert.Contains("autoritative Metadatendatei", damaged.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nicht lesbar", damaged.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScanDamagedProjects_DoesNotRequireMissingProjectMetadataStore()
    {
        using var workFolder = new TemporaryFolder();
        var projectFolder = Path.Combine(workFolder.Path, "Projects", "LocalProjects", "MissingMetadataStore");
        WriteJson(ProjectFolderLayout.GetWorkManifestPath(projectFolder), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder,
            State = new ExportManifestState { Status = ProjectManifestStatuses.Completed }
        });

        Assert.Empty(_service.ScanDamagedProjects(workFolder.Path));
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

    [Fact]
    public void Cancellation_before_cleanup_preserves_project_files_even_with_catalog_items()
    {
        using var root = new TemporaryFolder();
        var project = Path.Combine(root.Path, "Saved");
        WriteJson(Path.Combine(project, "project.json"), ExportManifest(project, DateTime.UtcNow.AddDays(-100)));
        var catalogItems = _service.ScanProjects(root.Path, DateTime.UtcNow, 10);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => _service.DeleteProjectsOlderThan(
            root.Path, DateTime.UtcNow, 10, cancellation.Token, catalogItems));
        Assert.True(Directory.Exists(project));
    }

    [Fact]
    public void Catalog_based_cleanup_preserves_incomplete_projects_with_old_timestamps()
    {
        using var root = new TemporaryFolder();
        var project = Path.Combine(root.Path, "Incomplete");
        var manifest = ExportManifest(project, DateTime.UtcNow.AddDays(-100));
        manifest.State.Status = ProjectPipelineStateNames.Converting;
        WriteJson(Path.Combine(project, "project.json"), manifest);
        var items = _service.ScanProjects(root.Path, DateTime.UtcNow, 10);
        Assert.False(Assert.Single(items).IsExpired);
        Assert.Equal(0, _service.DeleteProjectsOlderThan(root.Path, DateTime.UtcNow, 10, knownProjects: items).DeletedCount);
        Assert.True(Directory.Exists(project));
    }

    [Fact]
    public void Stale_catalog_candidate_cannot_delete_a_project_that_became_incomplete()
    {
        using var root = new TemporaryFolder();
        var project = Path.Combine(root.Path, "Changed");
        var path = ProjectFolderLayout.GetWorkManifestPath(project);
        var manifest = ExportManifest(project, DateTime.UtcNow.AddDays(-100));
        WriteJson(path, manifest);
        var candidates = _service.ScanProjects(root.Path, DateTime.UtcNow, 10);
        Assert.True(Assert.Single(candidates).IsExpired);
        manifest.State.Status = ProjectPipelineStateNames.Converting;
        WriteJson(path, manifest);
        Assert.Equal(0, _service.DeleteProjectsOlderThan(root.Path, DateTime.UtcNow, 10, knownProjects: candidates).DeletedCount);
        Assert.True(Directory.Exists(project));
    }

    [Fact]
    public async Task Cancellation_after_one_deleted_project_keeps_catalog_dirty_and_preserves_remaining_projects()
    {
        using var root = new TemporaryFolder();
        var first = Path.Combine(root.Path, "First");
        var second = Path.Combine(root.Path, "Second");
        foreach (var folder in new[] { first, second })
            WriteJson(ProjectFolderLayout.GetWorkManifestPath(folder), ExportManifest(folder, DateTime.UtcNow.AddDays(-100)));
        var snapshot = await _service.Catalog.RefreshAsync(root.Path, 10);
        await _service.Catalog.WaitForDamageAsync(snapshot.Generation, CancellationToken.None);
        Assert.False(_service.Catalog.Snapshot!.IsDirty);
        var items = new[] { snapshot.ProjectIndexItems.Single(item => item.ProjectFolder == first),
            snapshot.ProjectIndexItems.Single(item => item.ProjectFolder == second) };
        using var cancellation = new CancellationTokenSource();
        _service.SetActiveProjectFolderProvider(() =>
        {
            if (!Directory.Exists(first)) cancellation.Cancel();
            return null;
        });
        Assert.ThrowsAny<OperationCanceledException>(() => _service.DeleteProjectsOlderThan(
            root.Path, DateTime.UtcNow, 10, cancellation.Token, items));
        Assert.False(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
        Assert.True(_service.Catalog.Snapshot!.IsDirty);
        var refreshed = await _service.Catalog.RefreshAsync(root.Path, 10);
        Assert.Equal(second, Assert.Single(refreshed.ProjectIndexItems).ProjectFolder);
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
