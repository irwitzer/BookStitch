using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SourceMetadataSnapshotStoreTests
{
    private readonly SourceMetadataSnapshotStore _store = new();

    [Fact]
    public void SaveAndLoad_RoundtripsSnapshotBySourceId()
    {
        using var folder = new TemporaryFolder();
        var sourceId = Guid.NewGuid();
        var snapshot = CreateSnapshot(sourceId, "Titel", "TXXX:ASIN");

        _store.Save(folder.Path, snapshot);
        var loaded = _store.TryLoad(folder.Path, sourceId);

        Assert.NotNull(loaded);
        Assert.Equal(sourceId, loaded.SourceId);
        Assert.Equal("Titel", loaded.CanonicalFields.Title);
        Assert.Equal(AbridgedStatus.Abridged, loaded.CanonicalFields.AbridgedStatus);
        Assert.Equal(AudioWorkType.Audiobook, loaded.CanonicalFields.AudioWorkType);
        Assert.Equal(new MetadataDate(2026, 1, 31), loaded.CanonicalFields.ReleaseDate);
        Assert.Equal(1997, loaded.CanonicalFields.Year);
        Assert.Equal("TXXX:ASIN", loaded.RawTags[0].OriginalKey);
        Assert.Equal("Kapitel 1", loaded.OriginalChapters[0].Title);
        Assert.Equal("Warnung", loaded.ReadDiagnostics[0]);
    }

    [Fact]
    public void Save_RejectsEmptySourceIdBeforeWriting()
    {
        using var folder = new TemporaryFolder();

        Assert.Throws<InvalidOperationException>(() =>
            _store.Save(folder.Path, CreateSnapshot(Guid.Empty, "Titel", "TITLE")));

        Assert.False(Directory.Exists(ProjectFolderLayout.GetSourceMetadataSourcesFolder(folder.Path)));
    }

    [Fact]
    public void LoadAll_ReconstructsSnapshotSetFromSourceFilesWithoutIndex()
    {
        using var folder = new TemporaryFolder();
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _store.Save(folder.Path, CreateSnapshot(second, "Zweiter", "TITLE"));
        _store.Save(folder.Path, CreateSnapshot(first, "Erster", "TITLE"));

        var snapshots = _store.LoadAll(folder.Path);

        Assert.Equal([first, second], snapshots.Select(snapshot => snapshot.SourceId).ToArray());
        Assert.False(File.Exists(Path.Combine(ProjectFolderLayout.GetMetadataFolder(folder.Path), "source-index.json")));
    }

    [Fact]
    public void ParallelSaves_DoNotLoseSnapshots()
    {
        using var folder = new TemporaryFolder();
        var ids = Enumerable.Range(0, 50)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        Parallel.ForEach(ids, id => _store.Save(folder.Path, CreateSnapshot(id, id.ToString("N"), "TITLE")));

        var snapshots = _store.LoadAll(folder.Path);

        Assert.Equal(ids.OrderBy(id => id).ToArray(), snapshots.Select(snapshot => snapshot.SourceId).ToArray());
    }

    [Fact]
    public void TryLoad_WhenSnapshotIsCorrupt_ThrowsAndDoesNotOverwrite()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        Directory.CreateDirectory(ProjectFolderLayout.GetSourceMetadataSourcesFolder(folder.Path));
        var sourceId = Guid.NewGuid();
        var path = ProjectFolderLayout.GetSourceMetadataSnapshotPath(folder.Path, sourceId);
        const string corruptJson = "{ invalid";
        File.WriteAllText(path, corruptJson);

        Assert.ThrowsAny<Exception>(() => _store.TryLoad(folder.Path, sourceId));
        Assert.Equal(corruptJson, File.ReadAllText(path));
    }

    [Fact]
    public void HasCurrentCoverage_SchemaV2IsStaleAfterDirectYearTransportUpgrade()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        Directory.CreateDirectory(ProjectFolderLayout.GetSourceMetadataSourcesFolder(folder.Path));
        var sourceId = Guid.NewGuid();
        File.WriteAllText(
            ProjectFolderLayout.GetSourceMetadataSnapshotPath(folder.Path, sourceId),
            $$"""
            {
              "schemaVersion": 2,
              "Snapshot": {
                "SourceId": "{{sourceId}}"
              }
            }
            """);

        Assert.False(_store.HasCurrentCoverage(folder.Path, sourceId));
    }

    [Fact]
    public void TryLoad_WhenSchemaVersionIsFuture_ThrowsPersistentSchemaException()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        Directory.CreateDirectory(ProjectFolderLayout.GetSourceMetadataSourcesFolder(folder.Path));
        var sourceId = Guid.NewGuid();
        File.WriteAllText(
            ProjectFolderLayout.GetSourceMetadataSnapshotPath(folder.Path, sourceId),
            """
            {
              "schemaVersion": 99,
              "Snapshot": {}
            }
            """);

        Assert.Throws<PersistentJsonSchemaException>(() => _store.TryLoad(folder.Path, sourceId));
    }

    [Theory]
    [InlineData("{ damaged")]
    [InlineData("{\"schemaVersion\":3,\"Snapshot\":null}")]
    public void LoadAll_Isolates_one_corrupt_snapshot_among_valid_snapshots_without_overwriting_it(string corruptJson)
    {
        using var folder = new TemporaryFolder();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _store.Save(folder.Path, CreateSnapshot(first, "first", "TITLE"));
        _store.Save(folder.Path, CreateSnapshot(second, "second", "TITLE"));
        var damaged = ProjectFolderLayout.GetSourceMetadataSnapshotPath(folder.Path, Guid.NewGuid());
        File.WriteAllText(damaged, corruptJson);
        Assert.Equal(new[] { first, second }.OrderBy(id => id), _store.LoadAll(folder.Path).Select(snapshot => snapshot.SourceId));
        Assert.Equal(corruptJson, File.ReadAllText(damaged));
    }

    [Fact]
    public void LoadAll_Does_not_ignore_future_schema_snapshots()
    {
        using var folder = new TemporaryFolder();
        _store.Save(folder.Path, CreateSnapshot(Guid.NewGuid(), "first", "TITLE"));
        var future = ProjectFolderLayout.GetSourceMetadataSnapshotPath(folder.Path, Guid.NewGuid());
        File.WriteAllText(future, "{\"schemaVersion\":999,\"Snapshot\":{}}");
        Assert.Throws<PersistentJsonSchemaException>(() => _store.LoadAll(folder.Path));
        Assert.True(File.Exists(future));
    }

    private static SourceMetadataSnapshot CreateSnapshot(Guid sourceId, string title, string rawKey) =>
        new()
        {
            SourceId = sourceId,
            CanonicalFields = new SourceMetadataCanonicalFields
            {
                Title = title,
                Authors = ["Autor"],
                AbridgedStatus = AbridgedStatus.Abridged,
                AudioWorkType = AudioWorkType.Audiobook,
                ReleaseDate = new MetadataDate(2026, 1, 31),
                Year = 1997,
                Barcode = "9781234567890",
                CatalogNumbers = ["CAT-1"]
            },
            RawTags =
            [
                new RawTagEntry(rawKey, [title], MetadataTagFormat.Id3v2, "TXXX")
            ],
            OriginalChapters =
            [
                new TrackEmbeddedChapterInfo
                {
                    Title = "Kapitel 1",
                    StartTicks = 0,
                    EndTicks = TimeSpan.FromSeconds(1).Ticks
                }
            ],
            CoverCandidate = new SourceCoverCandidateInfo
            {
                HasEmbeddedCover = true,
                MimeType = "image/jpeg",
                ByteLength = 123
            },
            TechnicalInfo = new SourceTechnicalInfo
            {
                SourcePath = @"D:\Audio\track.mp3",
                Duration = TimeSpan.FromSeconds(1),
                BitrateKbps = 128,
                Channels = 2
            },
            ReadDiagnostics = [" Warnung "]
        };
}
