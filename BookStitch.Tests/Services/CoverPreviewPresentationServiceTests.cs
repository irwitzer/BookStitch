using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class CoverPreviewPresentationServiceTests
{
    [Fact]
    public void Oversized_existing_cover_uses_placeholder_instead_of_triggering_wpf_decoding()
    {
        using var folder = new BookStitch.Tests.TestHelpers.TemporaryFolder();
        var path = folder.CreateFile("oversized.jpg");
        using (var output = File.OpenWrite(path)) output.SetLength(CoverInputSafety.MaxEncodedBytes + 1);
        Assert.Equal("placeholder.png", CoverPreviewPresentationService.NormalizeSource(path, "placeholder.png"));
    }

    [Theory]
    [InlineData(null, "/default.png")]
    [InlineData("", "/default.png")]
    [InlineData("   ", "/default.png")]
    [InlineData("/cover.png", "/cover.png")]
    public void NormalizeSource_UsesDefaultForMissingValues(
        string? value,
        string expected)
    {
        var result = CoverPreviewPresentationService.NormalizeSource(
            value,
            "/default.png");

        Assert.Equal(expected, result);
    }



    [Theory]
    [InlineData(null, true, true)]
    [InlineData("", true, true)]
    [InlineData("   ", true, true)]
    [InlineData("/processed.png", true, false)]
    [InlineData(null, false, false)]
    public void IsHintVisible_RequiresMissingProcessedCoverAndEditingPermission(
        string? processedCoverPath,
        bool canChangeBookMetadata,
        bool expected)
    {
        var result = CoverPreviewPresentationService.IsHintVisible(
            processedCoverPath,
            canChangeBookMetadata);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData("   ", true, false)]
    [InlineData("/processed.png", false, false)]
    [InlineData("/processed.png", true, true)]
    public void CanRemove_RequiresProcessedCoverAndEditingPermission(
        string? processedCoverPath,
        bool canChangeBookMetadata,
        bool expected)
    {
        Assert.Equal(
            expected,
            CoverPreviewPresentationService.CanRemove(
                processedCoverPath,
                canChangeBookMetadata));
    }

    [Fact]
    public void NormalizeSource_UsesCurrentModeForPlaceholdersAndPreservesRealCovers()
    {
        var dark = CoverPreviewPresentationService.GetPlaceholderSource(AppearanceMode.Dark);
        var light = CoverPreviewPresentationService.GetPlaceholderSource(AppearanceMode.Light);

        Assert.EndsWith("CoverPlacHolder Old (1).png", dark, StringComparison.Ordinal);
        Assert.EndsWith("CoverPlacHolder Old (2).png", light, StringComparison.Ordinal);

        Assert.Equal(light, CoverPreviewPresentationService.NormalizeSource(null, AppearanceMode.Light));
        Assert.Equal(light, CoverPreviewPresentationService.NormalizeSource(dark, AppearanceMode.Light));
        Assert.Equal(dark, CoverPreviewPresentationService.NormalizeSource(light, AppearanceMode.Dark));
        Assert.Equal(@"C:\Books\cover.jpg", CoverPreviewPresentationService.NormalizeSource(@"C:\Books\cover.jpg", AppearanceMode.Light));
    }
}
