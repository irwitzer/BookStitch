using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Text.Json;
using Xunit;

namespace BookStitch.Tests.Services;

public class Mp3DiscProjectServiceTests
{
    private readonly Mp3DiscProjectService _service = new();

    [Fact]
    public void LoadOrCreate_WhenManifestIsMissing_CreatesNewMp3DiscManifest()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");

        var manifest = _service.LoadOrCreate(
            projectFolder,
            @"D:\",
            4,
            "AAC Stereo 192 kbps",
            "20",
            ".m4b",
            @"C:\Output",
            "{title}");

        Assert.Equal(ProjectManifestTypes.Mp3DiscProject, manifest.ProjectType);
        Assert.Equal(projectFolder, manifest.ProjectFolder);
        Assert.Equal(@"D:\", manifest.SourceFolder);
        Assert.Equal(4, manifest.TotalDiscs);
        Assert.Equal("AAC Stereo 192 kbps", manifest.ExportPreset);
        Assert.Equal("20", manifest.ParallelJobs);
        Assert.Equal(".m4b", manifest.OutputExtension);
        Assert.Equal(@"C:\Output", manifest.OutputFolder);
        Assert.Equal("{title}", manifest.FileNameTemplate);
        Assert.Empty(manifest.ImportedDiscs);
        Assert.True(manifest.CreatedUtc > DateTime.MinValue);
        Assert.True(manifest.UpdatedUtc > DateTime.MinValue);
    }

    [Fact]
    public void LoadOrCreate_AndMarkDiscCompleted_StoreDriveDiagnostics()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");
        var drive = new DiscDriveInfo(
            @"Y:\",
            "Y:",
            true,
            "Kummer aller Art",
            DiscMediaKind.Mp3Disc,
            "Internal DVD Writer",
            @"\Device\CdRom0");

        var manifest = _service.LoadOrCreate(
            projectFolder,
            @"Y:\",
            1,
            "AAC Stereo 192 kbps",
            "20",
            ".m4b",
            @"C:\Output",
            "{title}",
            drive);

        _service.MarkDiscCompleted(
            manifest,
            1,
            "signature",
            @"Y:\",
            System.IO.Path.Combine(projectFolder, "CD 01"),
            10,
            10,
            drive);

        Assert.Equal(@"Y:\", manifest.SourceDriveRoot);
        Assert.Equal("Internal DVD Writer", manifest.SourceDriveName);
        Assert.Equal(@"\Device\CdRom0", manifest.SourceDriveDevicePath);
        Assert.Equal("Kummer aller Art", manifest.SourceVolumeLabel);

        var importedDisc = Assert.Single(manifest.ImportedDiscs);
        Assert.Equal(manifest.SourceDriveName, importedDisc.SourceDriveName);
        Assert.Equal(manifest.SourceDriveDevicePath, importedDisc.SourceDriveDevicePath);
        Assert.Equal(manifest.SourceVolumeLabel, importedDisc.SourceVolumeLabel);
    }

    [Fact]
    public void Save_WritesManifestAndCanReloadImportedDisc()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");

        var manifest = _service.LoadOrCreate(
            projectFolder,
            @"D:\",
            2,
            "AAC Mono 64 kbps",
            "Auto",
            ".m4a",
            @"C:\Output",
            "{author} - {title}");

        var acquired = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 1);
        System.IO.Directory.CreateDirectory(acquired);
        for (var number = 1; number <= 12; number++)
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(acquired, $"{number:000}.mp3"), [1, 2, 3]);
        _service.MarkDiscCompleted(
            manifest,
            discNumber: 1,
            signature: "signature-cd-1",
            sourcePath: @"D:\",
            localFolder: System.IO.Path.Combine(projectFolder, "CD 01"),
            fileCount: 12,
            copiedFiles: 12);

        _service.Save(manifest);

        Assert.True(System.IO.File.Exists(Mp3DiscProjectService.GetManifestPath(projectFolder)));
        Assert.False(System.IO.File.Exists(Mp3DiscProjectService.GetManifestPath(projectFolder) + ".part"));

        var reloaded = _service.LoadOrCreate(
            projectFolder,
            @"E:\",
            2,
            "AAC Stereo 192 kbps",
            "20",
            ".m4b",
            @"C:\NewOutput",
            "{title}");

        Assert.Single(reloaded.ImportedDiscs);
        var importedDisc = reloaded.ImportedDiscs[0];
        Assert.Equal(1, importedDisc.DiscNumber);
        Assert.Equal(Mp3DiscImportStatus.Completed, importedDisc.Status);
        Assert.Equal("signature-cd-1", importedDisc.Signature);
        Assert.Equal(12, importedDisc.FileCount);
        Assert.Equal(12, importedDisc.CopiedFiles);

        Assert.Equal(@"E:\", reloaded.SourceFolder);
        Assert.Equal("AAC Stereo 192 kbps", reloaded.ExportPreset);
        Assert.Equal("20", reloaded.ParallelJobs);
        Assert.Equal(".m4b", reloaded.OutputExtension);
        Assert.Equal(@"C:\NewOutput", reloaded.OutputFolder);
        Assert.Equal("{title}", reloaded.FileNameTemplate);
    }


    [Fact]
    public void UpdateCoverAndOutputSnapshot_PreservesLegacyBookMetadata()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");

        var manifest = _service.LoadOrCreate(
            projectFolder,
            @"D:\",
            2,
            "AAC Stereo 192 kbps",
            "20",
            ".m4a",
            @"C:\Output",
            "{Autor} - {Titel}");
        manifest.Title = "Legacy Titel";
        manifest.Author = "Legacy Autor";
        manifest.Album = "Legacy Album";
        manifest.Narrator = "Legacy Sprecher";
        manifest.Genre = "Legacy Genre";

        _service.UpdateCoverAndOutputSnapshot(
            manifest,
            coverSourcePath: @"C:\Cover\source.jpg",
            processedCoverPath: @"C:\Cover\processed.jpg",
            outputFileName: "Mantel Mantu - Wölfe und andere Tiere.m4a");

        _service.Save(manifest);

        var reloaded = _service.LoadOrCreate(
            projectFolder,
            @"D:\",
            2,
            "AAC Stereo 192 kbps",
            "20",
            ".m4a",
            @"C:\Output",
            "{Autor} - {Titel}");

        Assert.Equal("Legacy Titel", reloaded.Title);
        Assert.Equal("Legacy Autor", reloaded.Author);
        Assert.Equal("Legacy Album", reloaded.Album);
        Assert.Equal("Legacy Sprecher", reloaded.Narrator);
        Assert.Equal("Legacy Genre", reloaded.Genre);
        Assert.Equal(@"C:\Cover\source.jpg", reloaded.CoverSourcePath);
        Assert.Equal(@"C:\Cover\processed.jpg", reloaded.ProcessedCoverPath);
        Assert.Equal("Mantel Mantu - Wölfe und andere Tiere.m4a", reloaded.OutputFileName);
    }

    [Fact]
    public void MarkDiscCompleted_ReplacesSameDiscNumber()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = @"C:\Work\DiscProject",
            ImportedDiscs = []
        };

        _service.MarkDiscCompleted(
            manifest,
            discNumber: 1,
            signature: "old-signature",
            sourcePath: @"D:\",
            localFolder: @"C:\Work\DiscProject\CD 01 old",
            fileCount: 5,
            copiedFiles: 5);

        _service.MarkDiscCompleted(
            manifest,
            discNumber: 1,
            signature: "new-signature",
            sourcePath: @"E:\",
            localFolder: @"C:\Work\DiscProject\CD 01",
            fileCount: 8,
            copiedFiles: 8);

        Assert.Single(manifest.ImportedDiscs);
        Assert.Equal(1, manifest.ImportedDiscs[0].DiscNumber);
        Assert.Equal("new-signature", manifest.ImportedDiscs[0].Signature);
        Assert.Equal(@"E:\", manifest.ImportedDiscs[0].SourcePath);
        Assert.Equal(8, manifest.ImportedDiscs[0].FileCount);
    }

    [Fact]
    public void MarkDiscCompleted_ReplacesSameSignatureIgnoringCase()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = @"C:\Work\DiscProject",
            ImportedDiscs = []
        };

        _service.MarkDiscCompleted(
            manifest,
            discNumber: 1,
            signature: "SAME-SIGNATURE",
            sourcePath: @"D:\",
            localFolder: @"C:\Work\DiscProject\CD 01",
            fileCount: 5,
            copiedFiles: 5);

        _service.MarkDiscCompleted(
            manifest,
            discNumber: 2,
            signature: "same-signature",
            sourcePath: @"E:\",
            localFolder: @"C:\Work\DiscProject\CD 02",
            fileCount: 6,
            copiedFiles: 6);

        Assert.Single(manifest.ImportedDiscs);
        Assert.Equal(2, manifest.ImportedDiscs[0].DiscNumber);
        Assert.Equal("same-signature", manifest.ImportedDiscs[0].Signature);
    }

    [Fact]
    public void MarkDiscCompleted_KeepsImportedDiscsSortedByDiscNumber()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectFolder = @"C:\Work\DiscProject",
            ImportedDiscs = []
        };

        _service.MarkDiscCompleted(manifest, 3, "sig-3", @"F:\", @"C:\Work\DiscProject\CD 03", 3, 3);
        _service.MarkDiscCompleted(manifest, 1, "sig-1", @"D:\", @"C:\Work\DiscProject\CD 01", 1, 1);
        _service.MarkDiscCompleted(manifest, 2, "sig-2", @"E:\", @"C:\Work\DiscProject\CD 02", 2, 2);

        Assert.Collection(
            manifest.ImportedDiscs,
            disc => Assert.Equal(1, disc.DiscNumber),
            disc => Assert.Equal(2, disc.DiscNumber),
            disc => Assert.Equal(3, disc.DiscNumber));
    }

    [Fact]
    public void LoadOrCreate_WhenManifestIsCorrupt_RejectsWithoutOverwriting()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        System.IO.File.WriteAllText(Mp3DiscProjectService.GetManifestPath(projectFolder), "{ kaputt");

        Assert.Throws<System.IO.IOException>(() => _service.LoadOrCreate(
            projectFolder,
            @"D:\",
            3,
            "AAC Stereo 128 kbps",
            "Auto",
            ".m4a",
            @"C:\Output",
            "{title}"));

        Assert.Equal("{ kaputt", System.IO.File.ReadAllText(Mp3DiscProjectService.GetManifestPath(projectFolder)));
    }

    [Fact]
    public void TryLoad_WhenManifestIsMissing_ReturnsNull()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "MissingDiscProject");

        var manifest = _service.TryLoad(projectFolder);

        Assert.Null(manifest);
    }

    [Fact]
    public void TryLoad_WhenManifestIsSaved_LoadsManifestWithoutUpdatingSettings()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");

        var manifest = _service.LoadOrCreate(
            projectFolder,
            @"D:\",
            3,
            "AAC Mono 64 kbps",
            "Auto",
            ".m4a",
            @"C:\Output",
            "{author} - {title}");

        _service.MarkDiscCompleted(manifest, 1, "sig-1", @"D:\", System.IO.Path.Combine(projectFolder, "CD 01"), 10, 10);
        _service.Save(manifest);

        var loaded = _service.TryLoad(projectFolder);

        Assert.NotNull(loaded);
        Assert.Equal(projectFolder, loaded.ProjectFolder);
        Assert.Equal(@"D:\", loaded.SourceFolder);
        Assert.Equal(3, loaded.TotalDiscs);
        Assert.Equal("AAC Mono 64 kbps", loaded.ExportPreset);
        Assert.Single(loaded.ImportedDiscs);
    }

    [Fact]
    public void CountCompletedImportedDiscs_CountsDistinctCompletedDiscNumbersOnly()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 2, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 3, Status = "Failed" }
            ]
        };

        var count = _service.CountCompletedImportedDiscs(manifest);

        Assert.Equal(2, count);
    }

    [Fact]
    public void GetMinimumDiscCount_UsesHighestCompletedDiscNumber()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 4, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 9, Status = "Failed" }
            ]
        };

        var minimum = _service.GetMinimumDiscCount(manifest);

        Assert.Equal(4, minimum);
    }

    [Fact]
    public void GetNextMissingDiscNumber_ReturnsFirstMissingCompletedDisc()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 3, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        var nextMissing = _service.GetNextMissingDiscNumber(manifest, totalDiscs: 4);

        Assert.Equal(2, nextMissing);
    }

    [Fact]
    public void GetNextMissingDiscNumber_WhenAllDiscsAreCompleted_ReturnsNull()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 2, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        var nextMissing = _service.GetNextMissingDiscNumber(manifest, totalDiscs: 2);

        Assert.Null(nextMissing);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void CompleteDiscImportEarly_KeepsCompletedExtensionDiscsAndRemovesOutstandingManifestEntries(int completed)
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 4,
            ImportedDiscs = Enumerable.Range(1, 4).Select(number => new Mp3DiscProjectManifestDisc
            { DiscNumber = number, Status = number <= completed ? Mp3DiscImportStatus.Completed : "Pending",
              Signature = "disc-" + number }).ToList()
        };
        var retained = manifest.ImportedDiscs.Take(completed).ToArray();
        _service.CompleteDiscImportEarly(manifest);
        Assert.Equal(completed, manifest.TotalDiscs);
        Assert.Null(_service.GetNextMissingDiscNumber(manifest, manifest.TotalDiscs));
        Assert.Equal(retained, manifest.ImportedDiscs);
        Assert.All(manifest.ImportedDiscs, disc => Assert.Equal(Mp3DiscImportStatus.Completed, disc.Status));
    }

    [Theory]
    [InlineData(1, 3, 2, "Importiert: 1 von 3 CDs.", "Nächste fehlende CD: CD 2.")]
    [InlineData(3, 3, null, "Importiert: 3 von 3 CDs.", "Alle eingestellten CDs sind bereits importiert.")]
    public void BuildResumeSetupMessage_ContainsImportStatusAndNextDisc(
        int importedDiscCount,
        int totalDiscs,
        int? nextMissingDisc,
        string expectedStatus,
        string expectedNext)
    {
        var message = _service.BuildResumeSetupMessage(importedDiscCount, totalDiscs, nextMissingDisc);

        Assert.Contains(expectedStatus, message);
        Assert.Contains(expectedNext, message);
        Assert.Contains("CD-Anzahl", message);
    }

    [Fact]
    public void GetMinimumTotalDiscsForAdditionalImport_IsOneMoreThanHighestCompletedDisc()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 5,
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 3, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        var minimum = _service.GetMinimumTotalDiscsForAdditionalImport(manifest);

        Assert.Equal(4, minimum);
    }

    [Fact]
    public void GetMinimumTotalDiscsForAdditionalImport_UsesHighestCompletedDiscWhenItExceedsCurrentTotal()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 2,
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 4, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        var minimum = _service.GetMinimumTotalDiscsForAdditionalImport(manifest);

        Assert.Equal(5, minimum);
    }

    [Fact]
    public void IncreaseTotalDiscsForAdditionalImport_UpdatesTotalDiscsWhenAboveImportedCount()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 3,
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 2, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 3, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        _service.IncreaseTotalDiscsForAdditionalImport(manifest, 5);

        Assert.Equal(5, manifest.TotalDiscs);
        Assert.True(manifest.UpdatedUtc > DateTime.MinValue);
    }

    [Fact]
    public void IncreaseTotalDiscsForAdditionalImport_RejectsExistingImportedDiscCount()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 3,
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 2, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 3, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.IncreaseTotalDiscsForAdditionalImport(manifest, 3));
    }

    [Fact]
    public void IncreaseTotalDiscsForAdditionalImport_CanReduceOldPlannedTotalToNextAfterCompletedDisc()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 5,
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        _service.IncreaseTotalDiscsForAdditionalImport(manifest, 2);

        Assert.Equal(2, manifest.TotalDiscs);
    }


    [Fact]
    public void BuildAdditionalImportPlan_UsesCompletedDiscsAsDialogBasis()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 4,
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 2, Status = "Canceled" }
            ]
        };

        var plan = _service.BuildAdditionalImportPlan(manifest);

        Assert.Equal(1, plan.CompletedDiscCount);
        Assert.Equal(4, plan.CurrentTotalDiscs);
        Assert.Equal(2, plan.MinimumTotalDiscs);
        Assert.Equal(2, plan.DefaultTotalDiscs);
        Assert.Equal(99, plan.MaximumTotalDiscs);
    }

    [Fact]
    public void BuildAdditionalImportPlan_ClampsMaximumTotalDiscs()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 150,
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        var plan = _service.BuildAdditionalImportPlan(manifest, maximumTotalDiscs: 120);

        Assert.Equal(99, plan.CurrentTotalDiscs);
        Assert.Equal(99, plan.MaximumTotalDiscs);
    }

    [Fact]
    public void BuildResumePlan_UsesHighestCompletedDiscAsMinimumAndFindsFirstGap()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 2,
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 3, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 4, Status = "Canceled" }
            ]
        };

        var plan = _service.BuildResumePlan(manifest);

        Assert.Equal(2, plan.CompletedDiscCount);
        Assert.Equal(3, plan.MinimumTotalDiscs);
        Assert.Equal(3, plan.CurrentTotalDiscs);
        Assert.Equal(2, plan.NextMissingDiscNumber);
        Assert.Contains("Importiert: 2 von 3 CDs.", plan.SetupMessage);
        Assert.Contains("Nächste fehlende CD: CD 2.", plan.SetupMessage);
    }

    [Fact]
    public void BuildResumePlan_WhenAllConfiguredDiscsAreCompleted_HasNoMissingDisc()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 2,
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 2, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        var plan = _service.BuildResumePlan(manifest);

        Assert.Null(plan.NextMissingDiscNumber);
        Assert.Contains("Alle eingestellten CDs sind bereits importiert.", plan.SetupMessage);
    }

    [Fact]
    public void UpdateResumeDiscPlan_UpdatesValidatedTotalAndTrimmedSource()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 2,
            SourceFolder = "old",
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        _service.UpdateResumeDiscPlan(manifest, 4, "  D:\\  ");

        Assert.Equal(4, manifest.TotalDiscs);
        Assert.Equal("D:\\", manifest.SourceFolder);
        Assert.True(manifest.UpdatedUtc > DateTime.MinValue);
    }

    [Fact]
    public void UpdateResumeDiscPlan_RejectsTotalBelowHighestCompletedDisc()
    {
        var manifest = new Mp3DiscProjectManifest
        {
            TotalDiscs = 4,
            ImportedDiscs =
            [
                new() { DiscNumber = 1, Status = Mp3DiscImportStatus.Completed },
                new() { DiscNumber = 4, Status = Mp3DiscImportStatus.Completed }
            ]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.UpdateResumeDiscPlan(manifest, 3, @"D:\"));
    }

    [Fact]
    public void TryLoad_LegacyManifestWithoutFormatVersion_NormalizesMissingValues()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);

        var legacyJson = """
        {
          "ProjectType": "Mp3Disc",
          "ProjectFolder": "old-path",
          "SourceFolder": null,
          "TotalDiscs": 2,
          "ExportPreset": null,
          "ImportedDiscs": [
            {
              "DiscNumber": 1,
              "Status": null,
              "Signature": null,
              "SourcePath": null,
              "LocalFolder": null,
              "FileCount": 4,
              "CopiedFiles": 4
            }
          ],
          "UnknownFutureField": "ignored"
        }
        """;

        System.IO.File.WriteAllText(Mp3DiscProjectService.GetManifestPath(projectFolder), legacyJson);

        Assert.Equal(ProjectUpgradeOutcome.Upgraded, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(projectFolder).Outcome);
        var manifest = _service.TryLoad(projectFolder);

        Assert.NotNull(manifest);
        Assert.Equal(Mp3DiscManifestVersions.Current, manifest.FormatVersion);
        Assert.Equal(Mp3DiscManifestVersions.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal(projectFolder, manifest.ProjectFolder);
        Assert.Equal(string.Empty, manifest.SourceFolder);
        Assert.Equal(string.Empty, manifest.ExportPreset);

        var disc = Assert.Single(manifest.ImportedDiscs);
        Assert.Equal("Incomplete", disc.Status);
        Assert.Equal(ProjectPipelineStateNames.AcquiringSources, manifest.PipelineState);
        Assert.Equal(string.Empty, disc.Signature);
        Assert.Equal(string.Empty, disc.SourcePath);
        Assert.Equal(ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 1), disc.LocalFolder);

        var migratedJson = System.IO.File.ReadAllText(Mp3DiscProjectService.GetManifestPath(projectFolder));
        Assert.Contains("\"schemaVersion\": 1", migratedJson);
    }

    [Fact]
    public void TryLoad_CurrentSchemaDiscWithoutSourceFiles_BackfillsFromSourceCopyIdentity()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var sourceFile = WriteFile(folder, System.IO.Path.Combine("external", "Track 01.mp3"), "audio");
        var targetFile = System.IO.Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 1), "Track 01.mp3");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetFile)!);
        System.IO.File.WriteAllText(targetFile, "audio");
        var lastWriteUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        System.IO.File.SetLastWriteTimeUtc(sourceFile, lastWriteUtc);
        var identity = new SourceCopyIdentityStore().RecordCopiedFile(projectFolder, sourceFile, targetFile);
        WriteMp3DiscManifestWithoutSourceFiles(projectFolder);

        var first = _service.TryLoad(projectFolder);
        Assert.NotNull(first);
        var sourceFileEntry = Assert.Single(first.ImportedDiscs.Single().SourceFiles);
        var persistedJson = System.IO.File.ReadAllText(Mp3DiscProjectService.GetManifestPath(projectFolder));
        var second = _service.TryLoad(projectFolder);

        Assert.Equal(identity.SourceId, sourceFileEntry.SourceId);
        Assert.Equal(System.IO.Path.Combine("CD 01", "Track 01.mp3"), sourceFileEntry.RelativePath);
        Assert.NotNull(second);
        Assert.Equal(identity.SourceId, Assert.Single(second.ImportedDiscs.Single().SourceFiles).SourceId);
        Assert.Equal(persistedJson, System.IO.File.ReadAllText(Mp3DiscProjectService.GetManifestPath(projectFolder)));
    }

    [Fact]
    public void TryLoad_CurrentSchemaSourceFileWithoutSourceId_UsesExportManifestEvidence()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var sourceId = Guid.NewGuid();
        var sourcePath = System.IO.Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 1), "Track 01.mp3");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sourcePath)!);
        System.IO.File.WriteAllText(sourcePath, "audio");
        WriteMp3DiscManifestWithSourceFile(projectFolder, sourceId: null);
        WriteExportManifestWithSourceId(ProjectFolderLayout.GetExportManifestPath(projectFolder), sourceId, sourcePath);

        var manifest = _service.TryLoad(projectFolder);

        Assert.NotNull(manifest);
        Assert.Equal(sourceId, Assert.Single(manifest.ImportedDiscs.Single().SourceFiles).SourceId);
    }

    [Fact]
    public void TryLoad_CurrentSchemaDiscWithoutEvidence_BackfillsFromLocalFilesAndPersistsOnce()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var sourcePath = System.IO.Path.Combine(ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 1), "Track 01.mp3");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sourcePath)!);
        System.IO.File.WriteAllText(sourcePath, "audio");
        WriteMp3DiscManifestWithoutSourceFiles(projectFolder);

        Assert.Equal(ProjectUpgradeOutcome.Upgraded, new ProjectUpgradeCoordinator().EnsureCurrentProjectFormat(projectFolder).Outcome);
        var first = _service.TryLoad(projectFolder);
        Assert.NotNull(first);
        var sourceId = Assert.Single(first.ImportedDiscs.Single().SourceFiles).SourceId;
        var persistedJson = System.IO.File.ReadAllText(Mp3DiscProjectService.GetManifestPath(projectFolder));

        var second = _service.TryLoad(projectFolder);

        Assert.Equal(Guid.Empty, sourceId);
        Assert.NotNull(second);
        Assert.Equal(sourceId, Assert.Single(second.ImportedDiscs.Single().SourceFiles).SourceId);
        Assert.Equal(persistedJson, System.IO.File.ReadAllText(Mp3DiscProjectService.GetManifestPath(projectFolder)));
    }

    [Fact]
    public void Save_LegacyManifest_WritesCurrentFormatVersion()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");
        var manifest = new Mp3DiscProjectManifest
        {
            FormatVersion = "",
            ProjectFolder = projectFolder,
            ImportedDiscs = []
        };

        _service.Save(manifest);

        var json = System.IO.File.ReadAllText(Mp3DiscProjectService.GetManifestPath(projectFolder));
        Assert.Contains($"\"FormatVersion\": \"{Mp3DiscManifestVersions.Current}\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
    }

    [Fact]
    public void TryLoad_UnsupportedSchema_RejectsAndLeavesFileUnchanged()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = System.IO.Path.Combine(folder.Path, "DiscProject");
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var manifestPath = Mp3DiscProjectService.GetManifestPath(projectFolder);
        var json = """
        {
          "schemaVersion": 99,
          "FormatVersion": "2",
          "ProjectType": "Mp3Disc",
          "ProjectFolder": "old-path",
          "TotalDiscs": 1,
          "ImportedDiscs": []
        }
        """;
        System.IO.File.WriteAllText(manifestPath, json);

        Assert.Throws<PersistentJsonSchemaException>(() => _service.TryLoad(projectFolder));
        Assert.Equal(json, System.IO.File.ReadAllText(manifestPath));
    }

    private static string WriteFile(TemporaryFolder folder, string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(folder.Path, relativePath);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        System.IO.File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private static void WriteMp3DiscManifestWithoutSourceFiles(string projectFolder)
    {
        var json = $$"""
        {
          "schemaVersion": 1,
          "FormatVersion": "{{Mp3DiscManifestVersions.Current}}",
          "ProjectType": "Mp3Disc",
          "ProjectFolder": "{{JsonEncodedText.Encode(projectFolder)}}",
          "TotalDiscs": 1,
          "ImportedDiscs": [
            {
              "DiscNumber": 1,
              "Status": "Completed",
              "LocalFolder": "{{JsonEncodedText.Encode(ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 1))}}",
              "FileCount": 1,
              "CopiedFiles": 1
            }
          ]
        }
        """;
        System.IO.File.WriteAllText(Mp3DiscProjectService.GetManifestPath(projectFolder), json);
    }

    private static void WriteMp3DiscManifestWithSourceFile(string projectFolder, Guid? sourceId)
    {
        var sourceIdLine = sourceId.HasValue
            ? $"""
                              "SourceId": "{sourceId.Value}",
              """
            : string.Empty;
        var json = $$"""
        {
          "schemaVersion": 1,
          "FormatVersion": "{{Mp3DiscManifestVersions.Current}}",
          "ProjectType": "Mp3Disc",
          "ProjectFolder": "{{JsonEncodedText.Encode(projectFolder)}}",
          "TotalDiscs": 1,
          "ImportedDiscs": [
            {
              "DiscNumber": 1,
              "Status": "Completed",
              "LocalFolder": "{{JsonEncodedText.Encode(ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, 1))}}",
              "FileCount": 1,
              "CopiedFiles": 1,
              "SourceFiles": [
                {
        {{sourceIdLine}}          "RelativePath": "CD 01\\Track 01.mp3",
                  "SourceFileName": "Track 01.mp3"
                }
              ]
            }
          ]
        }
        """;
        System.IO.File.WriteAllText(Mp3DiscProjectService.GetManifestPath(projectFolder), json);
    }

    private static void WriteExportManifestWithSourceId(string path, Guid sourceId, string sourcePath)
    {
        var json = $$"""
        {
          "schemaVersion": 1,
          "FormatVersion": "{{ExportWorkManifestVersions.Current}}",
          "ProjectType": "Mp3DiscProject",
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
              "SourceFileName": "Track 01.mp3",
              "Preset": "AAC Stereo 192 kbps",
              "Action": "Konvertieren",
              "Status": "Pending"
            }
          ]
        }
        """;
        System.IO.File.WriteAllText(path, json);
    }
}
