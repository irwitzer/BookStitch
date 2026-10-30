using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscProjectServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddDisc_AdvancesDriveCircleAcrossMultipleRounds(bool virtualDrivesEnabled)
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var rotation = new DiscDriveRotationService();
        var roots = new[] { @"U:\", @"V:\", @"Y:\", @"Z:\", @"X:\" };
        var configured = roots.Select((root, order) => new DiscDriveConfigurationItem(
            new ConfiguredDiscDrive { DriveRoot = root, Order = order,
                IsEnabled = virtualDrivesEnabled || (root != @"Y:\" && root != @"Z:\") }, true, null)).ToArray();
        var activeRoots = configured.Where(item => item.Configuration.IsEnabled)
            .Select(item => item.Configuration.DriveRoot).ToArray();
        var manifest = service.CreateInitialManifest(folder.Path,
            CreateDisc(1, "disc-1") with { DriveRoot = roots[0], DriveLetter = "U:" },
            1, CreateSetup("Circle"), AudioDiscWorkingFormat.Flac);
        manifest.TotalDiscs = activeRoots.Length * 2 + 1;

        for (var number = 2; number <= manifest.TotalDiscs; number++)
        {
            var expectedRoot = activeRoots[(number - 1) % activeRoots.Length];
            Assert.Equal(expectedRoot, rotation.BuildRound(configured, manifest.SourceDriveRoot)[0]);
            var disc = CreateDisc(1, $"disc-{number}") with
                { DriveRoot = expectedRoot, DriveLetter = expectedRoot[..2] };
            var driveInfo = new DiscDriveInfo(expectedRoot, expectedRoot[..2], true,
                "Audio CD", DiscMediaKind.AudioCd, $"Drive {number}", $@"\Device\CdRom{number}");
            var added = service.AddDisc(manifest, disc, number, driveInfo);
            Assert.Equal(expectedRoot, added.SourceDriveRoot);
            Assert.Equal(expectedRoot, manifest.SourceDriveRoot);
            Assert.Equal(added.SourceDriveName, manifest.SourceDriveName);
            Assert.Equal(added.SourceDriveDevicePath, manifest.SourceDriveDevicePath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepared_manifest_survives_successful_or_failed_metadata_attachment_without_ripping(bool failAttachment)
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(folder.Path, CreateDisc(trackCount: 2), 1,
            CreateSetup(title: "Prepared"), AudioDiscWorkingFormat.Flac);
        service.Save(manifest);
        var session = new ProjectMetadataSession();
        session.BeginNew();
        if (failAttachment)
        {
            new ProjectMetadataStore().Save(folder.Path, new ProjectMetadata());
            Assert.Throws<InvalidOperationException>(() => session.AttachNewProject(folder.Path));
        }
        else session.AttachNewProject(folder.Path);
        var loaded = service.TryLoad(folder.Path)!;
        Assert.Equal(manifest.ProjectId, loaded.ProjectId);
        Assert.Equal(2, Assert.Single(loaded.Discs).Tracks.Count);
        Assert.All(loaded.Discs[0].Tracks, track => Assert.Equal(AudioDiscTrackStatus.Pending, track.Status));
        Assert.True(File.Exists(ProjectFolderLayout.GetProjectMetadataPath(folder.Path)));
        Assert.True(File.Exists(ProjectFolderLayout.GetAudioDiscManifestPath(folder.Path)));
    }

    [Fact]
    public void CreateInitialManifest_UsesMetadataForContinuousFileNamesWithoutPersistingChapterTitles()
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

        Assert.Equal(string.Empty, manifest.Title);
        Assert.Equal(string.Empty, manifest.Author);
        Assert.Equal(string.Empty, manifest.Album);
        Assert.Equal(string.Empty, manifest.Narrator);
        Assert.Equal(string.Empty, manifest.Genre);

        var tracks = manifest.Discs.Single().Tracks;
        Assert.Equal(new[] { "001_NSA.flac", "002_NSA.flac", "003_NSA.flac" }, tracks.Select(track => track.FileName));
        Assert.All(tracks, track => Assert.Equal(string.Empty, track.ChapterTitle));
        Assert.All(tracks, track => Assert.Equal(AudioDiscTrackStatus.Pending, track.Status));
        Assert.All(tracks, track => Assert.Equal(4500, track.SectorCount));
        Assert.All(tracks, track => Assert.NotEqual(Guid.Empty, track.SourceId));
        Assert.Equal(tracks.Count, tracks.Select(track => track.SourceId).Distinct().Count());
    }

    [Fact]
    public void BuildDiscEntry_ContinuesGlobalNumberingAcrossAdditionalDiscsUsingCanonicalProjectTitle()
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            folder.Path,
            CreateDisc(trackCount: 2),
            discNumber: 1,
            CreateSetup(title: "Stonehenge"),
            AudioDiscWorkingFormat.Flac);
        manifest.Title = "Veralteter Manifest-Titel";

        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Stonehenge", MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder.Path, metadata);

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
        Assert.All(tracks, track => Assert.Equal(string.Empty, track.ChapterTitle));
    }

    [Fact]
    public void CreateTrackPreview_ShowsAudioCdSourcePropertiesInsteadOfFutureWorkingFileProperties()
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            folder.Path,
            CreateDisc(trackCount: 1),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        SaveCanonicalProjectMetadata(folder.Path, "NSA", "Autor");

        var track = Assert.Single(service.CreateTrackPreview(manifest));

        Assert.Equal(44100, track.SampleRateHz);
        Assert.Equal(@"CD-Laufwerk G:\", track.RelativeFolder);
        Assert.Equal(string.Empty, track.DisplayFolder);
        Assert.Equal("Audio-CD", track.Extension);
        Assert.Equal(string.Empty, track.TagTitle);
        Assert.Equal("001 NSA", track.ChapterTitle);
        Assert.Equal(1411, track.BitrateKbps);
        Assert.Equal(2, track.Channels);
        Assert.Equal("Stereo", track.ChannelLayout);
        Assert.Equal("PCM", track.Codec);
        Assert.Equal(ProcessingActionIds.RipFlac, track.ProcessingAction);
        Assert.Equal("FLAC rippen", track.DisplayProcessingAction);
        Assert.Equal(10.1d, track.SizeMb, precision: 1);
        Assert.Equal(1, track.AutomaticOrderPosition);
        Assert.Equal(manifest.Discs.Single().Tracks.Single().SourceId, track.SourceId);
    }

    [Fact]
    public void CreateTrackPreview_FromManifest_UsesGlobalIndexAsAutomaticOrder()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 2),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        service.AddDisc(
            manifest,
            CreateDisc(trackCount: 2, identity: "disc-two"),
            discNumber: 2);

        var tracks = service.CreateTrackPreview(manifest);

        Assert.Equal(new[] { 1, 2, 3, 4 }, tracks.Select(track => track.Index));
        Assert.Equal(new int?[] { 1, 2, 3, 4 }, tracks.Select(track => track.AutomaticOrderPosition));
        Assert.Equal(4, tracks.Select(track => track.AutomaticOrderPosition).Distinct().Count());
        Assert.All(tracks, track => Assert.Equal(string.Empty, track.DisplayFolder));
    }


    [Fact]
    public void CreateTrackPreview_UsesCanonicalAuthorInsteadOfLegacyManifestAuthor()
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            folder.Path,
            CreateDisc(trackCount: 1),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        manifest.Author = "Veralteter Manifest-Autor";

        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            ["Kanonischer Autor"],
            MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder.Path, metadata);

        var track = Assert.Single(service.CreateTrackPreview(manifest));

        Assert.Equal("Kanonischer Autor", track.Artist);
    }

    [Fact]
    public void ApplyManifestMetadataToRippedTracks_DerivesProjectChapterDataAndClearsMetadataHints()
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            folder.Path,
            CreateDisc(trackCount: 2),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        SaveCanonicalProjectMetadata(folder.Path, "NSA", "Autor");
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
        Assert.Equal(manifest.Discs.Single().Tracks.Select(track => track.SourceId), tracks.Select(track => track.SourceId));
        Assert.All(tracks, track => Assert.Equal(string.Empty, track.TagTitle));
        Assert.Equal(new int?[] { 1, 2 }, tracks.Select(track => track.AutomaticOrderPosition));
        Assert.All(tracks, track => Assert.Equal(string.Empty, track.Warning));
        Assert.Equal(new int?[] { 1, 2 }, tracks.Select(track => track.TrackNumber).ToArray());
        Assert.All(tracks, track => Assert.Equal(string.Empty, track.DisplayFolder));
    }

    [Fact]
    public void ApplyManifestOrderAndMetadataToRippedTracks_RestoresGlobalDiscOrderBeforeProjection()
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            folder.Path,
            CreateDisc(trackCount: 2),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        service.AddDisc(
            manifest,
            CreateDisc(trackCount: 2, identity: "disc-two"),
            discNumber: 2);
        SaveCanonicalProjectMetadata(folder.Path, "NSA", "Autor");

        var manifestTracks = manifest.Discs
            .SelectMany(disc => disc.Tracks)
            .OrderBy(track => track.GlobalIndex)
            .ToList();
        var tracks = manifestTracks
            .OrderByDescending(track => track.GlobalIndex)
            .Select(track => new TrackInfo
            {
                FileName = track.FileName,
                FilePath = Path.Combine(manifest.ProjectFolder, track.RelativePath),
                Codec = "FLAC",
                ProcessingAction = "Konvertieren",
                AudioValidationPassed = true
            })
            .ToList();

        service.ApplyManifestOrderAndMetadataToRippedTracks(manifest, tracks);

        Assert.Equal(new[] { 1, 2, 3, 4 }, tracks.Select(track => track.Index));
        Assert.Equal(new int?[] { 1, 2, 3, 4 }, tracks.Select(track => track.AutomaticOrderPosition));
        Assert.Equal(new int?[] { 1, 1, 2, 2 }, tracks.Select(track => track.DiscNumber));
        Assert.Equal(new int?[] { 1, 2, 1, 2 }, tracks.Select(track => track.TrackNumber));
        Assert.Equal(manifestTracks.Select(track => track.SourceId), tracks.Select(track => track.SourceId));
    }

    [Fact]
    public void ApplyManifestMetadataToRippedTracks_PreservesBlockingAudioErrors()
    {
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            folder.Path,
            CreateDisc(trackCount: 1),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        SaveCanonicalProjectMetadata(folder.Path, "NSA", "Autor");
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
        using var folder = new TemporaryFolder();
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            folder.Path,
            CreateDisc(trackCount: 2, identity: "disc-one"),
            discNumber: 1,
            CreateSetup(title: "NSA"),
            AudioDiscWorkingFormat.Flac);
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "NSA", MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder.Path, metadata);

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
    public void AddDisc_DuplicateOverrideAddsNextDiscWithoutChangingExistingIdentity()
    {
        var service = new AudioDiscProjectService();
        var setup = CreateSetup(title: "NSA") with { TotalDiscs = 2 };
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(2, "disc-one"),
            discNumber: 1,
            setup,
            AudioDiscWorkingFormat.Flac);

        var added = service.AddDisc(
            manifest,
            CreateDisc(2, "disc-one"),
            discNumber: 2,
            acceptDuplicate: true);

        Assert.Equal(2, added.DiscNumber);
        Assert.Equal(
            ["disc-one", "disc-one"],
            manifest.Discs.OrderBy(disc => disc.DiscNumber).Select(disc => disc.DiscIdentity));
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
    public void UpdateSnapshot_RefreshesOperationalStateWithoutRewritingLegacyMetadataScalars()
    {
        var service = new AudioDiscProjectService();
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 2),
            discNumber: 1,
            CreateSetup(title: "Alter Titel"),
            AudioDiscWorkingFormat.Flac);
        manifest.Title = "Legacy Titel";
        manifest.Author = "Legacy Autor";
        manifest.Album = "Legacy Album";
        manifest.Narrator = "Legacy Sprecher";
        manifest.Genre = "Legacy Genre";
        manifest.Discs.Single().Tracks[0].ChapterTitle = "Legacy Kapitel 1";
        manifest.Discs.Single().Tracks[1].ChapterTitle = "Legacy Kapitel 2";
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
        Assert.Equal("Legacy Titel", manifest.Title);
        Assert.Equal("Legacy Autor", manifest.Author);
        Assert.Equal("Legacy Album", manifest.Album);
        Assert.Equal("Legacy Sprecher", manifest.Narrator);
        Assert.Equal("Legacy Genre", manifest.Genre);
        Assert.Equal(@"D:\Cover.png", manifest.CoverSourcePath);
        Assert.Equal(@"D:\Cover.jpg", manifest.ProcessedCoverPath);
        Assert.Equal(new[] { "Legacy Kapitel 1", "Legacy Kapitel 2" },
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
    public void CompleteDiscImportEarly_ShrinksExpectedSequenceAndMarksRipCompleted()
    {
        var service = new AudioDiscProjectService();
        var setup = CreateSetup(title: "NSA") with { TotalDiscs = 3 };
        var manifest = service.CreateInitialManifest(
            Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Test"),
            CreateDisc(trackCount: 1, identity: "disc-one"),
            discNumber: 1,
            setup,
            AudioDiscWorkingFormat.Flac);
        manifest.Discs[0].Tracks.Single().Status = AudioDiscTrackStatus.Ripped;
        service.MarkDiscCompleted(manifest, 1, TimeSpan.FromMinutes(2));

        service.CompleteDiscImportEarly(manifest);

        Assert.Equal(1, manifest.TotalDiscs);
        Assert.True(service.IsProjectRipCompleted(manifest));
        Assert.Equal(AudioDiscProjectStatus.RippingCompleted, manifest.Status);
        Assert.Single(manifest.Discs);
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

            Directory.CreateDirectory(Path.Combine(projectFolder, "ripped"));
            File.WriteAllBytes(Path.Combine(projectFolder, "ripped", "001_NSA.flac"), [1, 2, 3]);
            Assert.Equal(ProjectUpgradeOutcome.Upgraded, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(projectFolder).Outcome);
            var manifest = service.TryLoad(projectFolder);

            Assert.NotNull(manifest);
            Assert.Equal(AudioDiscProjectManifestVersions.Current, manifest.FormatVersion);
            Assert.Equal(AudioDiscProjectManifestVersions.CurrentSchemaVersion, manifest.SchemaVersion);
            Assert.Equal(AudioDiscProjectStatus.RippingCompleted, manifest.Status);
            Assert.Equal(AudioDiscStatus.Completed, manifest.Discs.Single().Status);
            Assert.NotNull(manifest.CompletedUtc);

            var migratedJson = File.ReadAllText(ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder));
            Assert.Contains("\"schemaVersion\": 1", migratedJson);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void TryLoad_CurrentSchemaTrackWithoutSourceId_UsesExportManifestEvidence()
    {
        var service = new AudioDiscProjectService();
        var projectFolder = Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Load-" + Guid.NewGuid());
        Directory.CreateDirectory(projectFolder);
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        try
        {
            var sourceId = Guid.NewGuid();
            var relativePath = Path.Combine(ProjectFolderLayout.OriginalsFolderName, "CD 01", "001_NSA.flac");
            var sourcePath = Path.Combine(projectFolder, relativePath);
            WriteAudioDiscManifestWithoutSourceId(projectFolder, relativePath);
            WriteExportManifestWithSourceId(
                ProjectFolderLayout.GetWorkManifestPath(projectFolder),
                sourceId,
                sourcePath);

            Assert.Equal(ProjectUpgradeOutcome.Upgraded, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(projectFolder).Outcome);
            var manifest = service.TryLoad(projectFolder);

            Assert.NotNull(manifest);
            Assert.Equal(sourceId, manifest.Discs.Single().Tracks.Single().SourceId);
            Assert.Contains(sourceId.ToString(), File.ReadAllText(ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder)), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void TryLoad_CurrentSchemaTrackWithoutSourceId_BackfillsAndPersistsOnce()
    {
        var service = new AudioDiscProjectService();
        var projectFolder = Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Load-" + Guid.NewGuid());
        Directory.CreateDirectory(projectFolder);
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        try
        {
            WriteAudioDiscManifestWithoutSourceId(
                projectFolder,
                Path.Combine(ProjectFolderLayout.OriginalsFolderName, "CD 01", "001_NSA.flac"));

            Assert.Equal(ProjectUpgradeOutcome.Upgraded, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(projectFolder).Outcome);
            var first = service.TryLoad(projectFolder);
            Assert.NotNull(first);
            var sourceId = first.Discs.Single().Tracks.Single().SourceId;
            var persistedJson = File.ReadAllText(ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder));

            var second = service.TryLoad(projectFolder);

            Assert.Equal(Guid.Empty, sourceId);
            Assert.NotNull(second);
            Assert.Equal(sourceId, second.Discs.Single().Tracks.Single().SourceId);
            Assert.Contains(sourceId.ToString(), persistedJson, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(persistedJson, File.ReadAllText(ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder)));
        }
        finally
        {
            Directory.Delete(projectFolder, recursive: true);
        }
    }

    [Fact]
    public void TryLoad_UnsupportedSchema_RejectsAndLeavesFileUnchanged()
    {
        var service = new AudioDiscProjectService();
        var projectFolder = Path.Combine(Path.GetTempPath(), "BookStitch-AudioDisc-Load-" + Guid.NewGuid());
        Directory.CreateDirectory(projectFolder);
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        try
        {
            var path = ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder);
            var json = """
            {
              "schemaVersion": 99,
              "FormatVersion": "4",
              "ProjectType": "AudioCdProject",
              "ProjectFolder": "ignored",
              "TotalDiscs": 1,
              "Discs": []
            }
            """;
            File.WriteAllText(path, json);

            Assert.Throws<PersistentJsonSchemaException>(() => service.TryLoad(projectFolder));
            Assert.Equal(json, File.ReadAllText(path));
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

    private static void SaveCanonicalProjectMetadata(string projectFolder, string title, string author)
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, title, MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            [author],
            MetadataOrigin.User);
        new ProjectMetadataStore().Save(projectFolder, metadata);
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

    private static void WriteAudioDiscManifestWithoutSourceId(string projectFolder, string relativePath)
    {
        var path = ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder);
        var json = $$"""
        {
          "schemaVersion": 1,
          "FormatVersion": "{{AudioDiscProjectManifestVersions.Current}}",
          "ProjectType": "AudioCdProject",
          "ProjectFolder": "{{JsonEncodedText.Encode(projectFolder)}}",
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
                  "RelativePath": "{{JsonEncodedText.Encode(relativePath)}}",
                  "Status": "Ripped"
                }
              ]
            }
          ]
        }
        """;
        File.WriteAllText(path, json);
    }

    private static void WriteExportManifestWithSourceId(string path, Guid sourceId, string sourcePath)
    {
        var json = $$"""
        {
          "schemaVersion": 1,
          "FormatVersion": "{{ExportWorkManifestVersions.Current}}",
          "ProjectType": "AudioCdProject",
          "ProjectId": "export-id",
          "State": {
            "Status": "ReviewBeforeMerge",
            "ManualMergeReviewCompletedPresets": []
          },
          "Tracks": [
            {
              "SourceId": "{{sourceId}}",
              "TrackIndex": 1,
              "SourcePath": "{{JsonEncodedText.Encode(sourcePath)}}",
              "SourceFileName": "001_NSA.flac",
              "Preset": "AAC Mono 64 kbps",
              "Action": "Konvertieren",
              "Status": "Pending"
            }
          ]
        }
        """;
        File.WriteAllText(path, json);
    }
}
