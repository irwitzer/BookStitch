using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using System.IO;
using System.Linq;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscProjectServiceTests
{
    [Fact]
    public void CreateInitialManifest_UsesMetadataForContinuousFileAndChapterNames()
    {
        var service = new AudioDiscProjectService();
        var disc = CreateDisc(trackCount: 3);
        var setup = CreateSetup(title: "NSA");

        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            disc,
            discNumber: 1,
            setup,
            AudioDiscWorkingFormat.Flac);

        var tracks = manifest.Discs.Single().Tracks;
        Assert.Equal(new[] { "001_NSA.flac", "002_NSA.flac", "003_NSA.flac" }, tracks.Select(track => track.FileName));
        Assert.Equal(new[] { "001 NSA", "002 NSA", "003 NSA" }, tracks.Select(track => track.ChapterTitle));
        Assert.All(tracks, track => Assert.Equal(AudioDiscTrackStatus.Pending, track.Status));
        Assert.All(tracks, track => Assert.Equal(4500, track.SectorCount));
    }

    [Fact]
    public void BuildDiscEntry_ContinuesGlobalNumberingAcrossAdditionalDiscs()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 2),
            discNumber: 1,
            CreateSetup(title: "Stonehenge"),
            AudioDiscWorkingFormat.Flac);

        var secondDisc = service.BuildDiscEntry(manifest, CreateDisc(trackCount: 2), discNumber: 2, startingGlobalIndex: 3);

        Assert.Equal(new[] { 3, 4 }, secondDisc.Tracks.Select(track => track.GlobalIndex));
        Assert.Equal(new[] { "003_Stonehenge.flac", "004_Stonehenge.flac" }, secondDisc.Tracks.Select(track => track.FileName));
        Assert.All(secondDisc.Tracks, track => Assert.Equal(2, track.DiscNumber));
    }

    [Fact]
    public void CreateInitialManifest_UsesStableFallbackWhenTitleIsMissing()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 2),
            discNumber: 1,
            CreateSetup(title: ""),
            AudioDiscWorkingFormat.Flac);

        var tracks = manifest.Discs.Single().Tracks;
        Assert.Equal(new[] { "001_Track_01.flac", "002_Track_02.flac" }, tracks.Select(track => track.FileName));
        Assert.Equal(new[] { "001 Kapitel", "002 Kapitel" }, tracks.Select(track => track.ChapterTitle));
    }

    [Fact]
    public void CreateTrackPreview_ShowsAudioCdSourcePropertiesInsteadOfFutureWorkingFileProperties()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);

        var track = Assert.Single(service.CreateTrackPreview(manifest));

        Assert.Equal(@"CD-Laufwerk G:\", track.RelativeFolder);
        Assert.Equal("Audio-CD", track.Extension);
        Assert.Equal(string.Empty, track.TagTitle);
        Assert.Equal("001 NSA", track.ChapterTitle);
        Assert.Equal(1411, track.BitrateKbps);
        Assert.Equal(2, track.Channels);
        Assert.Equal("Stereo", track.ChannelLayout);
        Assert.Equal("PCM", track.Codec);
        Assert.Equal("FLAC rippen", track.ProcessingAction);
        Assert.Equal(10.1d, track.SizeMb, precision: 1);
    }


    [Fact]
    public void ApplyManifestMetadataToRippedTracks_RestoresProjectChapterDataAndClearsMetadataHints()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 2),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        var tracks = new List<TrackInfo>
        {
            new()
            {
                FileName = "001_NSA.flac",
                TagTitle = "",
                ChapterTitle = "NSA",
                Warning = "Kein Tag-Titel; Doppelter Kapitelname",
                Codec = "FLAC",
                ProcessingAction = "Konvertieren",
                AudioValidationPassed = true
            },
            new()
            {
                FileName = "002_NSA.flac",
                TagTitle = "",
                ChapterTitle = "NSA",
                Warning = "Kein Tag-Titel; Doppelter Kapitelname",
                Codec = "FLAC",
                ProcessingAction = "Konvertieren",
                AudioValidationPassed = true
            }
        };

        service.ApplyManifestMetadataToRippedTracks(manifest, tracks);

        Assert.Equal(new[] { "001 NSA", "002 NSA" }, tracks.Select(track => track.ChapterTitle));
        Assert.All(tracks, track => Assert.Equal(string.Empty, track.TagTitle));
        Assert.All(tracks, track => Assert.Equal(string.Empty, track.Warning));
        Assert.Equal(new int?[] { 1, 2 }, tracks.Select(track => track.TrackNumber).ToArray());
    }

    [Fact]
    public void ApplyManifestMetadataToRippedTracks_PreservesBlockingAudioErrors()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        var track = new TrackInfo
        {
            FileName = "001_NSA.flac",
            Warning = "Keine gültige Audiodatei erkannt.",
            Codec = "Ungültig",
            ProcessingAction = "Ungültig",
            AudioValidationPassed = false
        };

        service.ApplyManifestMetadataToRippedTracks(manifest, new[] { track });

        Assert.Equal("001 NSA", track.ChapterTitle);
        Assert.Equal("Keine gültige Audiodatei erkannt.", track.Warning);
        Assert.Equal("Ungültig", track.Codec);
    }

    [Fact]
    public void CreateInitialManifest_StoresDriveDiagnosticsForProjectAndDisc()
    {
        var service = new AudioDiscProjectService();
        var drive = new DiscDriveInfo(
            @"G:\",
            "G:",
            true,
            "NSA CD 1",
            DiscMediaKind.AudioCd,
            "Amazon Basics DVD Writer",
            @"\Device\CdRom1");

        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac,
            drive);

        Assert.Equal(@"G:\", manifest.SourceDriveRoot);
        Assert.Equal("Amazon Basics DVD Writer", manifest.SourceDriveName);
        Assert.Equal(@"\Device\CdRom1", manifest.SourceDriveDevicePath);
        Assert.Equal("NSA CD 1", manifest.SourceVolumeLabel);

        var disc = Assert.Single(manifest.Discs);
        Assert.Equal(manifest.SourceDriveName, disc.SourceDriveName);
        Assert.Equal(manifest.SourceDriveDevicePath, disc.SourceDriveDevicePath);
        Assert.Equal(manifest.SourceVolumeLabel, disc.SourceVolumeLabel);
    }

    [Fact]
    public void AddDisc_AppendsDiscWithContinuousGlobalNumbering()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 2, identity: "disc-one"),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);

        var added = service.AddDisc(
            manifest,
            CreateDisc(trackCount: 3, identity: "disc-two"),
            discNumber: 2);

        Assert.Equal(2, manifest.Discs.Count);
        Assert.Equal(new[] { 3, 4, 5 }, added.Tracks.Select(track => track.GlobalIndex));
        Assert.Equal(new[] { "003_NSA.flac", "004_NSA.flac", "005_NSA.flac" }, added.Tracks.Select(track => track.FileName));
        Assert.Equal(AudioDiscStatus.Pending, added.Status);
    }

    [Fact]
    public void AddDisc_ReturnsExistingEntryWhenSameDiscAndNumberAreRepeated()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 2, identity: "disc-one"),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        var first = service.AddDisc(manifest, CreateDisc(2, "disc-two"), discNumber: 2);

        var repeated = service.AddDisc(manifest, CreateDisc(2, "DISC-TWO"), discNumber: 2);

        Assert.Same(first, repeated);
        Assert.Equal(2, manifest.Discs.Count);
    }

    [Fact]
    public void AddDisc_RejectsSamePhysicalDiscUnderAnotherNumber()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 2, identity: "disc-one"),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.AddDisc(manifest, CreateDisc(2, "disc-one"), discNumber: 2));

        Assert.Contains("bereits als Disc 1", exception.Message);
        Assert.Single(manifest.Discs);
    }

    [Fact]
    public void AddDisc_RejectsDifferentPhysicalDiscForOccupiedNumber()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 2, identity: "disc-one"),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.AddDisc(manifest, CreateDisc(2, "other-disc"), discNumber: 1));

        Assert.Contains("bereits mit einer anderen Audio-CD belegt", exception.Message);
        Assert.Single(manifest.Discs);
    }


    [Fact]
    public void UpdateSnapshot_RefreshesEditableMetadataAndChapterSuggestions()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 2),
            discNumber: 1,
            CreateSetup(title: "Alter Titel"),
            AudioDiscWorkingFormat.Flac);
        var originalFileNames = manifest.Discs.Single().Tracks.Select(track => track.FileName).ToList();
        var snapshot = new ProjectSnapshotUiState(
            "AAC Stereo 128 kbps",
            "8",
            ".m4a",
            @"D:\Ausgabe",
            "{Titel}",
            "Neuer Titel",
            "Neue Autorin",
            "Neuer Titel",
            "Neue Sprecherin",
            "Hörbuch",
            @"D:\Cover.png",
            @"D:\Cover.jpg",
            "Neuer Titel.m4a");

        var changed = service.UpdateSnapshot(manifest, snapshot);

        Assert.True(changed);
        Assert.Equal("AAC Stereo 128 kbps", manifest.ExportPreset);
        Assert.Equal("8", manifest.ParallelJobs);
        Assert.Equal(".m4a", manifest.OutputExtension);
        Assert.Equal(@"D:\Ausgabe", manifest.OutputFolder);
        Assert.Equal("{Titel}", manifest.FileNameTemplate);
        Assert.Equal("Neuer Titel", manifest.Title);
        Assert.Equal("Neue Autorin", manifest.Author);
        Assert.Equal("Neuer Titel", manifest.Album);
        Assert.Equal("Neue Sprecherin", manifest.Narrator);
        Assert.Equal("Hörbuch", manifest.Genre);
        Assert.Equal(@"D:\Cover.png", manifest.CoverSourcePath);
        Assert.Equal(@"D:\Cover.jpg", manifest.ProcessedCoverPath);
        Assert.Equal(new[] { "001 Neuer Titel", "002 Neuer Titel" },
            manifest.Discs.Single().Tracks.Select(track => track.ChapterTitle));
        Assert.Equal(originalFileNames, manifest.Discs.Single().Tracks.Select(track => track.FileName));
    }

    [Fact]
    public void UpdateSnapshot_ReturnsFalseWhenNothingChanged()
    {
        var service = new AudioDiscProjectService();
        var setup = CreateSetup(title: "NSA");
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1),
            discNumber: 1,
            setup,
            AudioDiscWorkingFormat.Flac);
        var snapshot = new ProjectSnapshotUiState(
            setup.SelectedExportPreset,
            setup.ParallelJobs,
            setup.OutputExtension,
            setup.OutputFolder,
            setup.FileNameTemplate,
            setup.BookTitle,
            setup.Author,
            setup.BookTitle,
            setup.Narrator,
            setup.Genre,
            setup.CoverSourcePath,
            setup.ProcessedCoverPath,
            "unused-preview.m4b");

        var changed = service.UpdateSnapshot(manifest, snapshot);

        Assert.False(changed);
    }

    [Fact]
    public void GetNextRequiredDiscNumber_ReturnsFirstMissingOrIncompleteDisc()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1, identity: "disc-one"),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        var firstDisc = manifest.Discs.Single();
        firstDisc.Tracks.Single().Status = AudioDiscTrackStatus.Ripped;
        service.RefreshProgressState(manifest);
        service.AddDisc(manifest, CreateDisc(1, "disc-three"), discNumber: 3);

        Assert.Equal(2, service.GetNextRequiredDiscNumber(manifest));
        Assert.Equal(1, service.CountCompletedDiscs(manifest));
        Assert.False(service.IsProjectRipCompleted(manifest));
        Assert.Equal(AudioDiscProjectStatus.WaitingForDisc, manifest.Status);
    }

    [Fact]
    public void MarkDiscCompleted_CompletesProjectOnlyAfterEveryConfiguredDisc()
    {
        var service = new AudioDiscProjectService();
        var setup = CreateSetup(title: "NSA") with { TotalDiscs = 2 };
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1, identity: "disc-one"),
            discNumber: 1,
            setup,
            AudioDiscWorkingFormat.Flac);
        var second = service.AddDisc(manifest, CreateDisc(1, "disc-two"), discNumber: 2);

        manifest.Discs[0].Tracks.Single().Status = AudioDiscTrackStatus.Ripped;
        service.MarkDiscCompleted(manifest, discNumber: 1, TimeSpan.FromMinutes(2));

        Assert.Equal(AudioDiscStatus.Completed, manifest.Discs[0].Status);
        Assert.Equal(AudioDiscProjectStatus.WaitingForDisc, manifest.Status);
        Assert.Null(manifest.CompletedUtc);

        second.Tracks.Single().Status = AudioDiscTrackStatus.Ripped;
        service.MarkDiscCompleted(manifest, discNumber: 2, TimeSpan.FromMinutes(3));

        Assert.True(service.IsProjectRipCompleted(manifest));
        Assert.Equal(AudioDiscProjectStatus.RippingCompleted, manifest.Status);
        Assert.NotNull(manifest.CompletedUtc);
        Assert.Equal(TimeSpan.FromMinutes(5), manifest.RipDuration);
        Assert.Equal(2, service.CountCompletedDiscs(manifest));
    }

    [Fact]
    public void MarkProjectCanceled_ResetsActiveDiscToPendingForResume()
    {
        var service = new AudioDiscProjectService();
        var setup = CreateSetup(title: "NSA") with { TotalDiscs = 2 };
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1, identity: "disc-one"),
            discNumber: 1,
            setup,
            AudioDiscWorkingFormat.Flac);
        var second = service.AddDisc(manifest, CreateDisc(2, "disc-two"), discNumber: 2);

        manifest.Discs[0].Tracks.Single().Status = AudioDiscTrackStatus.Ripped;
        service.MarkDiscCompleted(manifest, discNumber: 1, TimeSpan.FromMinutes(2));
        service.MarkDiscRipping(manifest, discNumber: 2);
        second.Tracks[0].Status = AudioDiscTrackStatus.Ripped;

        service.MarkProjectCanceled(manifest);

        Assert.Equal(AudioDiscProjectStatus.Canceled, manifest.Status);
        Assert.Equal(AudioDiscStatus.Completed, manifest.Discs[0].Status);
        Assert.Equal(AudioDiscStatus.Pending, second.Status);
        Assert.Equal(AudioDiscTrackStatus.Ripped, second.Tracks[0].Status);
        Assert.Equal(AudioDiscTrackStatus.Pending, second.Tracks[1].Status);
        Assert.Null(second.CompletedUtc);
        Assert.Equal(string.Empty, manifest.ErrorMessage);
    }

    [Fact]
    public void MarkDiscCompleted_RejectsDiscWithPendingTracks()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);

        Assert.Throws<InvalidOperationException>(() => service.MarkDiscCompleted(manifest, discNumber: 1));
        Assert.Equal(AudioDiscStatus.Pending, manifest.Discs.Single().Status);
    }

    [Fact]
    public void TryLoad_MigratesVersionOneManifestAndDerivesCompletedState()
    {
        var service = new AudioDiscProjectService();
        var projectFolder = Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Load-" + Guid.NewGuid());
        Directory.CreateDirectory(projectFolder);
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        try
        {
            File.WriteAllText(
                ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder),
                """
                {
                  "FormatVersion": "1",
                  "ProjectType": "AudioCdProject",
                  "ProjectFolder": "ignored",
                  "TotalDiscs": 1,
                  "Discs": [
                    {
                      "DiscNumber": 1,
                      "DiscIdentity": "legacy-disc",
                      "Tracks": [
                        {
                          "GlobalIndex": 1,
                          "DiscNumber": 1,
                          "TrackNumber": 1,
                          "FileName": "001_NSA.flac",
                          "RelativePath": "ripped/001_NSA.flac",
                          "Status": "Ripped"
                        }
                      ]
                    }
                  ]
                }
                """);

            var manifest = service.TryLoad(projectFolder);

            Assert.NotNull(manifest);
            Assert.Equal(AudioDiscProjectManifestVersions.Current, manifest.FormatVersion);
            Assert.Equal(AudioDiscProjectStatus.RippingCompleted, manifest.Status);
            Assert.Equal(AudioDiscStatus.Completed, manifest.Discs.Single().Status);
            Assert.NotNull(manifest.CompletedUtc);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void ExportLifecycle_PersistsPauseCompletionAndFinalOutput()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);

        service.MarkExportStarted(manifest);

        Assert.Equal(AudioDiscExportStatus.Exporting, manifest.ExportStatus);
        Assert.NotNull(manifest.ExportStartedUtc);
        Assert.Null(manifest.ExportCompletedUtc);

        service.MarkExportPausedBeforeMerge(manifest);

        Assert.Equal(AudioDiscExportStatus.PausedBeforeMerge, manifest.ExportStatus);
        Assert.Null(manifest.ExportCompletedUtc);

        service.MarkExportCompleted(manifest, @"C:\Output\NSA.m4b");

        Assert.Equal(AudioDiscExportStatus.Completed, manifest.ExportStatus);
        Assert.NotNull(manifest.ExportCompletedUtc);
        Assert.Equal(@"C:\Output\NSA.m4b", manifest.FinalOutputPath);
        Assert.Equal(string.Empty, manifest.ExportErrorMessage);
    }

    [Fact]
    public void ExportLifecycle_CanceledAndFailedStatesClearStaleCompletionData()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);

        service.MarkExportCompleted(manifest, @"C:\Output\NSA.m4b");
        service.MarkExportCanceled(manifest);

        Assert.Equal(AudioDiscExportStatus.Canceled, manifest.ExportStatus);
        Assert.Null(manifest.ExportCompletedUtc);
        Assert.Equal(string.Empty, manifest.ExportErrorMessage);

        service.MarkExportFailed(manifest, "Merge fehlgeschlagen");

        Assert.Equal(AudioDiscExportStatus.Failed, manifest.ExportStatus);
        Assert.Null(manifest.ExportCompletedUtc);
        Assert.Equal("Merge fehlgeschlagen", manifest.ExportErrorMessage);
    }


    [Fact]
    public void CreateTrackPreview_FromDetectedDisc_IsAvailableBeforeProjectCreation()
    {
        var service = new AudioDiscProjectService();
        var disc = CreateDisc(trackCount: 2);

        var tracks = service.CreateTrackPreview(
            disc,
            title: "Hörbuch",
            author: "Autor",
            workingFormat: AudioDiscWorkingFormat.Flac);

        Assert.Equal(2, tracks.Count);
        Assert.Equal("001_Hörbuch.flac", tracks[0].FileName);
        Assert.Equal("001 Hörbuch", tracks[0].ChapterTitle);
        Assert.Equal("Autor", tracks[0].Artist);
        Assert.Equal("Noch nicht gerippt", tracks[0].Status);
    }

    [Fact]
    public void UpdateDiscSourceDrive_UsesMatchingDiscIdentityAndUpdatesDiagnostics()
    {
        var service = new AudioDiscProjectService();
        var originalDisc = CreateDisc(trackCount: 2, identity: "same-disc");
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            originalDisc,
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        var relocatedDisc = originalDisc with { DriveRoot = @"Y:\", DriveLetter = "Y:" };
        var driveInfo = new DiscDriveInfo(@"Y:\", "Y:", true, "Audio CD", DiscMediaKind.AudioCd, "Drive Y", @"\Device\CdRom1");

        service.UpdateDiscSourceDrive(manifest, 1, relocatedDisc, driveInfo);

        Assert.Equal(@"Y:\", manifest.SourceDriveRoot);
        Assert.Equal(@"Y:\", manifest.Discs[0].SourceDriveRoot);
        Assert.Equal("Drive Y", manifest.Discs[0].SourceDriveName);
        Assert.Equal(@"\Device\CdRom1", manifest.Discs[0].SourceDriveDevicePath);
    }

    [Fact]
    public void UpdateDiscSourceDrive_RejectsDifferentDiscIdentity()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1, identity: "expected"),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.UpdateDiscSourceDrive(
                manifest,
                1,
                CreateDisc(trackCount: 1, identity: "different"),
                null));

        Assert.Contains("entspricht nicht", exception.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void MarkExportCompleted_PreservesSuccessfulExportHistoryDuringLaterPreparation()
    {
        var service = new AudioDiscProjectService();
        var manifest = new AudioDiscProjectManifest();

        service.MarkExportCompleted(manifest, @"C:\Output\Book.m4b");
        service.MarkExportStarted(manifest);
        service.MarkExportPausedBeforeMerge(manifest);

        Assert.True(manifest.HasSuccessfulExport);
        Assert.Equal(@"C:\Output\Book.m4b", manifest.LastSuccessfulOutputPath);
        Assert.NotNull(manifest.LastSuccessfulExportUtc);
    }

    [Fact]
    public void IncreaseTotalDiscsForAdditionalRip_ExtendsOnlyBeyondCurrentProject()
    {
        var service = new AudioDiscProjectService();
        var manifest = new AudioDiscProjectManifest { TotalDiscs = 1 };

        service.IncreaseTotalDiscsForAdditionalRip(manifest, 2);

        Assert.Equal(2, manifest.TotalDiscs);
        Assert.Equal(ProjectPipelineStateNames.AcquiringSources, manifest.PipelineState);
    }

    private static AudioDiscInfo CreateDisc(int trackCount, string identity = "disc-identity")
    {
        var tracks = Enumerable.Range(1, trackCount)
            .Select(number => new AudioDiscTrackInfo(
                number,
                TimeSpan.FromMinutes(number - 1),
                TimeSpan.FromMinutes(1),
                $"track-{number}",
                150 + ((number - 1) * 4500)))
            .ToList();

        var offsets = tracks.Select(track => track.SectorOffset!.Value).ToList();
        var leadOut = 150 + (trackCount * 4500);

        return new AudioDiscInfo(
            @"G:\",
            "G:",
            tracks,
            TimeSpan.FromMinutes(trackCount),
            identity,
            new AudioDiscToc(1, trackCount, leadOut, offsets, "musicbrainz-id"));
    }

    private static DiscProjectSetupResult CreateSetup(string title) => new(
        TotalDiscs: 4,
        SelectedExportPreset: "AAC Mono 64 kbps",
        ParallelJobs: "Auto",
        OutputExtension: ".m4b",
        OutputFolder: @"C:\Output",
        BookTitle: title,
        Album: title,
        Author: "Autor",
        Narrator: "Sprecher",
        Genre: "iBook Hörbuch",
        FileNameTemplate: "{Autor} - {Titel}",
        CoverSourcePath: "",
        ProcessedCoverPath: "",
        AutoMergeAfterConversion: false);
}
