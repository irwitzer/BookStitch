using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public class ProjectResumePlanServiceTests
{
    private readonly ProjectResumePlanService _service = new();

    [Fact]
    public void BuildFromProjectFolder_PreservesDistinctAlbumFromManifest()
    {
        using var projectFolder = new TemporaryFolder();

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = projectFolder.Path,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Einzeltitel",
                Album = "Gesamtes Hörbuch",
                Author = "Autor"
            },
            State = new ExportManifestState
            {
                Status = ProjectManifestStatuses.Completed
            }
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal("Einzeltitel", plan.BookTitle);
        Assert.Equal("Gesamtes Hörbuch", plan.Album);
    }

    [Fact]
    public void BuildFromProjectFolder_StripsFinalAuthorTitleFolderFromFolderResumeOutput()
    {
        using var projectFolder = new TemporaryFolder();

        var baseOutputFolder = System.IO.Path.Combine(projectFolder.Path, "HB OUT");
        var finalOutputFolder = System.IO.Path.Combine(baseOutputFolder, "John Grisham", "Die Wächter");
        System.IO.Directory.CreateDirectory(finalOutputFolder);

        var sourceFolder = System.IO.Path.Combine(projectFolder.Path, "Source");
        System.IO.Directory.CreateDirectory(sourceFolder);

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = sourceFolder,
            Metadata = new ExportManifestBookMetadata
            {
                Author = "John Grisham",
                Title = "Die Wächter"
            },
            Export = new ExportManifestExportSettings
            {
                OutputFolder = finalOutputFolder,
                OutputFileName = "John Grisham - Die Wächter.m4a",
                OutputExtension = ".m4a"
            },
            State = new ExportManifestState
            {
                Status = ProjectManifestStatuses.Canceled
            },
            Resume = new ExportManifestResume
            {
                CanResume = true
            }
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal(baseOutputFolder, plan.OutputFolder);
    }

    [Fact]
    public void BuildFromProjectFolder_KeepsMp3DiscImportOutputFolderWhenExportManifestContainsFinalFolder()
    {
        using var projectFolder = new TemporaryFolder();

        var baseOutputFolder = System.IO.Path.Combine(projectFolder.Path, "HB OUT");
        var finalOutputFolder = System.IO.Path.Combine(baseOutputFolder, "John Grisham", "Die Wächter");

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectFolder = projectFolder.Path,
            OutputFolder = baseOutputFolder,
            Author = "John Grisham",
            Title = "Die Wächter",
            TotalDiscs = 1
        });

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "export-project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = projectFolder.Path,
            Metadata = new ExportManifestBookMetadata
            {
                Author = "John Grisham",
                Title = "Die Wächter"
            },
            Export = new ExportManifestExportSettings
            {
                OutputFolder = finalOutputFolder,
                OutputFileName = "John Grisham - Die Wächter.m4a",
                OutputExtension = ".m4a"
            },
            Resume = new ExportManifestResume
            {
                CanResume = true
            }
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal(baseOutputFolder, plan.OutputFolder);
    }

    [Fact]
    public void BuildFromProjectFolder_RemovesDuplicateResumeTracksForSameSourcePath()
    {
        using var projectFolder = new TemporaryFolder();

        var sourceFolder = System.IO.Path.Combine(projectFolder.Path, "Source");
        System.IO.Directory.CreateDirectory(sourceFolder);
        var sourcePath = System.IO.Path.Combine(sourceFolder, "Track 01.mp3");
        System.IO.File.WriteAllText(sourcePath, "audio");

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = sourceFolder,
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = sourcePath,
                    SourceFileName = "Track 01.mp3",
                    ChapterTitle = "001 Kapitel",
                    ConvertedPath = System.IO.Path.Combine(projectFolder.Path, "converted", "old.m4a"),
                    CompletedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ExportWorkManifestTrack
                {
                    TrackIndex = 77,
                    SourcePath = sourcePath,
                    SourceFileName = "Track 01.mp3",
                    ChapterTitle = "001 Kapitel",
                    ConvertedPath = System.IO.Path.Combine(projectFolder.Path, "converted", "new.m4a"),
                    CompletedUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
                }
            ],
            Resume = new ExportManifestResume
            {
                CanResume = true
            }
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        var track = Assert.Single(plan.Tracks);
        Assert.Equal(77, track.TrackIndex);
        Assert.Equal(sourcePath, track.SourcePath);
    }


    [Fact]
    public void BuildFromProjectFolder_CompletedMp3DiscProject_RemainsMp3DiscWithoutContinueFlag()
    {
        using var projectFolder = new TemporaryFolder();

        var discFolder = System.IO.Path.Combine(projectFolder.Path, "CD 01");
        System.IO.Directory.CreateDirectory(discFolder);
        var trackPath = System.IO.Path.Combine(discFolder, "Track 01.mp3");
        System.IO.File.WriteAllText(trackPath, "audio");

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectFolder = projectFolder.Path,
            TotalDiscs = 1,
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = Mp3DiscImportStatus.Completed,
                    LocalFolder = discFolder
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal(ProjectManifestTypes.Mp3DiscProject, plan.ProjectType);
        Assert.False(plan.CanContinueDiscImport);
        Assert.Null(plan.NextMissingDiscNumber);
        Assert.Equal(projectFolder.Path, plan.ProjectFolder);
        Assert.Single(plan.Tracks);
    }

    [Fact]
    public void BuildFromProjectFolder_Mp3DiscProjectWithExportManifest_UsesSavedTrackListInsteadOfAllImportedFiles()
    {
        using var projectFolder = new TemporaryFolder();

        var discFolder = System.IO.Path.Combine(projectFolder.Path, "CD 01");
        System.IO.Directory.CreateDirectory(discFolder);
        var track1 = System.IO.Path.Combine(discFolder, "Track 01.mp3");
        var track2 = System.IO.Path.Combine(discFolder, "Track 02.mp3");
        var track3 = System.IO.Path.Combine(discFolder, "Track 03.mp3");
        System.IO.File.WriteAllText(track1, "audio 1");
        System.IO.File.WriteAllText(track2, "audio 2");
        System.IO.File.WriteAllText(track3, "audio 3");

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectFolder = projectFolder.Path,
            TotalDiscs = 1,
            ImportedDiscs =
            [
                new Mp3DiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = Mp3DiscImportStatus.Completed,
                    LocalFolder = discFolder
                }
            ]
        });

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "export-project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = projectFolder.Path,
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = track1,
                    SourceFileName = "Track 01.mp3",
                    ChapterTitle = "Track 01"
                },
                new ExportWorkManifestTrack
                {
                    TrackIndex = 3,
                    SourcePath = track3,
                    SourceFileName = "Track 03.mp3",
                    ChapterTitle = "Track 03"
                }
            ],
            Resume = new ExportManifestResume
            {
                CanResume = true
            }
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal(ProjectManifestTypes.Mp3DiscProject, plan.ProjectType);
        var migratedDiscFolder = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder.Path, 1);
        Assert.Collection(
            plan.Tracks,
            track => Assert.Equal(System.IO.Path.Combine(migratedDiscFolder, "Track 01.mp3"), track.SourcePath),
            track => Assert.Equal(System.IO.Path.Combine(migratedDiscFolder, "Track 03.mp3"), track.SourcePath));
    }


    [Fact]
    public void BuildFromProjectFolder_IncompleteAudioDiscProject_CreatesLoadableRipResumePlan()
    {
        using var projectFolder = new TemporaryFolder();

        var rippedFolder = System.IO.Path.Combine(projectFolder.Path, "ripped");
        System.IO.Directory.CreateDirectory(rippedFolder);
        var rippedTrackPath = System.IO.Path.Combine(rippedFolder, "001_Test.flac");
        System.IO.File.WriteAllText(rippedTrackPath, "flac");

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "audio-disc-project.json"), new AudioDiscProjectManifest
        {
            ProjectFolder = projectFolder.Path,
            Status = AudioDiscProjectStatus.Canceled,
            TotalDiscs = 2,
            ExportPreset = "AAC Mono 64 kbps",
            ParallelJobs = "4",
            OutputExtension = ".m4b",
            OutputFolder = System.IO.Path.Combine(projectFolder.Path, "Output"),
            FileNameTemplate = "{Autor} - {Titel}",
            Title = "Testtitel",
            Author = "Testautor",
            Narrator = "Testsprecher",
            Genre = "Hörbuch",
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = AudioDiscStatus.Pending,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = 1,
                            DiscNumber = 1,
                            TrackNumber = 1,
                            FileName = "001_Test.flac",
                            RelativePath = System.IO.Path.Combine("ripped", "001_Test.flac"),
                            ChapterTitle = "001 Testtitel",
                            Duration = TimeSpan.FromMinutes(3),
                            Status = AudioDiscTrackStatus.Ripped
                        },
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = 2,
                            DiscNumber = 1,
                            TrackNumber = 2,
                            FileName = "002_Test.flac",
                            RelativePath = System.IO.Path.Combine("ripped", "002_Test.flac"),
                            ChapterTitle = "002 Testtitel",
                            Duration = TimeSpan.FromMinutes(4),
                            Status = AudioDiscTrackStatus.Pending
                        }
                    ]
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal(ProjectManifestTypes.AudioCdProject, plan.ProjectType);
        Assert.True(plan.CanResume);
        Assert.True(plan.CanContinueDiscImport);
        Assert.Equal(1, plan.NextMissingDiscNumber);
        Assert.Equal(2, plan.TotalDiscs);
        Assert.Equal(0, plan.ImportedDiscCount);
        var migratedOriginals = ProjectFolderLayout.GetOriginalsFolder(projectFolder.Path);
        var migratedDiscFolder = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder.Path, 1);
        Assert.Equal(migratedOriginals, plan.SourceFolder);
        Assert.Equal("Testtitel", plan.BookTitle);
        Assert.Equal("Testautor", plan.Author);
        Assert.Equal("AAC Mono 64 kbps", plan.SelectedPreset);
        Assert.Collection(
            plan.Tracks,
            track =>
            {
                Assert.Equal(System.IO.Path.Combine(migratedDiscFolder, "001_Test.flac"), track.SourcePath);
                Assert.Equal("Konvertieren", track.Action);
            },
            track =>
            {
                Assert.Equal(System.IO.Path.Combine(migratedDiscFolder, "002_Test.flac"), track.SourcePath);
                Assert.Equal("FLAC rippen", track.Action);
            });
    }

    [Fact]
    public void BuildFromProjectFolder_AudioDiscWithPartialExportManifest_KeepsAllDiscTracksVisible()
    {
        using var projectFolder = new TemporaryFolder();

        var rippedFolder = System.IO.Path.Combine(projectFolder.Path, "ripped");
        System.IO.Directory.CreateDirectory(rippedFolder);
        var rippedTrackPath = System.IO.Path.Combine(rippedFolder, "001_Test.flac");
        System.IO.File.WriteAllText(rippedTrackPath, "flac");

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "audio-disc-project.json"), new AudioDiscProjectManifest
        {
            ProjectFolder = projectFolder.Path,
            Status = AudioDiscProjectStatus.Canceled,
            TotalDiscs = 1,
            ExportPreset = "AAC Stereo 128 kbps",
            Discs =
            [
                new AudioDiscProjectManifestDisc
                {
                    DiscNumber = 1,
                    Status = AudioDiscStatus.Pending,
                    Tracks =
                    [
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = 1,
                            DiscNumber = 1,
                            TrackNumber = 1,
                            FileName = "001_Test.flac",
                            RelativePath = System.IO.Path.Combine("ripped", "001_Test.flac"),
                            ChapterTitle = "001 Test",
                            Duration = TimeSpan.FromMinutes(1),
                            Status = AudioDiscTrackStatus.Ripped
                        },
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = 2,
                            DiscNumber = 1,
                            TrackNumber = 2,
                            FileName = "002_Test.flac",
                            RelativePath = System.IO.Path.Combine("ripped", "002_Test.flac"),
                            ChapterTitle = "002 Test",
                            Duration = TimeSpan.FromMinutes(2),
                            Status = AudioDiscTrackStatus.Pending
                        },
                        new AudioDiscProjectManifestTrack
                        {
                            GlobalIndex = 3,
                            DiscNumber = 1,
                            TrackNumber = 3,
                            FileName = "003_Test.flac",
                            RelativePath = System.IO.Path.Combine("ripped", "003_Test.flac"),
                            ChapterTitle = "003 Test",
                            Duration = TimeSpan.FromMinutes(3),
                            Status = AudioDiscTrackStatus.Pending
                        }
                    ]
                }
            ]
        });

        var convertedPath = System.IO.Path.Combine(projectFolder.Path, "converted", "001_Test.m4a");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(convertedPath)!);
        System.IO.File.WriteAllText(convertedPath, "aac");
        System.IO.File.SetLastWriteTimeUtc(convertedPath, System.IO.File.GetLastWriteTimeUtc(rippedTrackPath).AddMinutes(1));

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = rippedFolder,
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = rippedTrackPath,
                    SourceFileName = "001_Test.flac",
                    Preset = "AAC Stereo 128 kbps",
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = convertedPath,
                    CompletedUtc = DateTime.UtcNow
                }
            ],
            Resume = new ExportManifestResume
            {
                CanResume = true
            }
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal(3, plan.Tracks.Count);
        Assert.Equal(convertedPath, plan.Tracks[0].ConvertedPath);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, plan.Tracks[0].Status);
        Assert.Equal("FLAC rippen", plan.Tracks[1].Action);
        Assert.Equal("FLAC rippen", plan.Tracks[2].Action);
    }

    [Fact]
    public void BuildFromProjectFolder_AudioDiscResume_UsesOnlyTheSelectedPresetTrackState()
    {
        using var projectFolder = new TemporaryFolder();

        var rippedFolder = System.IO.Path.Combine(projectFolder.Path, "ripped");
        var convertedFolder = System.IO.Path.Combine(projectFolder.Path, "converted");
        System.IO.Directory.CreateDirectory(rippedFolder);
        System.IO.Directory.CreateDirectory(convertedFolder);

        var sourcePath = System.IO.Path.Combine(rippedFolder, "001_Test.flac");
        var selectedConvertedPath = System.IO.Path.Combine(convertedFolder, "001_64k.m4a");
        var otherConvertedPath = System.IO.Path.Combine(convertedFolder, "001_128k.m4a");
        System.IO.File.WriteAllText(sourcePath, "flac");
        System.IO.File.WriteAllText(selectedConvertedPath, "aac64");
        System.IO.File.WriteAllText(otherConvertedPath, "aac128");
        System.IO.File.SetLastWriteTimeUtc(selectedConvertedPath, System.IO.File.GetLastWriteTimeUtc(sourcePath).AddMinutes(1));
        System.IO.File.SetLastWriteTimeUtc(otherConvertedPath, System.IO.File.GetLastWriteTimeUtc(sourcePath).AddMinutes(2));

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "audio-disc-project.json"), CreateAudioDiscManifest(
            projectFolder.Path,
            "AAC Mono 64 kbps",
            AudioDiscTrackStatus.Ripped));

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = rippedFolder,
            SelectedPreset = "AAC Mono 64 kbps",
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = sourcePath,
                    SourceFileName = "001_Test.flac",
                    Preset = "AAC Mono 64 kbps",
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = selectedConvertedPath,
                    CompletedUtc = DateTime.UtcNow.AddMinutes(-2)
                },
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = sourcePath,
                    SourceFileName = "001_Test.flac",
                    Preset = "AAC Stereo 128 kbps",
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = otherConvertedPath,
                    CompletedUtc = DateTime.UtcNow
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal("AAC Mono 64 kbps", plan.SelectedPreset);
        var track = Assert.Single(plan.Tracks);
        Assert.Equal("AAC Mono 64 kbps", track.Preset);
        Assert.Equal(selectedConvertedPath, track.ConvertedPath);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, track.Status);
    }

    [Fact]
    public void BuildFromProjectFolder_AudioDiscResume_UsesRequestedPresetOverrideForVisibleTrackState()
    {
        using var projectFolder = new TemporaryFolder();

        var rippedFolder = System.IO.Path.Combine(projectFolder.Path, "ripped");
        var convertedFolder = System.IO.Path.Combine(projectFolder.Path, "converted");
        System.IO.Directory.CreateDirectory(rippedFolder);
        System.IO.Directory.CreateDirectory(convertedFolder);

        var sourcePath = System.IO.Path.Combine(rippedFolder, "001_Test.flac");
        var monoConvertedPath = System.IO.Path.Combine(convertedFolder, "001_64k.m4a");
        var stereoConvertedPath = System.IO.Path.Combine(convertedFolder, "001_128k.m4a");
        System.IO.File.WriteAllText(sourcePath, "flac");
        System.IO.File.WriteAllText(monoConvertedPath, "aac64");
        System.IO.File.WriteAllText(stereoConvertedPath, "aac128");
        var sourceWriteTime = System.IO.File.GetLastWriteTimeUtc(sourcePath);
        System.IO.File.SetLastWriteTimeUtc(monoConvertedPath, sourceWriteTime.AddMinutes(1));
        System.IO.File.SetLastWriteTimeUtc(stereoConvertedPath, sourceWriteTime.AddMinutes(2));

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "audio-disc-project.json"), CreateAudioDiscManifest(
            projectFolder.Path,
            "AAC Stereo 128 kbps",
            AudioDiscTrackStatus.Ripped));

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = rippedFolder,
            SelectedPreset = "AAC Stereo 128 kbps",
            Export = new ExportManifestExportSettings
            {
                SelectedPreset = "AAC Stereo 128 kbps"
            },
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = sourcePath,
                    SourceFileName = "001_Test.flac",
                    Preset = "AAC Mono 64 kbps",
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = monoConvertedPath,
                    CompletedUtc = DateTime.UtcNow.AddMinutes(-2)
                },
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = sourcePath,
                    SourceFileName = "001_Test.flac",
                    Preset = "AAC Stereo 128 kbps",
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = stereoConvertedPath,
                    CompletedUtc = DateTime.UtcNow
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path, "AAC Mono 64 kbps");

        Assert.NotNull(plan);
        Assert.Equal("AAC Mono 64 kbps", plan.SelectedPreset);
        var track = Assert.Single(plan.Tracks);
        Assert.Equal("AAC Mono 64 kbps", track.Preset);
        Assert.Equal(monoConvertedPath, track.ConvertedPath);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, track.Status);
    }

    [Fact]
    public void BuildFromProjectFolder_AudioDiscResume_MatchesPresetStateBySourcePathAfterReorderingAndExclusion()
    {
        using var projectFolder = new TemporaryFolder();

        var rippedFolder = System.IO.Path.Combine(projectFolder.Path, "ripped");
        var convertedFolder = System.IO.Path.Combine(projectFolder.Path, "converted");
        System.IO.Directory.CreateDirectory(rippedFolder);
        System.IO.Directory.CreateDirectory(convertedFolder);

        var source1 = System.IO.Path.Combine(rippedFolder, "001_Test.flac");
        var source2 = System.IO.Path.Combine(rippedFolder, "002_Test.flac");
        var source3 = System.IO.Path.Combine(rippedFolder, "003_Test.flac");
        System.IO.File.WriteAllText(source1, "flac1");
        System.IO.File.WriteAllText(source2, "flac2");
        System.IO.File.WriteAllText(source3, "flac3");

        var converted1 = System.IO.Path.Combine(convertedFolder, "001_Test.m4a");
        var converted3 = System.IO.Path.Combine(convertedFolder, "003_Test.m4a");
        System.IO.File.WriteAllText(converted1, "aac1");
        System.IO.File.WriteAllText(converted3, "aac3");
        System.IO.File.SetLastWriteTimeUtc(converted1, System.IO.File.GetLastWriteTimeUtc(source1).AddMinutes(1));
        System.IO.File.SetLastWriteTimeUtc(converted3, System.IO.File.GetLastWriteTimeUtc(source3).AddMinutes(1));

        var audioManifest = CreateAudioDiscManifest(
            projectFolder.Path,
            "AAC Mono 64 kbps",
            AudioDiscTrackStatus.Ripped);
        audioManifest.Discs[0].Tracks =
        [
            audioManifest.Discs[0].Tracks[0],
            new AudioDiscProjectManifestTrack
            {
                GlobalIndex = 2,
                DiscNumber = 1,
                TrackNumber = 2,
                FileName = "002_Test.flac",
                RelativePath = System.IO.Path.Combine("ripped", "002_Test.flac"),
                Status = AudioDiscTrackStatus.Ripped
            },
            new AudioDiscProjectManifestTrack
            {
                GlobalIndex = 3,
                DiscNumber = 1,
                TrackNumber = 3,
                FileName = "003_Test.flac",
                RelativePath = System.IO.Path.Combine("ripped", "003_Test.flac"),
                Status = AudioDiscTrackStatus.Ripped
            }
        ];
        audioManifest.Discs[0].Tracks[0].FileName = "001_Test.flac";
        audioManifest.Discs[0].Tracks[0].RelativePath = System.IO.Path.Combine("ripped", "001_Test.flac");
        WriteJson(System.IO.Path.Combine(projectFolder.Path, "audio-disc-project.json"), audioManifest);

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = rippedFolder,
            SelectedPreset = "AAC Mono 64 kbps",
            Export = new ExportManifestExportSettings { SelectedPreset = "AAC Mono 64 kbps" },
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = source3,
                    SourceFileName = "003_Test.flac",
                    Preset = "AAC Mono 64 kbps",
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = converted3,
                    CompletedUtc = DateTime.UtcNow
                },
                new ExportWorkManifestTrack
                {
                    TrackIndex = 2,
                    SourcePath = source1,
                    SourceFileName = "001_Test.flac",
                    Preset = "AAC Mono 64 kbps",
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = converted1,
                    CompletedUtc = DateTime.UtcNow
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path, "AAC Mono 64 kbps");

        Assert.NotNull(plan);
        Assert.Equal(3, plan.Tracks.Count);
        var migratedDiscFolder = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder.Path, 1);
        var migratedSource1 = System.IO.Path.Combine(migratedDiscFolder, "001_Test.flac");
        var migratedSource2 = System.IO.Path.Combine(migratedDiscFolder, "002_Test.flac");
        var migratedSource3 = System.IO.Path.Combine(migratedDiscFolder, "003_Test.flac");
        Assert.Equal(converted1, plan.Tracks.Single(track => track.SourcePath == migratedSource1).ConvertedPath);
        Assert.Equal(ProjectManifestTrackStatuses.Pending, plan.Tracks.Single(track => track.SourcePath == migratedSource2).Status);
        Assert.Equal(converted3, plan.Tracks.Single(track => track.SourcePath == migratedSource3).ConvertedPath);
    }

    [Fact]
    public void BuildFromProjectFolder_AudioDiscResume_DoesNotTreatMissingOrPartialConvertedFilesAsComplete()
    {
        using var projectFolder = new TemporaryFolder();

        var rippedFolder = System.IO.Path.Combine(projectFolder.Path, "ripped");
        System.IO.Directory.CreateDirectory(rippedFolder);
        var sourcePath = System.IO.Path.Combine(rippedFolder, "001_Test.flac");
        System.IO.File.WriteAllText(sourcePath, "flac");

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "audio-disc-project.json"), CreateAudioDiscManifest(
            projectFolder.Path,
            "AAC Mono 64 kbps",
            AudioDiscTrackStatus.Ripped));

        var partialPath = System.IO.Path.Combine(projectFolder.Path, "converted", "001_Test.m4a.part");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(partialPath)!);
        System.IO.File.WriteAllText(partialPath, "partial");

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = rippedFolder,
            SelectedPreset = "AAC Mono 64 kbps",
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = sourcePath,
                    SourceFileName = "001_Test.flac",
                    Preset = "AAC Mono 64 kbps",
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = partialPath,
                    CompletedUtc = DateTime.UtcNow
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        var track = Assert.Single(plan.Tracks);
        Assert.Equal(string.Empty, track.ConvertedPath);
        Assert.Equal(ProjectManifestTrackStatuses.Pending, track.Status);
        Assert.Equal("Konvertieren", track.Action);
    }

    private static AudioDiscProjectManifest CreateAudioDiscManifest(
        string projectFolder,
        string preset,
        string trackStatus)
    {
        return new AudioDiscProjectManifest
        {
            ProjectFolder = projectFolder,
            Status = AudioDiscProjectStatus.Canceled,
            TotalDiscs = 1,
            ExportPreset = preset,
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
                            FileName = "001_Test.flac",
                            RelativePath = System.IO.Path.Combine("ripped", "001_Test.flac"),
                            ChapterTitle = "001 Test",
                            Duration = TimeSpan.FromMinutes(1),
                            Status = trackStatus
                        }
                    ]
                }
            ]
        };
    }

    [Fact]
    public void BuildFromProjectFolder_AudioDiscWaitingForNextDisc_ReportsNextMissingDisc()
    {
        using var projectFolder = new TemporaryFolder();

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "audio-disc-project.json"), new AudioDiscProjectManifest
        {
            ProjectFolder = projectFolder.Path,
            Status = AudioDiscProjectStatus.WaitingForDisc,
            TotalDiscs = 3,
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
                            FileName = "001_Test.flac",
                            RelativePath = System.IO.Path.Combine("ripped", "001_Test.flac"),
                            Duration = TimeSpan.FromMinutes(1),
                            Status = AudioDiscTrackStatus.Ripped
                        }
                    ]
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.True(plan.CanContinueDiscImport);
        Assert.Equal(2, plan.NextMissingDiscNumber);
        Assert.Equal(1, plan.ImportedDiscCount);
        Assert.Equal(AudioDiscProjectStatus.WaitingForDisc, plan.Status);
    }


    [Fact]
    public void BuildFromProjectFolder_CanceledFolderProject_RebuildsCompleteTrackListFromSourceFolder()
    {
        using var projectFolder = new TemporaryFolder();
        using var sourceFolder = new TemporaryFolder();

        var sourcePaths = Enumerable.Range(1, 4)
            .Select(index => System.IO.Path.Combine(sourceFolder.Path, $"{index:000}_Track.mp3"))
            .ToList();

        foreach (var sourcePath in sourcePaths)
            System.IO.File.WriteAllText(sourcePath, "audio");

        var convertedPath = System.IO.Path.Combine(projectFolder.Path, "converted", "001_Track.m4a");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(convertedPath)!);
        System.IO.File.WriteAllText(convertedPath, "converted");

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = sourceFolder.Path,
            SelectedPreset = "AAC Mono 64 kbps",
            State = new ExportManifestState { Status = ProjectManifestStatuses.Canceled },
            Resume = new ExportManifestResume { CanResume = true },
            Tracks =
            [
                new ExportWorkManifestTrack
                {
                    TrackIndex = 1,
                    SourcePath = sourcePaths[0],
                    SourceFileName = System.IO.Path.GetFileName(sourcePaths[0]),
                    ChapterTitle = "001 Track",
                    Preset = "AAC Mono 64 kbps",
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = convertedPath,
                    CompletedUtc = DateTime.UtcNow
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal(ProjectManifestTypes.FolderProject, plan.ProjectType);
        Assert.Equal(4, plan.Tracks.Count);
        Assert.Equal(sourcePaths, plan.Tracks.Select(track => track.SourcePath));
        Assert.Equal(ProjectManifestTrackStatuses.Converted, plan.Tracks[0].Status);
        Assert.Equal(ProjectManifestTrackStatuses.Pending, plan.Tracks[1].Status);
        Assert.Equal(ProjectManifestTrackStatuses.Pending, plan.Tracks[2].Status);
        Assert.Equal(ProjectManifestTrackStatuses.Pending, plan.Tracks[3].Status);
    }

    private static void WriteJson<T>(string path, T value)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            System.IO.Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
