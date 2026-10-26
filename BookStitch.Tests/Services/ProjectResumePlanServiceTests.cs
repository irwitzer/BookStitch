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
    public void Loading_a_project_recovers_an_interrupted_extension_before_deriving_the_resume_state()
    {
        using var folder = new TemporaryFolder();
        var discs = new Mp3DiscProjectService();
        var original = new Mp3DiscProjectManifest { ProjectFolder = folder.Path, TotalDiscs = 2,
            PipelineState = ProjectPipelineStateNames.Completed, ExportPreset = "AAC Mono 64 kbps" };
        discs.Save(original);
        var work = new WorkManifestService();
        work.Save(ProjectFolderLayout.GetExportManifestPath(folder.Path), new ExportWorkManifest
        {
            ProjectWorkFolder = folder.Path, ProjectType = ProjectManifestTypes.Mp3DiscProject,
            SelectedPreset = original.ExportPreset, State = new() { Status = ProjectManifestStatuses.Completed }
        });
        var rollback = new ProjectExtensionRollbackService();
        var snapshot = rollback.Capture(folder.Path) with { Kind = "AdditionalSources" };
        new AtomicJsonStore().Save(ProjectExtensionRollbackService.JournalPath(folder.Path),
            new ProjectRollbackJournal { Snapshot = snapshot }, null);
        original.TotalDiscs = 5;
        original.PipelineState = ProjectPipelineStateNames.AcquiringSources;
        discs.Save(original);
        var plan = _service.BuildFromProjectFolder(folder.Path)!;
        Assert.Equal(2, discs.TryLoad(folder.Path)!.TotalDiscs);
        Assert.True(plan.HasSuccessfulExport);
        Assert.False(plan.CanContinueDiscImport);
        Assert.False(File.Exists(ProjectExtensionRollbackService.JournalPath(folder.Path)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mp3_resume_uses_canonical_cover_including_explicit_removal(bool removed)
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var metadata = new ProjectMetadata();
        metadata.SetCover(removed ? "" : "user.jpg", removed ? "" : "processed-user.jpg", MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder.Path, metadata);
        new Mp3DiscProjectService().Save(new Mp3DiscProjectManifest { ProjectFolder = folder.Path,
            CoverSourcePath = "old.jpg", ProcessedCoverPath = "processed-old.jpg" });
        var plan = _service.BuildFromProjectFolder(folder.Path)!;
        Assert.Equal(metadata.Cover.SourcePath, plan.CoverSourcePath);
        Assert.Equal(metadata.Cover.ProcessedPath, plan.ProcessedCoverPath);
    }

    [Theory]
    [InlineData(2, "Completed", "Completed", false, false)]
    [InlineData(5, "Completed", "Completed", false, false)]
    [InlineData(5, "Completed", "ReviewBeforeMerge", false, false)]
    [InlineData(5, "AcquiringSources", "Completed", false, true)]
    [InlineData(5, "Completed", "Completed", true, true)]
    [InlineData(5, "Completed", "Converting", false, true)]
    public void Mp3_load_respects_current_lifecycle_agreement_without_inventing_active_work(
        int total, string importState, string exportState, bool stopped, bool continueImport)
    {
        using var folder = new TemporaryFolder();
        var manifest = new Mp3DiscProjectManifest { ProjectFolder = folder.Path, TotalDiscs = total,
            PipelineState = importState, IsStoppedForResume = stopped, ExportPreset = "AAC Mono 64 kbps" };
        var export = new ExportWorkManifest { ProjectWorkFolder = folder.Path, ProjectType = ProjectManifestTypes.Mp3DiscProject,
            SelectedPreset = manifest.ExportPreset, State = new() { Status = exportState, LastSuccessfulStep = "ExportCompleted" } };
        for (var disc = 1; disc <= 2; disc++)
        {
            var entry = new Mp3DiscProjectManifestDisc { DiscNumber = disc, Signature = "disc-" + disc,
                LocalFolder = ProjectFolderLayout.GetDiscOriginalsFolder(folder.Path, disc), FileCount = 150, CopiedFiles = 150 };
            Directory.CreateDirectory(entry.LocalFolder);
            for (var track = 1; track <= 150; track++)
            {
                var path = Path.Combine(entry.LocalFolder, track + ".mp3");
                File.WriteAllBytes(path, [1, 2, 3]);
                var id = Guid.NewGuid();
                entry.SourceFiles.Add(new() { SourceId = id, SourceFileName = Path.GetFileName(path), SourceSizeBytes = 3,
                    RelativePath = Path.GetRelativePath(ProjectFolderLayout.GetOriginalsFolder(folder.Path), path) });
                export.Tracks.Add(new() { SourceId = id, SourcePath = path, SourceFileName = Path.GetFileName(path),
                    TrackIndex = (disc - 1) * 150 + track, Preset = manifest.ExportPreset });
            }
            manifest.ImportedDiscs.Add(entry);
        }
        new Mp3DiscProjectService().Save(manifest);
        new WorkManifestService().Save(ProjectFolderLayout.GetExportManifestPath(folder.Path), export);
        var plan = _service.BuildFromProjectFolder(folder.Path)!;
        Assert.Equal(continueImport, plan.CanContinueDiscImport);
        Assert.Equal(2, plan.ImportedDiscCount);
        Assert.Equal(total, plan.TotalDiscs);
        Assert.Equal(300, plan.Tracks.Count);
        var importPath = ProjectFolderLayout.ResolveMp3DiscManifestPath(folder.Path);
        var before = File.ReadAllBytes(importPath);
        Assert.Equal(before, File.ReadAllBytes(importPath));
    }

    [Fact]
    public void Completed_mp3_resume_keeps_excluded_import_source_across_two_saves()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var discFolder = ProjectFolderLayout.ResolveDiscOriginalsFolder(folder.Path, 1);
        Directory.CreateDirectory(discFolder);
        var tracks = Enumerable.Range(1, 3).Select(index => {
            var path = Path.Combine(discFolder, $"{index:000}.mp3"); File.WriteAllText(path, "source");
            return new TrackInfo { SourceId = Guid.NewGuid(), FilePath = path, FileName = Path.GetFileName(path),
                Index = index, IsExcluded = index == 2 };
        }).ToArray();
        var import = new Mp3DiscProjectManifest { ProjectFolder = folder.Path, TotalDiscs = 1,
            ExportPreset = "AAC Mono 64 kbps", ImportedDiscs = [new() { DiscNumber = 1, LocalFolder = discFolder,
                SourceFiles = tracks.Select(t => new Mp3DiscProjectManifestSourceFile { SourceId = t.SourceId,
                    RelativePath = Path.GetRelativePath(ProjectFolderLayout.GetOriginalsFolder(folder.Path), t.FilePath),
                    SourceFileName = t.FileName }).ToList() }] };
        new Mp3DiscProjectService().Save(import);
        new WorkManifestService().Save(ProjectFolderLayout.GetExportManifestPath(folder.Path), new ExportWorkManifest {
            ProjectType = ProjectManifestTypes.Mp3DiscProject, ProjectWorkFolder = folder.Path,
            State = new() { Status = ProjectManifestStatuses.Completed }, SelectedPreset = import.ExportPreset,
            Tracks = tracks.Where(t => !t.IsExcluded).Select(t => new ExportWorkManifestTrack {
                SourceId = t.SourceId, SourcePath = t.FilePath, SourceFileName = t.FileName,
                TrackIndex = t.Index, Preset = import.ExportPreset }).ToList() });
        new TrackListStateService().Save(folder.Path, [tracks[2], tracks[1], tracks[0]]);
        for (var pass = 0; pass < 2; pass++)
        {
            var plan = _service.BuildFromProjectFolder(folder.Path)!;
            var restored = new TrackListStateService().Apply(folder.Path,
                plan.Tracks.Select(t => new TrackInfo { SourceId = t.SourceId, FilePath = t.SourcePath,
                    FileName = t.SourceFileName, Index = t.TrackIndex }));
            Assert.Equal(new[] { tracks[2].SourceId, tracks[1].SourceId, tracks[0].SourceId }, restored.Select(t => t.SourceId));
            Assert.True(restored[1].IsExcluded);
            new TrackListStateService().Save(folder.Path, restored);
        }
    }

    [Fact]
    public void Excluded_source_without_export_entry_restores_identity_exclusion_and_manual_order_after_reopen()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var originals = ProjectFolderLayout.GetOriginalsFolder(folder.Path);
        var tracks = Enumerable.Range(1, 3).Select(index =>
        {
            var path = Path.Combine(originals, $"{index:000}.mp3");
            File.WriteAllText(path, "source");
            var identity = new SourceCopyIdentityStore().RecordCopiedFile(folder.Path, path, path);
            return new TrackInfo { SourceId = identity.SourceId, FilePath = path,
                FileName = Path.GetFileName(path), Index = index, IsExcluded = index == 2 };
        }).ToArray();
        new TrackListStateService().Save(folder.Path, [tracks[2], tracks[1], tracks[0]]);
        var manifest = new ExportWorkManifest { ProjectWorkFolder = folder.Path,
            SourceFolder = originals, ProjectType = ProjectManifestTypes.FolderProject,
            SelectedPreset = "AAC Mono 64 kbps", State = new() { Status = ProjectManifestStatuses.Completed },
            Tracks = tracks.Where(track => !track.IsExcluded).Select(track => new ExportWorkManifestTrack {
                SourceId = track.SourceId, SourcePath = track.FilePath, SourceFileName = track.FileName,
                TrackIndex = track.Index, Preset = "AAC Mono 64 kbps" }).ToList() };
        new WorkManifestService().Save(ProjectFolderLayout.GetWorkManifestPath(folder.Path), manifest);
        var plan = new ProjectResumePlanService().BuildFromProjectFolder(folder.Path);
        Assert.NotNull(plan);
        Assert.Equal(tracks[1].SourceId, plan.Tracks.Single(track => track.SourceFileName == "002.mp3").SourceId);
        List<TrackInfo> Restore(ProjectResumePlan resumePlan) => new TrackListStateService().Apply(folder.Path,
            resumePlan.Tracks.Select(track => new TrackInfo {
                SourceId = track.SourceId, FilePath = track.SourcePath,
                FileName = track.SourceFileName, Index = track.TrackIndex }));
        var reopened = Restore(plan);
        Assert.Equal(new[] { "003.mp3", "002.mp3", "001.mp3" }, reopened.Select(track => track.FileName));
        Assert.True(reopened[1].IsExcluded);
        Assert.False(reopened[0].IsExcluded);
        Assert.False(reopened[2].IsExcluded);
        new TrackListStateService().Save(folder.Path, reopened);
        var second = Restore(new ProjectResumePlanService().BuildFromProjectFolder(folder.Path)!);
        Assert.True(second[1].IsExcluded);
    }

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
    public void BuildFromProjectFolder_Mp3DiscWithConflictingExportMetadata_MigratesDiscMetadataAsAuthoritative()
    {
        using var projectFolder = new TemporaryFolder();

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new Mp3DiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectFolder = projectFolder.Path,
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

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "export-project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.Mp3DiscProject,
            ProjectWorkFolder = projectFolder.Path,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Export Titel",
                Author = "Export Autor",
                Album = "Export Album",
                Narrator = "Export Sprecher",
                Genre = "Export Genre"
            },
            Resume = new ExportManifestResume { CanResume = true }
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal(ProjectManifestTypes.Mp3DiscProject, plan.ProjectType);
        Assert.Equal("Disc Titel", plan.BookTitle);
        Assert.Equal("Disc Autor", plan.Author);
        Assert.Equal("Disc Album", plan.Album);
        Assert.Equal("Disc Sprecher", plan.Narrator);
        Assert.Equal("Disc Genre", plan.Genre);
        Assert.Equal("Disc Autor - Disc Titel", plan.DisplayName);
    }

    [Fact]
    public void BuildFromProjectFolder_AudioDiscWithConflictingExportMetadata_MigratesDiscMetadataAsAuthoritative()
    {
        using var projectFolder = new TemporaryFolder();

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "audio-disc-project.json"), new AudioDiscProjectManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectFolder = projectFolder.Path,
            TotalDiscs = 1,
            Title = "Audio Titel",
            Author = "Audio Autor",
            Album = "Audio Album",
            Narrator = "Audio Sprecher",
            Genre = "Audio Genre",
            Discs =
            [
                new AudioDiscProjectManifestDisc { DiscNumber = 1, Status = AudioDiscStatus.Completed }
            ]
        });

        WriteJson(System.IO.Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.AudioCdProject,
            ProjectWorkFolder = projectFolder.Path,
            Metadata = new ExportManifestBookMetadata
            {
                Title = "Export Titel",
                Author = "Export Autor",
                Album = "Export Album",
                Narrator = "Export Sprecher",
                Genre = "Export Genre"
            },
            Resume = new ExportManifestResume { CanResume = true }
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal(ProjectManifestTypes.AudioCdProject, plan.ProjectType);
        Assert.Equal("Audio Titel", plan.BookTitle);
        Assert.Equal("Audio Autor", plan.Author);
        Assert.Equal("Audio Album", plan.Album);
        Assert.Equal("Audio Sprecher", plan.Narrator);
        Assert.Equal("Audio Genre", plan.Genre);
        Assert.Equal("Audio Autor - Audio Titel", plan.DisplayName);
    }

    [Fact]
    public void BuildFromProjectFolder_PreservesLegacyManifestChapterTitleAsResumeFallback()
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
                    ChapterTitle = "Legacy Kapitel"
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        var track = Assert.Single(plan.Tracks);
        Assert.Equal("Legacy Kapitel", track.ChapterTitle);
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
    public void BuildFromProjectFolder_CompletedMp3DiscProject_CarriesSourceIdFromImportManifest()
    {
        using var projectFolder = new TemporaryFolder();

        var discFolder = System.IO.Path.Combine(projectFolder.Path, "CD 01");
        System.IO.Directory.CreateDirectory(discFolder);
        var trackPath = System.IO.Path.Combine(discFolder, "Track 01.mp3");
        System.IO.File.WriteAllText(trackPath, "audio");
        var sourceId = Guid.NewGuid();

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
                    LocalFolder = discFolder,
                    SourceFiles =
                    [
                        new Mp3DiscProjectManifestSourceFile
                        {
                            SourceId = sourceId,
                            SourceFileName = "Track 01.mp3",
                            RelativePath = System.IO.Path.Combine("CD 01", "Track 01.mp3")
                        }
                    ]
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        var track = Assert.Single(plan.Tracks);
        Assert.Equal(sourceId, track.SourceId);
        Assert.Equal("Track 01.mp3", track.SourceFileName);
    }

    [Fact]
    public void BuildFromProjectFolder_Mp3DiscProjectWithExportManifest_KeepsFullImportedInventory()
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
            track => Assert.Equal(System.IO.Path.Combine(migratedDiscFolder, "Track 02.mp3"), track.SourcePath),
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
            Album = "Testalbum",
            Narrator = "Testsprecher",
            Genre = "Hörbuch",
            CoverSourcePath = @"D:\Cover\source.png",
            ProcessedCoverPath = @"D:\Cover\processed.jpg",
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
        Assert.Equal("Testalbum", plan.Album);
        Assert.Equal("Testsprecher", plan.Narrator);
        Assert.Equal("Hörbuch", plan.Genre);
        Assert.Equal(@"D:\Cover\source.png", plan.CoverSourcePath);
        Assert.Equal(@"D:\Cover\processed.jpg", plan.ProcessedCoverPath);
        Assert.Equal("AAC Mono 64 kbps", plan.SelectedPreset);
        Assert.Collection(
            plan.Tracks,
            track =>
            {
                Assert.Equal(System.IO.Path.Combine(migratedDiscFolder, "001_Test.flac"), track.SourcePath);
                Assert.Equal(ProcessingActionIds.Convert, track.Action);
            },
            track =>
            {
                Assert.Equal(System.IO.Path.Combine(migratedDiscFolder, "002_Test.flac"), track.SourcePath);
                Assert.Equal(ProcessingActionIds.RipFlac, track.Action);
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
        Assert.Equal(TimeSpan.FromMinutes(1).Ticks, plan.Tracks[0].DurationTicks);
        Assert.Equal(ProcessingActionIds.RipFlac, plan.Tracks[1].Action);
        Assert.Equal(ProcessingActionIds.RipFlac, plan.Tracks[2].Action);
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
    public void BuildFromProjectFolder_AudioDiscResume_MatchesExportStateBySourceIdBeforePathOrIndex()
    {
        using var projectFolder = new TemporaryFolder();

        var rippedFolder = System.IO.Path.Combine(projectFolder.Path, "ripped");
        var convertedFolder = System.IO.Path.Combine(projectFolder.Path, "converted");
        System.IO.Directory.CreateDirectory(rippedFolder);
        System.IO.Directory.CreateDirectory(convertedFolder);

        var manifestSourcePath = System.IO.Path.Combine(rippedFolder, "001_Test.flac");
        var staleExportSourcePath = System.IO.Path.Combine(rippedFolder, "renamed-source.flac");
        var convertedPath = System.IO.Path.Combine(convertedFolder, "001_Test.m4a");
        System.IO.File.WriteAllText(manifestSourcePath, "flac");
        System.IO.File.WriteAllText(convertedPath, "aac");
        System.IO.File.SetLastWriteTimeUtc(convertedPath, System.IO.File.GetLastWriteTimeUtc(manifestSourcePath).AddMinutes(1));
        var sourceId = Guid.NewGuid();

        var audioManifest = CreateAudioDiscManifest(
            projectFolder.Path,
            "AAC Mono 64 kbps",
            AudioDiscTrackStatus.Ripped);
        audioManifest.Discs[0].Tracks[0].SourceId = sourceId;
        audioManifest.Discs[0].Tracks[0].FileName = "001_Test.flac";
        audioManifest.Discs[0].Tracks[0].RelativePath = System.IO.Path.Combine("ripped", "001_Test.flac");
        WriteJson(System.IO.Path.Combine(projectFolder.Path, "audio-disc-project.json"), audioManifest);

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
                    TrackIndex = 99,
                    SourceId = sourceId,
                    SourcePath = staleExportSourcePath,
                    SourceFileName = "renamed-source.flac",
                    Preset = "AAC Mono 64 kbps",
                    Status = ProjectManifestTrackStatuses.Converted,
                    ConvertedPath = convertedPath,
                    CompletedUtc = DateTime.UtcNow
                }
            ]
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        var track = Assert.Single(plan.Tracks);
        Assert.Equal(sourceId, track.SourceId);
        Assert.Equal(convertedPath, track.ConvertedPath);
        Assert.Equal(ProjectManifestTrackStatuses.Converted, track.Status);
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
        Assert.Equal(ProcessingActionIds.Convert, track.Action);
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
        var ripped = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(projectFolder.Path, "ripped")).FullName;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(ripped, "001_Test.flac"), [1, 2, 3]);

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

    [Fact]
    public void BuildFromProjectFolder_UsesCatalogLocalImportFormatSet()
    {
        using var projectFolder = new TemporaryFolder();
        using var sourceFolder = new TemporaryFolder();
        var supportedFiles = new[]
        {
            "001.mp3",
            "002.m4a",
            "003.m4b",
            "004.aac",
            "005.wav",
            "006.flac",
            "007.wma",
            "008.ogg",
            "009.oga",
            "010.opus"
        };

        foreach (var fileName in supportedFiles)
            File.WriteAllText(Path.Combine(sourceFolder.Path, fileName), "audio");

        WriteJson(Path.Combine(projectFolder.Path, "project.json"), new ExportWorkManifest
        {
            ProjectType = ProjectManifestTypes.FolderProject,
            ProjectWorkFolder = projectFolder.Path,
            SourceFolder = sourceFolder.Path,
            State = new ExportManifestState { Status = ProjectManifestStatuses.Canceled },
            Resume = new ExportManifestResume { CanResume = true }
        });

        var plan = _service.BuildFromProjectFolder(projectFolder.Path);

        Assert.NotNull(plan);
        Assert.Equal(supportedFiles, plan.Tracks.Select(track => track.SourceFileName).ToArray());
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
