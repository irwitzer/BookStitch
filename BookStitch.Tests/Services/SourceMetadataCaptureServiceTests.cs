using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SourceMetadataCaptureServiceTests
{
    [Fact]
    public void BuildSnapshot_WhenTagReadSucceedsBeforeFfprobe_PreservesRawTagsAndTagFallbackTechnicalInfo()
    {
        var sourceId = Guid.NewGuid();
        var tagResult = new TagMetadataReadResult(
            new OriginalTagMetadata
            {
                Title = " Titel ",
                OriginalTitle = " Original work title ",
                Album = "Album",
                Authors = ["Autor 1", "Autor 2", "autor 1"],
                Narrators = ["Sprecher 1", "Sprecher 2"],
                Genres = ["Genre 1", "Genre 2", "Genre 3"],
                AbridgedStatus = "0",
                AudioWorkType = "Audio Drama",
                Barcode = "9781234567890",
                RawTags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TXXX:ASIN"] = [" B012345678 "],
                    ["TITLE"] = ["Titel"]
                }
            },
            TechnicalDurationFallback: TimeSpan.FromSeconds(5),
            TechnicalBitrateKbpsFallback: 128,
            TechnicalChannelsFallback: 2,
            HasEmbeddedCover: false,
            Failed: false);

        var snapshot = SourceMetadataCaptureService.BuildSnapshot(
            new SourceMetadataCaptureRequest(sourceId, @"D:\Audio\source.mp3"),
            tagResult,
            coverData: null);

        Assert.Equal(sourceId, snapshot.SourceId);
        Assert.Equal("Titel", snapshot.CanonicalFields.Title);
        Assert.Equal("Original work title", snapshot.CanonicalFields.OriginalTitle);
        Assert.Equal(["Autor 1", "Autor 2"], snapshot.CanonicalFields.Authors);
        Assert.Equal(["Sprecher 1", "Sprecher 2"], snapshot.CanonicalFields.Narrators);
        Assert.Equal(["Genre 1", "Genre 2", "Genre 3"], snapshot.CanonicalFields.Genres);
        Assert.Equal("9781234567890", snapshot.CanonicalFields.Barcode);
        Assert.Equal(AbridgedStatus.Unabridged, snapshot.CanonicalFields.AbridgedStatus);
        Assert.Equal(AudioWorkType.AudioDrama, snapshot.CanonicalFields.AudioWorkType);
        Assert.Equal(TimeSpan.FromSeconds(5), snapshot.TechnicalInfo.Duration);
        Assert.Equal(128, snapshot.TechnicalInfo.BitrateKbps);
        Assert.Equal(2, snapshot.TechnicalInfo.Channels);
        Assert.Equal(["TITLE", "TXXX:ASIN"], snapshot.RawTags.Select(tag => tag.OriginalKey).ToArray());
        Assert.Empty(snapshot.ReadDiagnostics);
    }

    [Fact]
    public void BuildSnapshot_WhenAbridgedStatusOrAudioWorkTypeIsAmbiguous_DoesNotInventCanonicalValues()
    {
        var snapshot = SourceMetadataCaptureService.BuildSnapshot(
            new SourceMetadataCaptureRequest(Guid.NewGuid(), @"D:\Audio\source.mp3"),
            new TagMetadataReadResult(
                new OriginalTagMetadata
                {
                    AbridgedStatus = "maybe",
                    AudioWorkType = "spoken content"
                },
                null,
                null,
                null,
                HasEmbeddedCover: false,
                Failed: false),
            coverData: null);

        Assert.Null(snapshot.CanonicalFields.AbridgedStatus);
        Assert.Null(snapshot.CanonicalFields.AudioWorkType);
    }

    [Fact]
    public void BuildSnapshot_TransportsCanonicalCoverageAndKeepsDirectSourceYear()
    {
        var original = new OriginalTagMetadata
        {
            Description = "Beschreibung",
            Keywords = ["eins", "zwei"],
            ContentType = "Audiobook",
            Comment = "Kommentar",
            Translators = ["Übersetzerin"],
            Composers = ["Komponist A", "Komponist B"],
            Performers = ["Performer"],
            Contributors = ["Beitrag"],
            Hosts = ["Host"],
            Guests = ["Gast"],
            Speakers = ["Stimme"],
            Volume = "2",
            Edition = "Ungekürzt",
            OriginalLanguages = ["en"],
            TargetLanguages = ["de"],
            Script = "Latn",
            Publisher = "Verlag",
            ReleaseDate = "31.01.2024",
            Year = 1997,
            Copyright = "Copyright",
            PhonogramCopyright = "Phonogram",
            License = "Lizenz",
            Categories = ["Fiktion"],
            Series = [new SeriesEntry("Hauptreihe", "4"), new SeriesEntry("Nebenreihe", "1")],
            Barcode = "9781234567890",
            CatalogNumbers = ["CAT-1", "CAT-2"],
            MusicBrainzReleaseId = "mb-release",
            SortTitle = "Titel, Der",
            SortAuthors = ["Autor, Ada"],
            SortNarrators = ["Stimme, Nina"],
            SortAlbum = "Album, Das",
            SortSeries = "Reihe, Die",
            Website = "https://example.test/book",
            Source = "Publisher archive"
        };

        var snapshot = SourceMetadataCaptureService.BuildSnapshot(
            new SourceMetadataCaptureRequest(Guid.NewGuid(), @"D:\Audio\source.m4b"),
            new TagMetadataReadResult(original, null, null, null, false, Failed: false),
            coverData: null);

        Assert.Equal("Beschreibung", snapshot.CanonicalFields.Description);
        Assert.Equal(["eins", "zwei"], snapshot.CanonicalFields.Keywords);
        Assert.Equal(["Übersetzerin"], snapshot.CanonicalFields.Translators);
        Assert.Equal(["Komponist A", "Komponist B"], snapshot.CanonicalFields.Composers);
        Assert.Equal(["Performer"], snapshot.CanonicalFields.Performers);
        Assert.Equal(["Beitrag"], snapshot.CanonicalFields.Contributors);
        Assert.Equal(["Host"], snapshot.CanonicalFields.Hosts);
        Assert.Equal(["Gast"], snapshot.CanonicalFields.Guests);
        Assert.Equal(["Stimme"], snapshot.CanonicalFields.Speakers);
        Assert.Equal(new MetadataDate(2024, 1, 31), snapshot.CanonicalFields.ReleaseDate);
        Assert.Equal(1997, snapshot.CanonicalFields.Year);
        Assert.Equal(
            [new SeriesEntry("Hauptreihe", "4"), new SeriesEntry("Nebenreihe", "1")],
            snapshot.CanonicalFields.Series);
        Assert.Equal("9781234567890", snapshot.CanonicalFields.Barcode);
        Assert.Equal(["CAT-1", "CAT-2"], snapshot.CanonicalFields.CatalogNumbers);
        Assert.Equal("https://example.test/book", snapshot.CanonicalFields.Website);
        Assert.Equal("Publisher archive", snapshot.CanonicalFields.Source);
    }

    [Fact]
    public void BuildSnapshot_WhenDirectYearIsMissing_DerivesYearFromTolerantReleaseDate()
    {
        var snapshot = SourceMetadataCaptureService.BuildSnapshot(
            new SourceMetadataCaptureRequest(Guid.NewGuid(), @"D:\Audio\source.m4b"),
            new TagMetadataReadResult(
                new OriginalTagMetadata { ReleaseDate = "31/01/2026" },
                null,
                null,
                null,
                false,
                Failed: false),
            coverData: null);

        Assert.Equal(new MetadataDate(2026, 1, 31), snapshot.CanonicalFields.ReleaseDate);
        Assert.Equal(2026, snapshot.CanonicalFields.Year);
    }

    [Fact]
    public void BuildSnapshot_WhenFfprobeSucceeds_PreservesEmbeddedChaptersAndTechnicalData()
    {
        var sourceId = Guid.NewGuid();
        var probe = new AudioProbeInfo
        {
            Success = true,
            CodecName = "aac",
            FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
            Duration = TimeSpan.FromSeconds(12),
            BitrateKbps = 96,
            Channels = 1,
            Chapters =
            [
                new TrackEmbeddedChapterInfo
                {
                    Title = "Kapitel 1",
                    StartTicks = 0,
                    EndTicks = TimeSpan.FromSeconds(6).Ticks
                }
            ]
        };

        var snapshot = SourceMetadataCaptureService.BuildSnapshot(
            new SourceMetadataCaptureRequest(sourceId, @"D:\Audio\source.m4b", ProbeInfo: probe),
            new TagMetadataReadResult(new OriginalTagMetadata(), null, null, null, false, Failed: false),
            coverData: null);

        Assert.Equal("aac", snapshot.TechnicalInfo.Codec);
        Assert.Equal("mov,mp4,m4a,3gp,3g2,mj2", snapshot.TechnicalInfo.Container);
        Assert.Equal(TimeSpan.FromSeconds(12), snapshot.TechnicalInfo.Duration);
        var chapter = Assert.Single(snapshot.OriginalChapters);
        Assert.Equal("Kapitel 1", chapter.Title);
    }

    [Fact]
    public void BuildSnapshot_WhenCoverIsReadable_StoresEvidenceFingerprintOnly()
    {
        var sourceId = Guid.NewGuid();
        var snapshot = SourceMetadataCaptureService.BuildSnapshot(
            new SourceMetadataCaptureRequest(sourceId, @"D:\Audio\source.mp3"),
            new TagMetadataReadResult(new OriginalTagMetadata(), null, null, null, HasEmbeddedCover: true, Failed: false),
            new EmbeddedCoverData([1, 2, 3], "image/jpeg"));

        Assert.True(snapshot.CoverCandidate.HasEmbeddedCover);
        Assert.Equal("image/jpeg", snapshot.CoverCandidate.MimeType);
        Assert.Equal(3, snapshot.CoverCandidate.ByteLength);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.CoverCandidate.Fingerprint));
    }

    [Fact]
    public void PublishIfMissing_WhenSourceFileIsUnreadable_PublishesGapDiagnosticWithoutInventingRawTags()
    {
        using var folder = new TemporaryFolder();
        var sourceId = Guid.NewGuid();
        var sourcePath = Path.Combine(folder.Path, "source.mp3");
        File.WriteAllText(sourcePath, "not audio");
        var service = new SourceMetadataCaptureService();

        var result = service.PublishIfMissing(
            folder.Path,
            new SourceMetadataCaptureRequest(sourceId, sourcePath));
        var loaded = new SourceMetadataSnapshotStore().TryLoad(folder.Path, sourceId);

        Assert.True(result.Published);
        Assert.NotNull(loaded);
        Assert.Empty(loaded.RawTags);
        Assert.Contains(loaded.ReadDiagnostics, item => item.Contains("Tags konnten nicht gelesen werden", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublishIfMissing_WhenReaderReportsDiagnostics_PersistsDiagnosticsInSnapshot()
    {
        using var folder = new TemporaryFolder();
        var sourceId = Guid.NewGuid();
        var sourcePath = Path.Combine(folder.Path, "source.mp3");
        File.WriteAllText(sourcePath, "fake audio");
        var service = new SourceMetadataCaptureService(
            new SourceMetadataSnapshotStore(),
            new DiagnosticTagMetadataReader());

        service.PublishIfMissing(
            folder.Path,
            new SourceMetadataCaptureRequest(sourceId, sourcePath));

        var loaded = new SourceMetadataSnapshotStore().TryLoad(folder.Path, sourceId);

        Assert.NotNull(loaded);
        Assert.Contains(loaded.ReadDiagnostics, item => item.Contains("Reader-Hinweis", StringComparison.Ordinal));
    }

    [Fact]
    public void BackfillMissingSnapshots_UsesStableCopyIdentityAndIsIdempotent()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var sourcePath = Path.Combine(folder.Path, "external.mp3");
        var targetPath = Path.Combine(ProjectFolderLayout.GetOriginalsFolder(folder.Path), "external.mp3");
        File.WriteAllText(sourcePath, "not audio");
        File.WriteAllText(targetPath, "not audio");
        var identity = new SourceCopyIdentityStore().RecordCopiedFile(folder.Path, sourcePath, targetPath);
        var service = new SourceMetadataCaptureService();

        service.BackfillMissingSnapshots(folder.Path);
        service.BackfillMissingSnapshots(folder.Path);

        var snapshots = new SourceMetadataSnapshotStore().LoadAll(folder.Path);
        var snapshot = Assert.Single(snapshots);
        Assert.Equal(identity.SourceId, snapshot.SourceId);
        Assert.Equal(targetPath, snapshot.TechnicalInfo.SourcePath);
        Assert.Contains(snapshot.ReadDiagnostics, item => item.Contains("source-copy-identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublishIfMissing_ParallelHundredSources_DoNotLoseSnapshots()
    {
        using var folder = new TemporaryFolder();
        var service = new SourceMetadataCaptureService();
        var ids = Enumerable.Range(0, 100)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        Parallel.ForEach(ids, sourceId =>
        {
            service.PublishIfMissing(
                folder.Path,
                new SourceMetadataCaptureRequest(sourceId, Path.Combine(folder.Path, sourceId + ".missing")));
        });

        var snapshots = new SourceMetadataSnapshotStore().LoadAll(folder.Path);

        Assert.Equal(ids.OrderBy(id => id).ToArray(), snapshots.Select(snapshot => snapshot.SourceId).ToArray());
        Assert.All(snapshots, snapshot => Assert.Empty(snapshot.RawTags));
    }

    private sealed class DiagnosticTagMetadataReader : TagMetadataReader
    {
        public override TagMetadataReadResult Read(string filePath) =>
            new(
                new OriginalTagMetadata
                {
                    RawTagEntries =
                    [
                        new RawTagEntry("DESCRIPTION", ["Beschreibung"], MetadataTagFormat.Xiph, "Xiph")
                    ]
                },
                TechnicalDurationFallback: null,
                TechnicalBitrateKbpsFallback: null,
                TechnicalChannelsFallback: null,
                HasEmbeddedCover: false,
                Failed: false,
                Diagnostics: ["Reader-Hinweis: RawTag wurde gekürzt."]);
    }
}
