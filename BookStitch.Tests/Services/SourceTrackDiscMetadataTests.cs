using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Services.TrackOrdering;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SourceTrackDiscMetadataTests
{
    [Fact]
    public void NamedId3TrackAndDiscPairs_AreParsedStrictly()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Id3v2,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["TRCK"] = ["04/09"],
                ["TPOS"] = ["01/03"]
            });

        Assert.Equal((uint)4, metadata.TrackNumber);
        Assert.Equal((uint)9, metadata.TrackTotal);
        Assert.Equal((uint)1, metadata.DiscNumber);
        Assert.Equal((uint)3, metadata.DiscTotal);
    }

    [Theory]
    [InlineData("4/foo", "1/3")]
    [InlineData("1//3", "1/3")]
    [InlineData("abc", "1/3")]
    public void DamagedTrackForms_AreNotSalvaged(string trackValue, string discValue)
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Id3v2,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["TRCK"] = [trackValue],
                ["TPOS"] = [discValue]
            });

        Assert.Equal((uint)0, metadata.TrackNumber);
        Assert.Equal((uint)0, metadata.TrackTotal);
        Assert.Equal((uint)1, metadata.DiscNumber);
        Assert.Equal((uint)3, metadata.DiscTotal);
    }

    [Fact]
    public void XiphExplicitTotals_ArePreservedAsSourceStructure()
    {
        var metadata = TagMetadataReader.CreateFromNamedTagsForTests(
            MetadataTagFormat.Xiph,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["TRACKNUMBER"] = ["4"],
                ["TRACKTOTAL"] = ["9"],
                ["DISCNUMBER"] = ["1"],
                ["DISCTOTAL"] = ["3"]
            });

        Assert.Equal((uint)4, metadata.TrackNumber);
        Assert.Equal((uint)9, metadata.TrackTotal);
        Assert.Equal((uint)1, metadata.DiscNumber);
        Assert.Equal((uint)3, metadata.DiscTotal);
    }

    [Fact]
    public void SourceSnapshotCanonicalFields_KeepTrackDiscSeparateFromProjectOutputMetadata()
    {
        var fields = SourceMetadataCaptureService.BuildCanonicalFields(new OriginalTagMetadata
        {
            TrackNumber = 4,
            TrackTotal = 9,
            DiscNumber = 1,
            DiscTotal = 3,
            Title = "Source title"
        });

        Assert.Equal(4, fields.SourceTrackNumber);
        Assert.Equal(9, fields.SourceTrackTotal);
        Assert.Equal(1, fields.SourceDiscNumber);
        Assert.Equal(3, fields.SourceDiscTotal);
        Assert.Equal("Source title", fields.Title);

        var projectMetadata = new ProjectMetadata();
        Assert.Null(projectMetadata.GetValue<int?>(ProjectMetadataField.OutputTrackNumber).Value);
        Assert.Null(projectMetadata.GetValue<int?>(ProjectMetadataField.OutputTrackTotal).Value);
    }

    [Fact]
    public void TrackOrderInputFactory_UsesOriginalSourceEvidenceNotMergedWorkingNumbers()
    {
        var track = new TrackInfo
        {
            FilePath = @"C:\Book\Track.mp3",
            FileName = "Track.mp3",
            SourceTrackNumber = 4,
            SourceTrackTotal = 9,
            SourceDiscNumber = 1,
            SourceDiscTotal = 3,
            TrackNumber = 44,
            DiscNumber = 11
        };

        var input = Assert.Single(new TrackOrderInputFactory().Create([track]));

        Assert.Equal(4, input.Metadata.TrackNumber);
        Assert.Equal(9, input.Metadata.TrackTotal);
        Assert.Equal(1, input.Metadata.DiscNumber);
        Assert.Equal(3, input.Metadata.DiscTotal);
    }

    [Fact]
    public void Resume_RestoresSourceTrackDiscEvidenceFromImmutableSnapshot()
    {
        using var folder = new TemporaryFolder();
        var sourceId = Guid.NewGuid();
        new SourceMetadataSnapshotStore().Save(
            folder.Path,
            new SourceMetadataSnapshot
            {
                SourceId = sourceId,
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    SourceTrackNumber = 4,
                    SourceTrackTotal = 9,
                    SourceDiscNumber = 1,
                    SourceDiscTotal = 3,
                    Title = "Kapitelquelle"
                }
            });

        var plan = new ProjectResumePlan
        {
            ProjectFolder = folder.Path,
            ProjectType = ProjectManifestTypes.FolderProject,
            SourceFolder = folder.Path,
            Tracks =
            [
                new ProjectResumeTrackItem
                {
                    SourceId = sourceId,
                    TrackIndex = 1,
                    TrackNumber = 44,
                    DiscNumber = 11,
                    SourcePath = Path.Combine(folder.Path, "track.mp3"),
                    SourceFileName = "track.mp3"
                }
            ]
        };

        var result = new ProjectResumeLoadWorkflowService(
            new AudioDiscProjectService(),
            new TrackListStateService()).Prepare(plan);

        Assert.True(result.Success);
        var track = Assert.Single(result.Tracks);
        Assert.Equal(4, track.SourceTrackNumber);
        Assert.Equal(9, track.SourceTrackTotal);
        Assert.Equal(1, track.SourceDiscNumber);
        Assert.Equal(3, track.SourceDiscTotal);
        Assert.Equal(44, track.TrackNumber);
        Assert.Equal(11, track.DiscNumber);
    }

    [Fact]
    public void FolderScanner_KeepsTagSourceNumbersSeparateFromWorkingNumbers()
    {
        using var folder = new TemporaryFolder();
        var sourcePath = Path.Combine(folder.Path, "009 Source.mp3");
        File.WriteAllBytes(sourcePath, [1]);

        var scanner = new FolderScanner(
            new FixedTagMetadataReader(new OriginalTagMetadata
            {
                TrackNumber = 4,
                TrackTotal = 9,
                DiscNumber = 1,
                DiscTotal = 3,
                Title = "4 Der Anfang"
            }),
            new FileSystemMetadataReader());

        var track = Assert.Single(scanner.Scan(folder.Path, TrackNumberPreference.FileName));

        Assert.Equal(4, track.SourceTrackNumber);
        Assert.Equal(9, track.SourceTrackTotal);
        Assert.Equal(1, track.SourceDiscNumber);
        Assert.Equal(3, track.SourceDiscTotal);
        Assert.Equal(9, track.TrackNumber);
        Assert.Empty(track.ChapterTitle);
    }

    private sealed class FixedTagMetadataReader(OriginalTagMetadata metadata) : TagMetadataReader
    {
        public override TagMetadataReadResult Read(string filePath) =>
            new(
                metadata,
                TechnicalDurationFallback: null,
                TechnicalBitrateKbpsFallback: null,
                TechnicalChannelsFallback: null,
                HasEmbeddedCover: false,
                Failed: false);
    }
}
