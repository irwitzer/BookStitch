using System.IO;
using Xunit;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;

namespace BookStitch.Tests.Services;

public sealed class FolderScannerTests
{
    [Fact]
    public void Two_disc_preview_keeps_the_complete_chapter_number_family_with_a_bad_source_tag()
    {
        using var folder = new TemporaryFolder();
        var metadata = new Dictionary<string, OriginalTagMetadata>();
        for (var number = 1; number <= 338; number++)
        {
            var disc = number <= 169 ? 1 : 2;
            var name = $"{number:000}_Jensen_Oxen_Gefrorene_Flammen_CD{disc:00}.mp3";
            folder.CreateFile($"CD {disc:00}/{name}");
            metadata[name] = new OriginalTagMetadata
            {
                TrackNumber = (uint)(number == 213 ? 123 : number), DiscNumber = (uint)disc,
                Title = number is >= 162 and <= 169 ? Path.GetFileNameWithoutExtension(name) : ""
            };
        }
        var scanner = new FolderScanner(new PerFileTagMetadataReader(metadata), new FileSystemMetadataReader());
        var tracks = scanner.Scan(folder.Path, TrackNumberPreference.FileName);
        foreach (var track in tracks) track.SourceId = Guid.NewGuid();
        var manifest = new Mp3DiscProjectManifest
        {
            ProjectId = "two-disc-regression", ProjectFolder = folder.Path, TotalDiscs = 2,
            ImportedDiscs = [new Mp3DiscProjectManifestDisc
            {
                DiscNumber = 1, Signature = "disc-one", SourceFiles = tracks.Take(169).Select(t => new Mp3DiscProjectManifestSourceFile
                { SourceId = t.SourceId, SourceFileName = t.FileName, RelativePath = t.FileName }).ToList()
            }]
        };
        var pending = new Mp3DiscPendingChapterDisc(2, "disc-two", tracks.Skip(169).Select(t =>
            new Mp3DiscPlannedSourceIdentity(t.SourceId, t.FileName, t.FileName, 0, 0)).ToArray());
        var reconciliation = new Mp3DiscTrackReconciliationService();
        var rows = tracks.Take(169).ToList();
        reconciliation.AppendMissingPreviewTracks(rows, tracks.Skip(169), Path.Combine(folder.Path, "CD 02"), 2, false);
        for (var continuation = 0; continuation < 3; continuation++)
        {
            reconciliation.AppendMissingPreviewTracks(rows,
                reconciliation.CreatePreviewTracksFromKnownFiles(Path.Combine(folder.Path, "CD 02"), tracks.Skip(169).Select(t => t.FilePath)),
                Path.Combine(folder.Path, "CD 02"), 2, false);
            var result = new ChapterAutoEngineService().Apply(new ChapterAutoEngineRequest(
                rows, ProjectManifestTypes.Mp3DiscProject, manifest.ProjectId, folder.Path,
                new ChapterCanonicalMetadataSnapshot(0, "Oxen. Gefrorene Flammen", MetadataOrigin.User, null, MetadataOrigin.None),
                new ChapterSettings { Mode = "Auto", NumberingStyle = "SpacePadded" }, false,
                Mp3DiscManifest: manifest, PendingMp3Discs: [pending]));
            Assert.Equal(338, rows.Count);
            Assert.Equal(Enumerable.Range(1, 338).Select(n => (int?)n), rows.Select(t => t.TrackNumber));
            Assert.Equal("170 Oxen. Gefrorene Flammen", rows[169].ChapterTitle);
            Assert.Equal("213 Oxen. Gefrorene Flammen", rows[212].ChapterTitle);
            Assert.Equal(338, result.RenderedTimeline.Count);
        }
    }

    [Fact]
    public void Disc_filename_order_is_consistent_with_displayed_numbers_despite_a_conflicting_tag()
    {
        using var folder = new TemporaryFolder();
        var numbers = new[] { 170, 171, 212, 213, 214 };
        var metadata = new Dictionary<string, OriginalTagMetadata>();
        foreach (var number in numbers)
        {
            var name = $"{number}_Oxen_CD02.mp3";
            folder.CreateFile(name);
            metadata[name] = new OriginalTagMetadata { TrackNumber = (uint)(number == 213 ? 123 : number), DiscNumber = 2 };
        }
        var scanner = new FolderScanner(new PerFileTagMetadataReader(metadata), new FileSystemMetadataReader());
        var imported = scanner.Scan(folder.Path, TrackNumberPreference.FileName);
        Assert.Equal(numbers.Select(n => (int?)n), imported.Select(t => t.TrackNumber));
        Assert.Equal(numbers.Select(n => $"{n}_Oxen_CD02.mp3"), imported.Select(t => t.FileName));
        Assert.Equal(Enumerable.Range(1, numbers.Length).Select(n => (int?)n), imported.Select(t => t.AutomaticOrderPosition));
        Assert.Equal(123, imported.Single(t => t.TrackNumber == 213).SourceTrackNumber);
        // Normal folder import still uses its existing metadata/evidence policy.
        Assert.Equal("213_Oxen_CD02.mp3", scanner.Scan(folder.Path)[0].FileName);
    }

    [Fact]
    public void Scan_reads_source_sample_rate_and_bitrate_from_the_existing_tag_read()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("source.mp3");
        var reader = new TechnicalTagMetadataReader();
        var track = Assert.Single(new FolderScanner(reader, new FileSystemMetadataReader()).Scan(folder.Path));
        Assert.Equal(1, reader.ReadCalls);
        Assert.Equal(256, track.BitrateKbps);
        Assert.Equal(44100, track.SampleRateHz);
    }

    private sealed class TechnicalTagMetadataReader : TagMetadataReader
    {
        public int ReadCalls { get; private set; }
        public override TagMetadataReadResult Read(string filePath)
        {
            ReadCalls++;
            return new(new OriginalTagMetadata(), TimeSpan.FromMinutes(1), 256, 2, false, false)
            { TechnicalSampleRateHzFallback = 44100 };
        }
    }

    [Fact]
    public void Scan_RetainsReadableSiblingsWhenAnotherDirectoryDeniesEnumeration()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile(Path.Combine("readable", "001.mp3"));
        folder.CreateFile(Path.Combine("blocked", "002.mp3"));
        var blocked = new DirectoryInfo(Path.Combine(folder.Path, "blocked"));
        var original = System.IO.FileSystemAclExtensions.GetAccessControl(blocked);
        var restricted = System.IO.FileSystemAclExtensions.GetAccessControl(blocked);
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        restricted.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            identity.User!, System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny));
        try
        {
            System.IO.FileSystemAclExtensions.SetAccessControl(blocked, restricted);
            Assert.Equal("001.mp3", Assert.Single(new FolderScanner().Scan(folder.Path)).FileName);
            Assert.Single(new Mp3DiscImportService().AnalyzeSource(folder.Path).SupportedAudioFiles);
        }
        finally { System.IO.FileSystemAclExtensions.SetAccessControl(blocked, original); }
    }

    [Fact]
    public async Task ScanAsync_CanBeCanceledDuringEnumerationWithoutPublishingPartialTracks()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001.mp3");
        folder.CreateFile("002.mp3");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FolderScanner().ScanAsync(
            folder.Path, includeFile: _ => { cancellation.Cancel(); return true; },
            cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Scan_SkipsAJunctionBackToItsParentAndRetainsReadableTracks()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001.mp3");
        var junction = Path.Combine(folder.Path, "loop");
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", junction, folder.Path })
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);
        try
        {
            Assert.True((File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0);
            var tracks = new FolderScanner().Scan(folder.Path);
            Assert.Equal("001.mp3", Assert.Single(tracks).FileName);
            Assert.Single(new Mp3DiscImportService().AnalyzeSource(folder.Path).SupportedAudioFiles);
        }
        finally { Directory.Delete(junction); }
    }

    [Fact]
    public void Scan_Orders_Files_By_Disc_Track_And_Natural_File_Name()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile(@"CD 10\001 Final.mp3");
        folder.CreateFile(@"CD 2\010 Ten.mp3");
        folder.CreateFile(@"CD 2\002 Two.mp3");
        folder.CreateFile(@"CD 1\009 Nine.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.Equal(
            [
                "009 Nine.mp3",
                "002 Two.mp3",
                "010 Ten.mp3",
                "001 Final.mp3"
            ],
            tracks.Select(track => track.FileName).ToArray());

        Assert.Equal([1, 2, 3, 4], tracks.Select(track => track.Index).ToArray());
    }

    [Theory]
    [InlineData("Track 001 Intro.mp3", 1)]
    [InlineData("Track01 Intro.mp3", 1)]
    [InlineData("Kapitel 002 Anfang.mp3", 2)]
    [InlineData("Chapter 003 Middle.mp3", 3)]
    [InlineData("Titel 004 Ende.mp3", 4)]
    [InlineData("005 Outro.mp3", 5)]
    public void Scan_Recognizes_Common_Track_Number_PatternsWithoutPrecomputingAutoChapter(
        string fileName,
        int expectedTrackNumber)
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile(fileName);

        var track = Assert.Single(new FolderScanner().Scan(folder.Path));

        Assert.Equal(expectedTrackNumber, track.TrackNumber);
        Assert.Empty(track.ChapterTitle);
    }

    [Theory]
    [InlineData("Kapitola 3 Úvod.mp3", 3)]
    [InlineData("Rozdział 4 Początek.mp3", 4)]
    [InlineData("Глава 5 Начало.mp3", 5)]
    [InlineData("Κεφάλαιο 6 Αρχή.mp3", 6)]
    [InlineData("第7章标题.mp3", 7)]
    [InlineData("第8話タイトル.mp3", 8)]
    public void Scan_RecognizesInternationalNamedTrackNumbers(string fileName, int expectedTrackNumber)
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile(fileName);

        var track = Assert.Single(new FolderScanner().Scan(folder.Path));

        Assert.Equal(expectedTrackNumber, track.TrackNumber);
    }

    [Theory]
    [InlineData("Część 2", 2)]
    [InlineData("Часть 3", 3)]
    [InlineData("Μέρος 4", 4)]
    [InlineData("Deel 5", 5)]
    public void Scan_RecognizesInternationalDiscFolders(string discFolder, int expectedDiscNumber)
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile(Path.Combine(discFolder, "001 Intro.mp3"));

        var track = Assert.Single(new FolderScanner().Scan(folder.Path));

        Assert.Equal(expectedDiscNumber, track.DiscNumber);
        Assert.Equal(1, track.TrackNumber);
    }

    [Fact]
    public void Scan_Does_Not_Warn_For_Distinct_Number_Suffixes()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("003A Teil A.mp3");
        folder.CreateFile("003B Teil B.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.All(tracks, track => Assert.DoesNotContain("Doppelte Tracknummer", track.Warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_DoesNotPrecomputeChapterTitleFromRepeatedTechnicalNumbers()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001_01_-_Die_Flüsse_von_London.flac");

        var track = Assert.Single(new FolderScanner().Scan(folder.Path));

        Assert.Empty(track.ChapterTitle);
    }

    [Fact]
    public void Scan_LargeRepetitiveFolder_DoesNotThrowOrDropTracks()
    {
        using var folder = new TemporaryFolder();
        for (var index = 0; index < 1300; index++)
        {
            var group = (index % 2) + 1;
            var copy = (index % 2) + 1;
            folder.CreateFile(Path.Combine(
                $"Copy {index:0000}",
                $"{group:000}_{copy:000}_Stephen_King_1000.mp3"));
        }

        var tagReader = new EmptyTagMetadataReader();
        var tracks = new FolderScanner(tagReader, new FileSystemMetadataReader()).Scan(folder.Path);

        Assert.Equal(1300, tracks.Count);
        Assert.Equal(1300, tagReader.ReadCalls);
        Assert.Equal(1300, tracks.Select(track => track.RelativeFolder).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Enumerable.Range(1, 1300), tracks.Select(track => track.Index));
        Assert.All(tracks, track => Assert.True(track.TrackNumber is 1 or 2));
        Assert.All(tracks, track => Assert.True(track.AutomaticOrderPosition is >= 1 and <= 1300));
    }

    [Fact]
    public void Scan_FillsProjectRelativeFolderForRootAndNestedFiles()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001 Root.mp3");
        folder.CreateFile(@"CD 01\002 One.mp3");
        folder.CreateFile(@"Teil 1\CD 01\003 Nested.mp3");
        var rootName = Path.GetFileName(folder.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var tracks = new FolderScanner().Scan(folder.Path)
            .OrderBy(track => track.FileName)
            .ToList();

        Assert.Equal(rootName, tracks[0].ProjectRelativeFolder);
        Assert.Equal(Path.Combine(rootName, "CD 01"), tracks[1].ProjectRelativeFolder);
        Assert.Equal(Path.Combine(rootName, "Teil 1", "CD 01"), tracks[2].ProjectRelativeFolder);
        Assert.All(tracks, track => Assert.DoesNotContain(Path.GetPathRoot(folder.Path)!, track.ProjectRelativeFolder));
        Assert.Equal(tracks.Select(track => track.ProjectRelativeFolder), tracks.Select(track => track.DisplayFolder));
    }

    [Fact]
    public void Scan_Warns_For_Real_Duplicate_Track_Numbers()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("003 Teil Eins.mp3");
        folder.CreateFile("003 Teil Zwei.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.All(tracks, track => Assert.Contains("Doppelte Tracknummer", track.Warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_Warns_For_Missing_Track_Numbers_When_Sequence_Is_Almost_Complete()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001 Eins.mp3");
        folder.CreateFile("002 Zwei.mp3");
        folder.CreateFile("004 Vier.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.Contains(tracks, track => track.Warning.Contains("Tracknummer 003 fehlt möglicherweise", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_Does_Not_Warn_For_Numbered_Duplicate_Chapter_Title_Content()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001 Gleicher Titel.mp3");
        folder.CreateFile("002 Gleicher Titel.mp3");
        folder.CreateFile("003 Gleicher Titel.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.All(tracks, track => Assert.DoesNotContain("Doppelter Kapitelname", track.Warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_DefersDuplicateAutoChapterWarningsUntilV4Resolve()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile(Path.Combine("A", "Gleicher Titel.mp3"));
        folder.CreateFile(Path.Combine("B", "Gleicher Titel.mp3"));

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.All(tracks, track => Assert.DoesNotContain("Doppelter Kapitelname", track.Warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_Includes_Currently_Supported_Input_Formats()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001 Eins.mp3");
        folder.CreateFile("002 Zwei.m4a");
        folder.CreateFile("003 Drei.m4b");
        folder.CreateFile("004 Vier.aac");
        folder.CreateFile("005 Fuenf.wav");
        folder.CreateFile("006 Sechs.flac");
        folder.CreateFile("007 Sieben.wma");
        folder.CreateFile("008 Acht.ogg");
        folder.CreateFile("009 Neun.oga");
        folder.CreateFile("010 Zehn.opus");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.Equal(10, tracks.Count);
        Assert.Equal(["MP3", "M4A", "M4B", "AAC", "WAV", "FLAC", "WMA", "OGG", "OGA", "OPUS"], tracks.Select(track => track.Extension).ToArray());
    }
    [Fact]
    public void Scan_Can_Prefer_File_Name_Track_Numbers_For_Imported_Disc_Projects()
    {
        using var folder = new TemporaryFolder();
        CreateMp3WithId3v1Track(folder, "001 Eins.mp3", 1);
        CreateMp3WithId3v1Track(folder, "002 Zwei.mp3", 1);
        CreateMp3WithId3v1Track(folder, "003 Drei.mp3", 1);

        var fileNamePreferredTracks = new FolderScanner().Scan(folder.Path, TrackNumberPreference.FileName);

        Assert.Equal([1, 2, 3], fileNamePreferredTracks.Select(track => track.TrackNumber).ToArray());
        Assert.All(fileNamePreferredTracks, track =>
            Assert.DoesNotContain("Doppelte Tracknummer", track.Warning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_Applies_File_Filter_Before_Duplicate_Warnings()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile(@"CD 01\001 Original.mp3");
        folder.CreateFile(@"converted\AAC Stereo 128 kbps\001 Original.m4a");

        var tracks = new FolderScanner().Scan(
            folder.Path,
            TrackNumberPreference.FileName,
            path => !path.Contains($"{Path.DirectorySeparatorChar}converted{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var track = Assert.Single(tracks);
        Assert.Equal("001 Original.mp3", track.FileName);
        Assert.DoesNotContain("Doppelte Tracknummer", track.Warning, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Scan_Uses_File_Name_Track_Numbers_When_Tags_Do_Not_Provide_Track_Numbers()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("111_Eckhart_Omama.mp3");
        folder.CreateFile("020_Eckhart_Omama.mp3");
        folder.CreateFile("003_Eckhart_Omama.mp3");

        var tracks = new FolderScanner().Scan(folder.Path);

        Assert.Equal(["003_Eckhart_Omama.mp3", "020_Eckhart_Omama.mp3", "111_Eckhart_Omama.mp3"],
            tracks.Select(track => track.FileName).ToArray());
        Assert.Equal([3, 20, 111], tracks.Select(track => track.TrackNumber).ToArray());
    }


    [Fact]
    public void Scan_RetainsCanonicalSourceMetadataListsFromTheSingleTagRead()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001 One.mp3");
        folder.CreateFile("002 Two.mp3");
        var reader = new PerFileTagMetadataReader(new Dictionary<string, OriginalTagMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["001 One.mp3"] = new OriginalTagMetadata
            {
                Authors = ["A1", "A2"],
                Narrators = ["N1"],
                Genres = ["G1", "G2"],
                Keywords = ["K1", "K2"]
            },
            ["002 Two.mp3"] = new OriginalTagMetadata
            {
                Authors = ["A3"],
                Narrators = ["N2", "N3"],
                Genres = ["G3"],
                Keywords = ["K3"]
            }
        });

        var tracks = new FolderScanner(reader, new FileSystemMetadataReader()).Scan(folder.Path);

        Assert.Equal(["G1", "G2"], tracks[0].SourceMetadataFields!.Genres);
        Assert.Equal(["G3"], tracks[1].SourceMetadataFields!.Genres);
        Assert.Equal(["A1", "A2"], tracks[0].SourceMetadataFields!.Authors);
        Assert.Equal(["N2", "N3"], tracks[1].SourceMetadataFields!.Narrators);
        Assert.Equal(["K1", "K2"], tracks[0].SourceMetadataFields!.Keywords);
        Assert.Equal(2, reader.ReadPaths.Count);
    }

    [Fact]
    public void Scan_DoesNotWarnWhenWholeAnalysisGroupHasNoTagTitles()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001 One.mp3");
        folder.CreateFile("002 Two.mp3");

        var tracks = new FolderScanner(new EmptyTagMetadataReader(), new FileSystemMetadataReader()).Scan(folder.Path);

        Assert.All(tracks, track => Assert.False(track.WarningCodes.HasFlag(TrackWarningCodes.MissingTagTitle)));
    }

    [Fact]
    public void Scan_DoesNotWarnForMissingTitlesInMixedAnalysisGroup()
    {
        using var folder = new TemporaryFolder();
        folder.CreateFile("001 One.mp3");
        folder.CreateFile("002 Two.mp3");
        var reader = new PerFileTagMetadataReader(new Dictionary<string, OriginalTagMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["001 One.mp3"] = new OriginalTagMetadata { Title = "Tagged" },
            ["002 Two.mp3"] = new OriginalTagMetadata()
        });

        var tracks = new FolderScanner(reader, new FileSystemMetadataReader()).Scan(folder.Path);

        Assert.False(tracks[0].WarningCodes.HasFlag(TrackWarningCodes.MissingTagTitle));
        Assert.False(tracks[1].WarningCodes.HasFlag(TrackWarningCodes.MissingTagTitle));
    }

    [Fact]
    public void WarningRefresh_RemovesLegacyMissingTitleCodeAndPreservesUnreadableTags()
    {
        var titled = new TrackInfo { TagTitle = "Tagged", DiscNumber = 1 };
        var missing = new TrackInfo { DiscNumber = 1 };
        missing.SetWarning(
            $"{TrackWarningPresentation.Format(TrackWarningCodes.UnreadableTags)}; {TrackWarningPresentation.Format(TrackWarningCodes.MissingTagTitle)}",
            TrackWarningCodes.UnreadableTags | TrackWarningCodes.MissingTagTitle);

        new TrackListWarningService().Apply([titled, missing]);
        Assert.False(missing.WarningCodes.HasFlag(TrackWarningCodes.MissingTagTitle));
        Assert.Equal(TrackWarningPresentation.Format(TrackWarningCodes.UnreadableTags), missing.DisplayChapterWarning);

        titled.TagTitle = string.Empty;
        new TrackListWarningService().Apply([titled, missing]);

        Assert.False(missing.WarningCodes.HasFlag(TrackWarningCodes.MissingTagTitle));
        Assert.True(missing.WarningCodes.HasFlag(TrackWarningCodes.UnreadableTags));
    }

    private static void CreateMp3WithId3v1Track(TemporaryFolder folder, string fileName, byte trackNumber)
    {
        var path = folder.CreateFile(fileName);
        var tag = new byte[128];
        tag[0] = (byte)'T';
        tag[1] = (byte)'A';
        tag[2] = (byte)'G';
        tag[125] = 0;
        tag[126] = trackNumber;
        File.WriteAllBytes(path, tag);
    }

    private sealed class PerFileTagMetadataReader : TagMetadataReader
    {
        private readonly IReadOnlyDictionary<string, OriginalTagMetadata> _metadataByFileName;

        public PerFileTagMetadataReader(IReadOnlyDictionary<string, OriginalTagMetadata> metadataByFileName)
        {
            _metadataByFileName = metadataByFileName;
        }

        public List<string> ReadPaths { get; } = [];

        public override TagMetadataReadResult Read(string filePath)
        {
            ReadPaths.Add(filePath);
            _metadataByFileName.TryGetValue(Path.GetFileName(filePath), out var metadata);
            return new TagMetadataReadResult(
                metadata ?? new OriginalTagMetadata(),
                TechnicalDurationFallback: null,
                TechnicalBitrateKbpsFallback: null,
                TechnicalChannelsFallback: null,
                HasEmbeddedCover: false,
                Failed: false);
        }
    }

    private sealed class EmptyTagMetadataReader : TagMetadataReader
    {
        public int ReadCalls { get; private set; }

        public override TagMetadataReadResult Read(string filePath)
        {
            ReadCalls++;
            return new TagMetadataReadResult(
                new OriginalTagMetadata(),
                TechnicalDurationFallback: null,
                TechnicalBitrateKbpsFallback: null,
                TechnicalChannelsFallback: null,
                HasEmbeddedCover: false,
                Failed: false);
        }
    }

}
