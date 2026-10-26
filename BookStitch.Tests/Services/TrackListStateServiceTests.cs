using Xunit;
using BookStitch.Models;
using BookStitch.Models.TrackOrdering;
using BookStitch.Services;
using BookStitch.Services.TrackOrdering;

namespace BookStitch.Tests.Services;

public sealed class TrackListStateServiceTests
{
    [Fact]
    public void SaveAndApply_PreservesOrderAndExclusions()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var service = new TrackListStateService();
            var first = new TrackInfo { FilePath = Path.Combine(folder, "a.flac"), FileName = "a.flac", ChapterTitle = "001 A", AutomaticOrderPosition = 1 };
            var second = new TrackInfo { FilePath = Path.Combine(folder, "b.flac"), FileName = "b.flac", IsExcluded = true, ExcludedChapterTitle = "002 B", AutomaticOrderPosition = 2 };
            service.Save(folder, [second, first]);

            first.AutomaticOrderPosition = null;
            second.AutomaticOrderPosition = null;
            var applied = service.Apply(folder, [first, second]);

            Assert.Same(second, applied[0]);
            Assert.True(applied[0].IsExcluded);
            Assert.Same(first, applied[1]);
            Assert.Equal(2, second.AutomaticOrderPosition);
            Assert.Equal(1, first.AutomaticOrderPosition);

            var json = File.ReadAllText(ProjectFolderLayout.GetTrackListStatePath(folder));
            Assert.Contains("\"schemaVersion\": 5", json);
            Assert.Contains("\"AutomaticOrderAlgorithmVersion\": 2", json);
            Assert.Contains("\"AutomaticOrder\": 2", json);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Apply_WhenStateFileIsMissing_RebuildsAutomaticOrderAndPersistsStateWithoutChangingCurrentOrder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var service = new TrackListStateService();
            var second = new TrackInfo
            {
                FilePath = Path.Combine(folder, "002 Chapter.mp3"),
                FileName = "002 Chapter.mp3",
                ChapterTitle = "002 Chapter"
            };
            var first = new TrackInfo
            {
                FilePath = Path.Combine(folder, "001 Chapter.mp3"),
                FileName = "001 Chapter.mp3",
                ChapterTitle = "001 Chapter"
            };

            var applied = service.Apply(folder, [second, first], TrackOrderProjectType.LocalFolder);

            Assert.Same(second, applied[0]);
            Assert.Same(first, applied[1]);
            Assert.Equal(2, second.AutomaticOrderPosition);
            Assert.Equal(1, first.AutomaticOrderPosition);
            var statePath = ProjectFolderLayout.GetTrackListStatePath(folder);
            Assert.True(File.Exists(statePath));
            var json = File.ReadAllText(statePath);
            Assert.Contains("\"AutomaticOrder\": 2", json);
            Assert.Contains("\"AutomaticOrder\": 1", json);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Apply_WhenStateDoesNotMatchEveryTrack_RebuildsAutomaticOrderAndPersistsAllTracks()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        ProjectFolderLayout.EnsureProjectFolders(folder);
        try
        {
            var service = new TrackListStateService();
            var first = new TrackInfo
            {
                FilePath = Path.Combine(folder, "001 Chapter.mp3"),
                FileName = "001 Chapter.mp3",
                ChapterTitle = "001 Chapter"
            };
            var second = new TrackInfo
            {
                FilePath = Path.Combine(folder, "002 Chapter.mp3"),
                FileName = "002 Chapter.mp3",
                ChapterTitle = "002 Chapter"
            };
            var firstKey = service.CreateKey(first).Replace("\\", "\\\\");
            File.WriteAllText(
                ProjectFolderLayout.GetTrackListStatePath(folder),
                $$"""
                {
                  "schemaVersion": 5,
                  "AutomaticOrderAlgorithmVersion": 2,
                  "UpdatedUtc": "2026-08-24T10:00:00Z",
                  "Tracks": [
                    {
                      "Key": "{{firstKey}}",
                      "Order": 0,
                      "AutomaticOrder": 1,
                      "IsExcluded": false,
                      "ChapterTitle": "001 Chapter",
                      "ExcludedChapterTitle": ""
                    }
                  ]
                }
                """);

            var applied = service.Apply(folder, [first, second], TrackOrderProjectType.LocalFolder);

            Assert.Same(first, applied[0]);
            Assert.Same(second, applied[1]);
            Assert.Equal(1, first.AutomaticOrderPosition);
            Assert.Equal(2, second.AutomaticOrderPosition);
            var json = File.ReadAllText(ProjectFolderLayout.GetTrackListStatePath(folder));
            var persistedSecondKey = service.CreateKey(second).Replace("\\", "\\\\");
            Assert.Contains(persistedSecondKey, json, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Apply_LegacyStateWithoutSchema_MigratesAfterSuccessfulApply()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        ProjectFolderLayout.EnsureProjectFolders(folder);
        try
        {
            var service = new TrackListStateService();
            var first = new TrackInfo { FilePath = Path.Combine(folder, "a.flac"), FileName = "a.flac" };
            var second = new TrackInfo { FilePath = Path.Combine(folder, "b.flac"), FileName = "b.flac" };
            var key1 = service.CreateKey(first).Replace("\\", "\\\\");
            var key2 = service.CreateKey(second).Replace("\\", "\\\\");
            File.WriteAllText(
                ProjectFolderLayout.GetTrackListStatePath(folder),
                $$"""
                {
                  "UpdatedUtc": "2026-08-09T10:00:00Z",
                  "Tracks": [
                    {
                      "Key": "{{key2}}",
                      "Order": 0,
                      "IsExcluded": true,
                      "ChapterTitle": "",
                      "ExcludedChapterTitle": "002 B"
                    },
                    {
                      "Key": "{{key1}}",
                      "Order": 1,
                      "IsExcluded": false,
                      "ChapterTitle": "001 A",
                      "ExcludedChapterTitle": ""
                    }
                  ]
                }
                """);

            var applied = service.Apply(folder, [first, second]);

            Assert.Same(second, applied[0]);
            Assert.True(second.IsExcluded);
            Assert.Equal("001 A", first.ChapterTitle);
            var migratedJson = File.ReadAllText(ProjectFolderLayout.GetTrackListStatePath(folder));
            Assert.Contains("\"schemaVersion\": 5", migratedJson);
            Assert.Contains("\"AutomaticOrderAlgorithmVersion\": 2", migratedJson);
            Assert.Contains("\"AutomaticOrder\"", migratedJson);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Apply_MissingAutomaticOrder_UsesProjectTypeAndConfirmedContextsWithoutReplacingManualOrder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        ProjectFolderLayout.EnsureProjectFolders(folder);
        try
        {
            var service = new TrackListStateService();
            var discTwo = new TrackInfo
            {
                FilePath = Path.Combine(folder, "originals", "CD 02", "001.mp3"),
                FileName = "001.mp3",
                RelativeFolder = Path.Combine("CD 02"),
                DiscNumber = 2
            };
            var discOne = new TrackInfo
            {
                FilePath = Path.Combine(folder, "originals", "CD 01", "999.mp3"),
                FileName = "999.mp3",
                RelativeFolder = Path.Combine("CD 01"),
                DiscNumber = 1
            };
            var key1 = service.CreateKey(discOne).Replace("\\", "\\\\");
            var key2 = service.CreateKey(discTwo).Replace("\\", "\\\\");
            File.WriteAllText(
                ProjectFolderLayout.GetTrackListStatePath(folder),
                $$"""
                {
                  "schemaVersion": 5,
                  "UpdatedUtc": "2026-08-09T10:00:00Z",
                  "Tracks": [
                    {
                      "Key": "{{key2}}",
                      "Order": 0,
                      "IsExcluded": false,
                      "ChapterTitle": "",
                      "ExcludedChapterTitle": ""
                    },
                    {
                      "Key": "{{key1}}",
                      "Order": 1,
                      "IsExcluded": false,
                      "ChapterTitle": "",
                      "ExcludedChapterTitle": ""
                    }
                  ]
                }
                """);

            var applied = service.Apply(
                folder,
                [discOne, discTwo],
                TrackOrderProjectType.Mp3Disc,
                TrackOrderConfirmedContextFactory.CreateMp3DiscContexts([discOne, discTwo]));

            Assert.Same(discTwo, applied[0]);
            Assert.Same(discOne, applied[1]);
            Assert.Equal(1, discOne.AutomaticOrderPosition);
            Assert.Equal(2, discTwo.AutomaticOrderPosition);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Apply_UnsupportedSchema_RejectsAndLeavesFileUnchanged()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        ProjectFolderLayout.EnsureProjectFolders(folder);
        try
        {
            var path = ProjectFolderLayout.GetTrackListStatePath(folder);
            var json = """
            {
              "schemaVersion": 99,
              "UpdatedUtc": "2026-08-09T10:00:00Z",
              "Tracks": []
            }
            """;
            File.WriteAllText(path, json);

            Assert.Throws<PersistentJsonSchemaException>(() =>
                new TrackListStateService().Apply(folder, []));
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Save_DoesNotPersistOriginalTagMetadata()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var service = new TrackListStateService();
            var original = new TrackInfo
            {
                FilePath = Path.Combine(folder, "source.flac"),
                FileName = "source.flac",
                TagTitle = "Wenn du erzählst, erblüht die Wüste - 001",
                Artist = "Original Author",
                ChapterTitle = "1 Wenn du erzählst, erblüht die Wüste"
            };
            service.Save(folder, [original]);

            var json = File.ReadAllText(ProjectFolderLayout.GetTrackListStatePath(folder));

            Assert.Contains("\"schemaVersion\": 5", json);
            Assert.DoesNotContain("Wenn du erzählst, erblüht die Wüste - 001", json);
            Assert.DoesNotContain("Original Author", json);
            Assert.DoesNotContain("\"TagTitle\"", json);
            Assert.DoesNotContain("\"Artist\"", json);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Apply_LegacyTagMetadataDoesNotOverwriteFreshTrackProjection()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        ProjectFolderLayout.EnsureProjectFolders(folder);
        try
        {
            var service = new TrackListStateService();
            var sourceId = Guid.NewGuid();
            var resumed = new TrackInfo
            {
                SourceId = sourceId,
                FilePath = Path.Combine(folder, "source.flac"),
                FileName = "source.flac",
                TagTitle = "Fresh snapshot title",
                Artist = "Fresh snapshot artist",
                ChapterTitle = ""
            };
            var key = service.CreateKey(resumed).Replace("\\", "\\\\");
            File.WriteAllText(
                ProjectFolderLayout.GetTrackListStatePath(folder),
                $$"""
                {
                  "schemaVersion": 4,
                  "AutomaticOrderAlgorithmVersion": 2,
                  "UpdatedUtc": "2026-08-09T10:00:00Z",
                  "Tracks": [
                    {
                      "Key": "{{key}}",
                      "SourceId": "{{sourceId}}",
                      "Order": 0,
                      "AutomaticOrder": 1,
                      "TagTitle": "Stale legacy title",
                      "Artist": "Stale legacy artist",
                      "IsExcluded": false,
                      "ChapterTitle": "1 Wenn du erzählst",
                      "ExcludedChapterTitle": ""
                    }
                  ]
                }
                """);

            var applied = service.Apply(folder, [resumed]);

            var track = Assert.Single(applied);
            Assert.Equal(sourceId, track.SourceId);
            Assert.Equal("Fresh snapshot title", track.TagTitle);
            Assert.Equal("Fresh snapshot artist", track.Artist);
            Assert.Equal("1 Wenn du erzählst", track.ChapterTitle);

            var migratedJson = File.ReadAllText(ProjectFolderLayout.GetTrackListStatePath(folder));
            Assert.Contains("\"schemaVersion\": 5", migratedJson);
            Assert.DoesNotContain("Stale legacy title", migratedJson);
            Assert.DoesNotContain("Stale legacy artist", migratedJson);
            Assert.DoesNotContain("\"TagTitle\"", migratedJson);
            Assert.DoesNotContain("\"Artist\"", migratedJson);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void SaveAndApply_PreservesSourceIdAndUsesItAsPreferredIdentity()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var sourceId = Guid.NewGuid();
            var service = new TrackListStateService();
            var original = new TrackInfo
            {
                SourceId = sourceId,
                FilePath = Path.Combine(folder, "old", "source.flac"),
                FileName = "source.flac",
                ChapterTitle = "1 Original"
            };
            service.Save(folder, [original]);

            var resumed = new TrackInfo
            {
                SourceId = sourceId,
                FilePath = Path.Combine(folder, "new", "renamed.flac"),
                FileName = "renamed.flac",
                ChapterTitle = "Fresh probe"
            };

            var applied = service.Apply(folder, [resumed]);

            var track = Assert.Single(applied);
            Assert.Equal(sourceId, track.SourceId);
            Assert.Equal("1 Original", track.ChapterTitle);
            var json = File.ReadAllText(ProjectFolderLayout.GetTrackListStatePath(folder));
            Assert.Contains("sourceid:" + sourceId.ToString("N").ToUpperInvariant(), json);
            Assert.Contains(sourceId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Apply_LegacyPathStateCanPopulateSourceId()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        ProjectFolderLayout.EnsureProjectFolders(folder);
        try
        {
            var service = new TrackListStateService();
            var sourceId = Guid.NewGuid();
            var track = new TrackInfo { FilePath = Path.Combine(folder, "a.flac"), FileName = "a.flac" };
            var key = service.CreateKey(track).Replace("\\", "\\\\");
            File.WriteAllText(
                ProjectFolderLayout.GetTrackListStatePath(folder),
                $$"""
                {
                  "schemaVersion": 5,
                  "UpdatedUtc": "2026-08-09T10:00:00Z",
                  "Tracks": [
                    {
                      "Key": "{{key}}",
                      "SourceId": "{{sourceId}}",
                      "Order": 0,
                      "IsExcluded": false,
                      "ChapterTitle": "001 A",
                      "ExcludedChapterTitle": ""
                    }
                  ]
                }
                """);

            var applied = service.Apply(folder, [track]);

            Assert.Equal(sourceId, Assert.Single(applied).SourceId);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Apply_LegacyPathStateDoesNotReplaceExistingSourceId()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        ProjectFolderLayout.EnsureProjectFolders(folder);
        try
        {
            var service = new TrackListStateService();
            var existingSourceId = Guid.NewGuid();
            var staleSourceId = Guid.NewGuid();
            var track = new TrackInfo
            {
                SourceId = existingSourceId,
                FilePath = Path.Combine(folder, "a.flac"),
                FileName = "a.flac"
            };
            var pathKey = "path:" + Path.GetFullPath(track.FilePath).ToUpperInvariant().Replace("\\", "\\\\");
            File.WriteAllText(
                ProjectFolderLayout.GetTrackListStatePath(folder),
                $$"""
                {
                  "schemaVersion": 5,
                  "UpdatedUtc": "2026-08-09T10:00:00Z",
                  "Tracks": [
                    {
                      "Key": "{{pathKey}}",
                      "SourceId": "{{staleSourceId}}",
                      "Order": 0,
                      "IsExcluded": false,
                      "ChapterTitle": "001 A",
                      "ExcludedChapterTitle": ""
                    }
                  ]
                }
                """);

            var applied = service.Apply(folder, [track]);

            Assert.Equal(existingSourceId, Assert.Single(applied).SourceId);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Save_DoesNotPersistEmbeddedChaptersSoResumeUsesFreshProbeData()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var service = new TrackListStateService();
            var track = new TrackInfo
            {
                FilePath = Path.Combine(folder, "container.m4b"),
                FileName = "container.m4b",
                ChapterTitle = "1 Container",
                EmbeddedChapters =
                [
                    new TrackEmbeddedChapterInfo
                    {
                        Title = "Source Chapter",
                        StartTicks = 0,
                        EndTicks = TimeSpan.FromSeconds(1).Ticks
                    }
                ]
            };

            service.Save(folder, [track]);

            var json = File.ReadAllText(ProjectFolderLayout.GetTrackListStatePath(folder));

            Assert.Contains("\"schemaVersion\": 5", json);
            Assert.DoesNotContain("EmbeddedChapters", json);
            Assert.DoesNotContain("Source Chapter", json);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Apply_SchemaTwoState_MigratesToCurrentSchemaAfterSuccessfulApply()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        ProjectFolderLayout.EnsureProjectFolders(folder);
        try
        {
            var service = new TrackListStateService();
            var track = new TrackInfo { FilePath = Path.Combine(folder, "a.flac"), FileName = "a.flac" };
            var key = service.CreateKey(track).Replace("\\", "\\\\");
            File.WriteAllText(
                ProjectFolderLayout.GetTrackListStatePath(folder),
                $$"""
                {
                  "schemaVersion": 2,
                  "AutomaticOrderAlgorithmVersion": 1,
                  "UpdatedUtc": "2026-08-09T10:00:00Z",
                  "Tracks": [
                    {
                      "Key": "{{key}}",
                      "Order": 0,
                      "AutomaticOrder": 1,
                      "IsExcluded": false,
                      "ChapterTitle": "001 A",
                      "ExcludedChapterTitle": ""
                    }
                  ]
                }
                """);

            service.Apply(folder, [track]);

            var migratedJson = File.ReadAllText(ProjectFolderLayout.GetTrackListStatePath(folder));
            Assert.Contains("\"schemaVersion\": 5", migratedJson);
            Assert.DoesNotContain("\"TagTitle\"", migratedJson);
            Assert.DoesNotContain("\"Artist\"", migratedJson);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Apply_InvalidLegacyJson_DoesNotRewrite()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BookStitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        ProjectFolderLayout.EnsureProjectFolders(folder);
        try
        {
            var path = ProjectFolderLayout.GetTrackListStatePath(folder);
            const string json = "{ invalid";
            File.WriteAllText(path, json);

            var track = new TrackInfo { FilePath = Path.Combine(folder, "a.flac"), FileName = "a.flac" };
            var applied = new TrackListStateService().Apply(folder, [track]);

            Assert.Same(track, Assert.Single(applied));
            Assert.Equal(json, File.ReadAllText(path));
        }
        finally { Directory.Delete(folder, true); }
    }
}
