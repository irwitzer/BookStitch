using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit;
using AppleTag = TagLib.Mpeg4.AppleTag;
using TagLibFile = TagLib.File;

namespace BookStitch.Tests.Services;

public sealed partial class RealMediaIntegrationTests
{
    private static readonly ExportPreset Stereo96 = ExportPreset.Parse("AAC Stereo 96 kbps");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_no_chapters_validates_and_exports_zero_real_chapters_without_mutating_sources(bool embedded)
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var temp = new TemporaryFolder();
        var seed = Path.Combine(temp.Path, "seed.m4a");
        await RunProcessAsync(tools.FfmpegPath, ["-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "2", "-c:a", "aac", seed]);
        var source = Path.Combine(temp.Path, "input.m4b");
        var sourceMetadata = Path.Combine(temp.Path, "source.ffmetadata");
        await File.WriteAllTextAsync(sourceMetadata, ";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=2000\ntitle=Embedded\n");
        if (embedded) await RunProcessAsync(tools.FfmpegPath, ["-y", "-i", seed, "-i", sourceMetadata, "-map", "0:a", "-map_chapters", "1", "-c:a", "copy", source]);
        else File.Copy(seed, source);
        var before = File.ReadAllBytes(source);
        var probe = await new AudioInfoService().ProbeAsync(source, tools.FfprobePath);
        Assert.True(probe.Success, probe.ErrorMessage);
        Assert.Equal(embedded ? 1 : 0, probe.Chapters.Count);
        var track = new TrackInfo { Index = 1, FilePath = source, FileName = Path.GetFileName(source),
            Extension = ".m4b", Codec = "AAC", ProcessingAction = ProcessingActionIds.Reuse,
            DurationTicks = probe.Duration?.Ticks, EmbeddedChapters = probe.Chapters, ChapterTitle = "" };
        var settings = new ChapterSettings { Mode = ChapterSettingsService.ModeNone };
        var validation = new ExportValidationService().Validate([track],
            new FfmpegToolStatus { FfmpegAvailable = true, FfprobeAvailable = true,
                FfmpegPath = tools.FfmpegPath, FfprobePath = tools.FfprobePath },
            temp.Path, temp.Path, "output.m4b", ".m4b", temp.Path, chapterMode: settings.Mode);
        Assert.Empty(validation.Errors); Assert.Empty(validation.Warnings);
        var rendered = new ChapterModeEngineService().Apply(new ChapterAutoEngineRequest([track],
            ProjectManifestTypes.FolderProject, "no-chapters-regression", "",
            new ChapterCanonicalMetadataSnapshot(1, "", MetadataOrigin.None, "", MetadataOrigin.None), settings, false));
        Assert.Empty(rendered.RenderedTimeline);
        var metadata = Path.Combine(temp.Path, "chapters.ffmetadata");
        await File.WriteAllTextAsync(metadata, new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(rendered.RenderedTimeline));
        var concat = Path.Combine(temp.Path, "concat-list.txt");
        await File.WriteAllTextAsync(concat, "file '" + source.Replace("\\", "/") + "'\n");
        var output = Path.Combine(temp.Path, "output.m4b");
        await new AacExportProcessingService().MergeConvertedTracksAsync(concat, metadata, output,
            probe.Duration!.Value.Ticks, tools.FfmpegPath, default, _ => { });
        new FinalTagService().WriteFinalTags(output, new FinalAudioTagData { Title = "No chapters" });
        var result = await new AudioInfoService().ProbeAsync(output, tools.FfprobePath);
        Assert.True(result.Success, result.ErrorMessage); Assert.Empty(result.Chapters);
        await RunProcessAsync(tools.FfmpegPath, ["-v", "error", "-xerror", "-i", output, "-f", "null", "-"]);
        Assert.Equal(before, File.ReadAllBytes(source)); Assert.Equal(ChapterSettingsService.ModeNone, settings.Mode);
    }

    [Fact]
    public async Task M4bEmbeddedChapters_RoundTripThroughFfprobeParserTimelineAndExportMetadata()
    {
        var tools = ResolveFfmpegToolchain();
        if (tools is null)
            return;

        using var temp = new TemporaryFolder();
        var sourcePath = Path.Combine(temp.Path, "source.m4a");
        var chapterMetadataPath = Path.Combine(temp.Path, "chapters.ffmetadata");
        var chapteredPath = Path.Combine(temp.Path, "chaptered.m4b");

        await RunProcessAsync(
            tools.FfmpegPath,
            [
                "-y",
                "-f",
                "lavfi",
                "-i",
                "anullsrc=r=44100:cl=mono",
                "-t",
                "3",
                "-c:a",
                "aac",
                "-b:a",
                "64k",
                sourcePath
            ]);

        await File.WriteAllTextAsync(
            chapterMetadataPath,
            string.Join(
                Environment.NewLine,
                [
                    ";FFMETADATA1",
                    "",
                    "[CHAPTER]",
                    "TIMEBASE=1/1000",
                    "START=0",
                    "END=1000",
                    "title=Intro",
                    "",
                    "[CHAPTER]",
                    "TIMEBASE=1/1000",
                    "START=1000",
                    "END=2000",
                    "title=Mitte",
                    "",
                    "[CHAPTER]",
                    "TIMEBASE=1/1000",
                    "START=2000",
                    "END=3000",
                    "title=Ende",
                    ""
                ]),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await RunProcessAsync(
            tools.FfmpegPath,
            [
                "-y",
                "-i",
                sourcePath,
                "-i",
                chapterMetadataPath,
                "-map",
                "0:a",
                "-map_metadata",
                "1",
                "-map_chapters",
                "1",
                "-c",
                "copy",
                chapteredPath
            ]);

        var probe = await new AudioInfoService().ProbeAsync(chapteredPath, tools.FfprobePath);

        Assert.True(probe.Success, probe.ErrorMessage);
        Assert.True(probe.HasPlausibleAudioProperties, probe.ErrorMessage);
        Assert.Equal(["Intro", "Mitte", "Ende"], probe.Chapters.Select(chapter => chapter.Title).ToList());
        Assert.Equal([0, 1000, 2000], probe.Chapters.Select(chapter => ToMilliseconds(chapter.StartTicks)).ToList());
        Assert.Equal([1000, 2000, 3000], probe.Chapters.Select(chapter => ToMilliseconds(chapter.EndTicks)).ToList());

        var track = new TrackInfo
        {
            FilePath = chapteredPath,
            FileName = Path.GetFileName(chapteredPath),
            DurationTicks = probe.Duration?.Ticks,
            EmbeddedChapters = probe.Chapters
        };
        var timeline = new ChapterTimelineService().BuildTimeline([track]);

        Assert.Equal(3, timeline.Count);
        Assert.All(timeline, chapter => Assert.False(chapter.IsSynthetic));
        Assert.Equal(["Intro", "Mitte", "Ende"], timeline.Select(chapter => chapter.Title).ToList());

        var rendered = new ChapterModeEngineService().Apply(new ChapterAutoEngineRequest(
            [track],
            ProjectManifestTypes.FolderProject,
            "real-media",
            string.Empty,
            new ChapterCanonicalMetadataSnapshot(1, "", MetadataOrigin.None, "", MetadataOrigin.None),
            new ChapterSettings { NumberingStyle = ChapterNumberingStyle.Plain.ToString() },
            LegacyUseLeadingZeros: false));
        var exportMetadata = new ExportChapterService().BuildFfmpegChapterMetadataFromTimeline(rendered.RenderedTimeline);

        Assert.Equal(["1 Intro", "2 Mitte", "3 Ende"], ExtractChapterTitles(exportMetadata));
    }

    [Fact]
    public async Task FinalTagService_RewritesExistingM4aAppleFreeformsAndPreservesCoverAndChapters()
    {
        var tools = ResolveFfmpegToolchain();
        if (tools is null)
            return;

        using var temp = new TemporaryFolder();
        var sourcePath = Path.Combine(temp.Path, "source.m4a");
        var chapterMetadataPath = Path.Combine(temp.Path, "chapters.ffmetadata");
        var chapteredPath = Path.Combine(temp.Path, "chaptered.m4a");
        var coverPath = WriteTinyJpeg(temp, "cover.jpg");

        await RunProcessAsync(
            tools.FfmpegPath,
            [
                "-y",
                "-f",
                "lavfi",
                "-i",
                "anullsrc=r=44100:cl=mono",
                "-t",
                "2",
                "-c:a",
                "aac",
                "-b:a",
                "64k",
                sourcePath
            ]);

        await File.WriteAllTextAsync(
            chapterMetadataPath,
            string.Join(
                Environment.NewLine,
                [
                    ";FFMETADATA1",
                    "",
                    "[CHAPTER]",
                    "TIMEBASE=1/1000",
                    "START=0",
                    "END=1000",
                    "title=Start",
                    "",
                    "[CHAPTER]",
                    "TIMEBASE=1/1000",
                    "START=1000",
                    "END=2000",
                    "title=Ende",
                    ""
                ]),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await RunProcessAsync(
            tools.FfmpegPath,
            [
                "-y",
                "-i",
                sourcePath,
                "-i",
                chapterMetadataPath,
                "-map",
                "0:a",
                "-map_metadata",
                "1",
                "-map_chapters",
                "1",
                "-c",
                "copy",
                chapteredPath
            ]);

        var service = new FinalTagService();
        service.WriteFinalTags(
            chapteredPath,
            new FinalAudioTagData
            {
                Title = "First",
                Album = "First Album",
                Authors = ["Author"],
                Narrators = ["Narrator"],
                Series = [new SeriesEntry("Series", "3")],
                Description = "Blurb",
                CoverPath = coverPath,
                CompatibilityDescriptionEnabled = true,
                OutputPresentationCulture = "en-US"
            });

        service.WriteFinalTags(
            chapteredPath,
            new FinalAudioTagData
            {
                Title = "Second",
                Album = "Second Album",
                Authors = ["Author"],
                DisabledFields = new HashSet<ProjectMetadataField>
                {
                    ProjectMetadataField.Series,
                    ProjectMetadataField.Description
                },
                CompatibilityDescriptionEnabled = true,
                OutputPresentationCulture = "en-US"
            });

        var tags = new TagMetadataReader().Read(chapteredPath);
        Assert.False(tags.Failed);
        Assert.Equal("Second", tags.Original.Title);
        Assert.Equal("Second Album", tags.Original.Album);
        Assert.Empty(tags.Original.Series);
        Assert.Equal(string.Empty, tags.Original.Description);
        Assert.True(tags.HasEmbeddedCover);

        var probe = await new AudioInfoService().ProbeAsync(chapteredPath, tools.FfprobePath);
        Assert.True(probe.Success, probe.ErrorMessage);
        Assert.Equal(["Start", "Ende"], probe.Chapters.Select(chapter => chapter.Title).ToList());
        Assert.Equal([0, 1000], probe.Chapters.Select(chapter => ToMilliseconds(chapter.StartTicks)).ToList());
        Assert.Equal([1000, 2000], probe.Chapters.Select(chapter => ToMilliseconds(chapter.EndTicks)).ToList());
    }

    [Fact]
    public async Task FinalTagService_WritesRealM4aUnicodeMultiValuesDatesIdentifiersAndLongDuration()
    {
        var tools = ResolveFfmpegToolchain();
        if (tools is null)
            return;

        using var temp = new TemporaryFolder();
        var unicodeFolder = Path.Combine(temp.Path, "Unicode Ω 中 Кириллица");
        Directory.CreateDirectory(unicodeFolder);
        var audioPath = Path.Combine(unicodeFolder, "Quelle Ω 你好.m4a");
        var coverPath = WriteTinyJpeg(temp, "cover.jpg");

        await CreateSilentAacAsync(tools, audioPath, seconds: 1);

        new FinalTagService().WriteFinalTags(
            audioPath,
            new FinalAudioTagData
            {
                Title = "Книга 你好 e\u0301",
                Subtitle = "Untertitel Ω",
                Album = "Album 世界",
                Authors = ["Anaïs Nin", "李雷", "Мария"],
                Narrators = ["Sprecher Ω", "旁白"],
                Genres = ["Audiobook", "幻想"],
                Languages = ["de", "zh-Hans"],
                OriginalLanguages = ["ru"],
                TargetLanguages = ["de"],
                Series = [new SeriesEntry("Saga 零", "2"), new SeriesEntry("Nebenreihe", "β")],
                ISBN = "978-3-16-148410-0",
                ASIN = "B00TESTΩ",
                Barcode = "9783161484100",
                CatalogNumbers = ["CAT-Ω", "目録-2"],
                ReleaseDate = new MetadataDate(2024),
                Year = 2024,
                Publisher = "Verlag 世界",
                Copyright = "© 2024 Autor Ω",
                PhonogramCopyright = "℗ 2024 Studio Ω",
                License = "CC-BY-4.0",
                SortTitle = "Saga 0002",
                SortAlbum = "Album Sort",
                SortAuthors = ["Nin, Anaïs", "Li, Lei"],
                SortNarrators = ["Sprecher, Omega"],
                OutputTrackNumber = 1,
                OutputTrackTotal = 9,
                Description = "Klappentext mit Unicode: Добрый день / 你好 / e\u0301",
                CoverPath = coverPath,
                Duration = TimeSpan.FromHours(25) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3),
                CompatibilityDescriptionEnabled = true,
                OutputPresentationCulture = "en-US"
            });

        using var file = TagLibFile.Create(audioPath);
        var tag = Assert.IsType<AppleTag>(file.GetTag(TagLib.TagTypes.Apple));
        Assert.Equal("Книга 你好 e\u0301", tag.Title);
        Assert.Equal("Untertitel Ω", tag.Subtitle);
        Assert.Equal("Album 世界", tag.Album);
        Assert.Equal(["Anaïs Nin", "李雷", "Мария"], tag.AlbumArtists);
        Assert.Equal(["Anaïs Nin", "李雷", "Мария"], tag.Performers);
        Assert.Equal(["Audiobook", "幻想"], tag.Genres);
        Assert.Equal(2024u, tag.Year);
        Assert.Equal(1u, tag.Track);
        Assert.Equal(9u, tag.TrackCount);
        Assert.Equal("Saga 0002", tag.TitleSort);
        Assert.Equal("Album Sort", tag.AlbumSort);
        Assert.Equal(["Nin, Anaïs", "Li, Lei"], tag.AlbumArtistsSort);
        Assert.Equal(["Nin, Anaïs", "Li, Lei"], tag.PerformersSort);
        Assert.Equal(["Sprecher Ω", "旁白"], tag.GetDashBoxes("com.apple.iTunes", "NARRATOR"));
        Assert.Equal(["Sprecher, Omega"], tag.GetDashBoxes("com.apple.iTunes", "SORTNARRATORS"));
        Assert.Equal(["de", "zh-Hans"], tag.GetDashBoxes("com.apple.iTunes", "LANGUAGE"));
        Assert.Equal(["Saga 零"], tag.GetDashBoxes("com.apple.iTunes", "SERIES"));
        Assert.Equal(["2"], tag.GetDashBoxes("com.apple.iTunes", "SERIES-PART"));
        Assert.Equal(["978-3-16-148410-0"], tag.GetDashBoxes("com.apple.iTunes", "ISBN"));
        Assert.Equal(["B00TESTΩ"], tag.GetDashBoxes("com.apple.iTunes", "ASIN"));
        Assert.Equal(["9783161484100"], tag.GetDashBoxes("com.apple.iTunes", "BARCODE"));
        Assert.Equal(["CAT-Ω", "目録-2"], tag.GetDashBoxes("com.apple.iTunes", "CATALOGNUMBER"));
        Assert.Equal(["2024"], tag.GetDashBoxes("com.apple.iTunes", "RELEASEDATE"));
        Assert.Null(tag.GetDashBoxes("com.apple.iTunes", "ORIGINALDATE"));
        Assert.Null(tag.GetDashBoxes("com.apple.iTunes", "PUBLICATIONDATE"));
        Assert.Contains("Title: Книга 你好 e\u0301", tag.Description);
        Assert.Contains("Duration: 25:02:03", tag.Description);
        Assert.Equal(["• Title: Книга 你好 e\u0301 \n• Album: Album 世界 \n• Author: Anaïs Nin, 李雷, Мария \n• Series: Saga 零 2 \n• Series: Nebenreihe β \n• Narrator: Sprecher Ω, 旁白 \n• Language: de, zh-Hans \n• Duration: 25:02:03"], tag.GetDashBoxes("com.apple.iTunes", "BOOKSTITCH_COMPATIBILITY"));
        Assert.Single(tag.Pictures);

        var readBack = new TagMetadataReader().Read(audioPath);
        Assert.False(readBack.Failed);
        Assert.Equal("Книга 你好 e\u0301", readBack.Original.Title);
        Assert.Equal(["Anaïs Nin", "李雷", "Мария"], readBack.Original.Authors);
        Assert.Equal(["Sprecher Ω", "旁白"], readBack.Original.Narrators);
        Assert.Equal(
            [new SeriesEntry("Saga 零", "2"), new SeriesEntry("Nebenreihe", "β")],
            readBack.Original.Series);
        Assert.Equal("978-3-16-148410-0", readBack.Original.ISBN);
        Assert.Equal("B00TESTΩ", readBack.Original.ASIN);
        Assert.Equal("9783161484100", readBack.Original.Barcode);
        Assert.Equal(["CAT-Ω", "目録-2"], readBack.Original.CatalogNumbers);
        Assert.Equal("Klappentext mit Unicode: Добрый день / 你好 / e\u0301", readBack.Original.Description);
    }

    [Fact]
    public async Task FinalTagService_RealM4aDisabledFieldsSuppressMirrorsAndSourceOnlyTags()
    {
        var tools = ResolveFfmpegToolchain();
        if (tools is null)
            return;

        using var temp = new TemporaryFolder();
        var audioPath = Path.Combine(temp.Path, "disabled-fields.m4a");
        await CreateSilentAacAsync(tools, audioPath, seconds: 1);

        using (var file = TagLibFile.Create(audioPath))
        {
            var tag = Assert.IsType<AppleTag>(file.GetTag(TagLib.TagTypes.Apple, true));
            tag.Disc = 3;
            tag.DiscCount = 5;
            tag.ISRC = "SOURCE-ISRC";
            tag.MusicBrainzTrackId = "source-track-id";
            tag.ReplayGainTrackGain = 1.5;
            tag.ReplayGainTrackPeak = 0.9;
            tag.ReplayGainAlbumGain = 2.5;
            tag.ReplayGainAlbumPeak = 0.8;
            tag.SetDashBoxes("com.apple.iTunes", "SERIES", ["Old Series"]);
            file.Save();
        }

        new FinalTagService().WriteFinalTags(
            audioPath,
            new FinalAudioTagData
            {
                Title = "Disabled Contract",
                Description = "Must not leak",
                Series = [new SeriesEntry("Series", "4")],
                SortTitle = "Sort Title",
                SortAlbum = "Sort Album",
                SortAuthors = ["Sort Author"],
                DisabledFields = new HashSet<ProjectMetadataField>
                {
                    ProjectMetadataField.Description,
                    ProjectMetadataField.Series,
                    ProjectMetadataField.SortTitle,
                    ProjectMetadataField.SortAlbum,
                    ProjectMetadataField.SortAuthors
                },
                CompatibilityDescriptionEnabled = true,
                OutputPresentationCulture = "en-US"
            });

        using var result = TagLibFile.Create(audioPath);
        var resultTag = Assert.IsType<AppleTag>(result.GetTag(TagLib.TagTypes.Apple));
        Assert.Equal("Disabled Contract", resultTag.Title);
        Assert.True(string.IsNullOrEmpty(resultTag.Description));
        Assert.True(string.IsNullOrEmpty(resultTag.TitleSort));
        Assert.True(string.IsNullOrEmpty(resultTag.AlbumSort));
        Assert.Empty(resultTag.AlbumArtistsSort);
        AssertEmptyDashBoxes(resultTag, "SERIES");
        AssertEmptyDashBoxes(resultTag, "SERIES-PART");
        AssertEmptyDashBoxes(resultTag, "BOOKSTITCH_SERIES");
        Assert.Equal(["• Title: Disabled Contract"], resultTag.GetDashBoxes("com.apple.iTunes", "BOOKSTITCH_COMPATIBILITY"));
        Assert.Equal(0u, resultTag.Disc);
        Assert.Equal(0u, resultTag.DiscCount);
        Assert.Equal(string.Empty, resultTag.ISRC);
        Assert.Equal(string.Empty, resultTag.MusicBrainzTrackId);
        Assert.True(double.IsNaN(resultTag.ReplayGainTrackGain));
        Assert.True(double.IsNaN(resultTag.ReplayGainTrackPeak));
        Assert.True(double.IsNaN(resultTag.ReplayGainAlbumGain));
        Assert.True(double.IsNaN(resultTag.ReplayGainAlbumPeak));
    }

    [Fact]
    public async Task OggOgaAndOpusInputs_ProbeConvertAndPreserveEmbeddedChapterSemantics()
    {
        var tools = ResolveFfmpegToolchain();
        if (tools is null)
            return;

        var corpusFolder = ResolveAudioTestCorpusFolder();
        if (corpusFolder is null)
            return;

        using var temp = new TemporaryFolder();
        var cases = new[]
        {
            new RealInputCase("16 OGG Vorbis - no chapters.ogg", "vorbis", ExpectedChapters: 0),
            new RealInputCase("17 OGG Vorbis - 4 chapters.ogg", "vorbis", ExpectedChapters: 4),
            new RealInputCase("18 OGA Vorbis - 4 chapters.oga", "vorbis", ExpectedChapters: 4),
            new RealInputCase("19 Opus - no chapters.opus", "opus", ExpectedChapters: 0),
            new RealInputCase("20 Opus - 4 chapters.opus", "opus", ExpectedChapters: 4),
            new RealInputCase("21 OGG container - Opus codec - 4 chapters.ogg", "opus", ExpectedChapters: 4)
        };

        foreach (var testCase in cases)
        {
            var sourcePath = Path.Combine(corpusFolder, testCase.FileName);

            var probe = await new AudioInfoService().ProbeAsync(sourcePath, tools.FfprobePath);

            Assert.True(probe.Success, $"{testCase.FileName}: {probe.ErrorMessage}");
            Assert.True(probe.HasPlausibleAudioProperties, $"{testCase.FileName}: {probe.ErrorMessage}");
            Assert.Equal(testCase.ExpectedCodec, probe.CodecName, ignoreCase: true);
            Assert.Equal(ProcessingActionIds.Convert, AudioProcessingService.DetermineProcessingAction(probe, Stereo96));

            var track = new TrackInfo
            {
                FilePath = sourcePath,
                FileName = Path.GetFileName(sourcePath),
                Extension = Path.GetExtension(sourcePath)
            };
            new TrackAudioProbeApplicationService().ApplySuccessfulProbe(
                track,
                probe,
                Stereo96,
                markAudioValidationPassed: true);

            Assert.Equal(testCase.ExpectedCodec.ToUpperInvariant(), track.Codec);
            Assert.Equal(ProcessingActionIds.Convert, track.ProcessingAction);
            Assert.True(track.AudioValidationPassed);

            var tagMetadata = new TagMetadataReader().Read(sourcePath);
            Assert.False(tagMetadata.Failed);
            Assert.NotNull(tagMetadata.Original.Title);
            Assert.NotNull(tagMetadata.Original.Album);
            Assert.True(tagMetadata.TechnicalDurationFallback is { } duration && duration > TimeSpan.Zero);
            Assert.True(tagMetadata.TechnicalChannelsFallback is > 0);

            Assert.Equal(testCase.ExpectedChapters, probe.Chapters.Count);
            if (testCase.ExpectedChapters > 0)
            {
                Assert.Equal(probe.Chapters.Select(chapter => chapter.Title), track.EmbeddedChapters.Select(chapter => chapter.Title));

                var timeline = new ChapterTimelineService().BuildTimeline([track]);
                Assert.Equal(testCase.ExpectedChapters, timeline.Count);
                Assert.All(timeline, chapter => Assert.False(chapter.IsSynthetic));
            }

            var convertedPath = Path.Combine(temp.Path, Path.GetFileNameWithoutExtension(testCase.FileName) + ".m4a");
            await RunProcessAsync(
                tools.FfmpegPath,
                [
                    "-y",
                    "-i",
                    sourcePath,
                    "-map",
                    "0:a:0",
                    "-c:a",
                    "aac",
                    "-b:a",
                    "96k",
                    convertedPath
                ]);

            var convertedProbe = await new AudioInfoService().ProbeAsync(convertedPath, tools.FfprobePath);
            Assert.True(convertedProbe.Success, $"{testCase.FileName} conversion: {convertedProbe.ErrorMessage}");
            Assert.True(convertedProbe.IsAac, convertedProbe.CodecName);
        }
    }

    private static FfmpegToolchain? ResolveFfmpegToolchain()
    {
        var ffmpegPath = ResolveToolPath("ffmpeg");
        var ffprobePath = ResolveToolPath("ffprobe");

        return string.IsNullOrWhiteSpace(ffmpegPath) || string.IsNullOrWhiteSpace(ffprobePath)
            ? null
            : new FfmpegToolchain(ffmpegPath, ffprobePath);
    }

    private static string? ResolveToolPath(string toolName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim(), toolName + (OperatingSystem.IsWindows() ? ".exe" : ""));
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries in test environments.
            }
        }

        return null;
    }

    private static string? ResolveAudioTestCorpusFolder()
    {
        var candidate = Environment.GetEnvironmentVariable("BOOKSTITCH_AUDIO_TEST_CORPUS");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static async Task RunProcessAsync(string executablePath, IReadOnlyList<string> arguments)
    {
        using var process = new Process();
        var stderr = new StringBuilder();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null && stderr.Length < 4000)
                stderr.AppendLine(args.Data);
        };

        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"{Path.GetFileName(executablePath)} failed with exit code {process.ExitCode}:{Environment.NewLine}{stderr}");
    }

    private static Task CreateSilentAacAsync(FfmpegToolchain tools, string audioPath, int seconds) =>
        RunProcessAsync(
            tools.FfmpegPath,
            [
                "-y",
                "-f",
                "lavfi",
                "-i",
                "anullsrc=r=44100:cl=mono",
                "-t",
                seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-c:a",
                "aac",
                "-b:a",
                "64k",
                audioPath
            ]);

    private static List<string> ExtractChapterTitles(string metadata)
    {
        return metadata
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("title=", StringComparison.Ordinal))
            .Select(line => line["title=".Length..])
            .ToList();
    }

    private static long ToMilliseconds(long ticks)
    {
        return (long)Math.Round(ticks / (double)TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero);
    }

    private static void AssertEmptyDashBoxes(AppleTag tag, string name)
    {
        var values = tag.GetDashBoxes("com.apple.iTunes", name);
        Assert.True(values is null || values.Length == 0, $"{name} should be absent.");
    }

    private static string WriteTinyJpeg(TemporaryFolder folder, string fileName)
    {
        var path = Path.Combine(folder.Path, fileName);
        var bytes = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAH/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAEFAqf/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/ASP/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/ASP/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAY/Al//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/IV//2gAMAwEAAgADAAAAEP/EFBQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQMBAT8QH//EFBQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQIBAT8QH//EFBABAQAAAAAAAAAAAAAAAAAAARD/2gAIAQEAAT8QH//Z");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private sealed record FfmpegToolchain(string FfmpegPath, string FfprobePath);
    private sealed record RealInputCase(string FileName, string ExpectedCodec, int ExpectedChapters);
}
