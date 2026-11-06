using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using TagLib;
using TagLib.Id3v2;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed partial class RealMediaIntegrationTests
{
    [Fact]
    public async Task Repeated_physical_id3_values_consolidate_without_adding_lower_priority_aliases()
    {
        var tools = ResolveFfmpegToolchain(); Assert.NotNull(tools);
        using var folder = new TemporaryFolder();
        var path = System.IO.Path.Combine(folder.Path, "repeated.mp3");
        await RunProcessAsync(tools.FfmpegPath, ["-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "1", "-c:a", "libmp3lame", path]);
        using (var file = TagLib.File.Create(path))
        {
            var tag = Assert.IsType<TagLib.Id3v2.Tag>(file.GetTag(TagTypes.Id3v2, true));
            tag.Version = 4;
            tag.RemoveFrames("TPE2");
            tag.AddFrame(new TextInformationFrame("TPE2") { Text = ["A;B"] });
            tag.AddFrame(new TextInformationFrame("TPE2") { Text = ["C / D"] });
            tag.Performers = ["lower priority artist"];
            tag.AddFrame(new TextInformationFrame("TSOC") { Text = ["Composer;Other"] });
            tag.AddFrame(new UserTextInformationFrame("SERIES") { Text = ["First", "", "Third"] });
            tag.AddFrame(new UserTextInformationFrame("SERIES-PART") { Text = ["1B", "orphan", "3C"] });
            file.Save();
        }
        var before = System.IO.File.ReadAllBytes(path);
        var read = new TagMetadataReader().Read(path);
        Assert.False(read.Failed);
        Assert.Equal(new[] { "A;B", "C / D" }, read.Original.Authors);
        Assert.Equal(new[] { new SeriesEntry("First", "1B"), new SeriesEntry("Third", "3C") }, read.Original.Series);
        var track = Assert.Single(new FolderScanner().Scan(folder.Path));
        var fields = Assert.IsType<SourceMetadataCanonicalFields>(track.SourceMetadataFields);
        Assert.Equal(new[] { "A", "B", "C", "D" }, fields.Authors);
        Assert.Equal(new[] { "Composer", "Other" }, fields.SortComposers);
        var backfill = SourceMetadataCaptureService.BuildSnapshot(new SourceMetadataCaptureRequest(Guid.NewGuid(), path), read, null);
        Assert.Equal(fields.Authors, backfill.CanonicalFields.Authors);
        Assert.Contains(read.Original.RawTagEntries, entry => entry.OriginalKey == "TPE1" && entry.Values.Contains("lower priority artist"));
        Assert.Equal(before, System.IO.File.ReadAllBytes(path));
    }
}
