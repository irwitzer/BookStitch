using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class SourceMetadataDomainTests
{
    [Fact]
    public void RawTagEntry_PreservesQualifiedNativeKeyAndFormat()
    {
        var entry = new RawTagEntry(
            "TXXX:ASIN",
            ["  B012345678  ", ""],
            MetadataTagFormat.Id3v2,
            "TXXX");

        Assert.Equal("TXXX:ASIN", entry.OriginalKey);
        Assert.Equal(["B012345678"], entry.Values);
        Assert.Equal(MetadataTagFormat.Id3v2, entry.TagFormat);
        Assert.Equal("TXXX", entry.Namespace);
    }

    [Fact]
    public void RawTagEntry_RejectsMissingOriginalKey()
    {
        Assert.Throws<ArgumentException>(() =>
            new RawTagEntry("", ["Value"], MetadataTagFormat.Mp4, "com.apple.iTunes"));
    }

    [Fact]
    public void SourceMetadataSnapshot_RequiresStableSourceIdBeforePublication()
    {
        var snapshot = new SourceMetadataSnapshot();

        Assert.Throws<InvalidOperationException>(() => snapshot.RequireComplete());
    }

    [Fact]
    public void SourceMetadataSnapshot_RequireCompleteDefensivelyCopiesChaptersAndDiagnostics()
    {
        var chapters = new List<TrackEmbeddedChapterInfo>
        {
            new()
            {
                Title = "Kapitel",
                StartTicks = 0,
                EndTicks = TimeSpan.FromSeconds(1).Ticks
            }
        };
        var diagnostics = new List<string> { "  Tag warning  ", "" };
        var snapshot = new SourceMetadataSnapshot
        {
            SourceId = Guid.NewGuid(),
            OriginalChapters = chapters,
            ReadDiagnostics = diagnostics
        };

        var complete = snapshot.RequireComplete();
        chapters[0].Title = "Changed";
        diagnostics[0] = "Changed";

        Assert.Equal("Kapitel", complete.OriginalChapters[0].Title);
        Assert.Equal(["Tag warning"], complete.ReadDiagnostics);
    }

    [Fact]
    public void SourceCanonicalFields_UsesBarcodeAndCatalogNumbersInsteadOfLegacyEanCatalogNumber()
    {
        var fields = new SourceMetadataCanonicalFields
        {
            Barcode = "9781234567890",
            CatalogNumbers = ["ABC-1", "ABC-2"]
        };

        Assert.Equal("9781234567890", fields.Barcode);
        Assert.Equal(["ABC-1", "ABC-2"], fields.CatalogNumbers);
        Assert.DoesNotContain(
            "EAN",
            typeof(SourceMetadataCanonicalFields).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(
            "CatalogNumber",
            typeof(SourceMetadataCanonicalFields).GetProperties().Select(property => property.Name));
    }
}
