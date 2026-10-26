using System.Globalization;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class CoverImageServiceTests
{
    [Theory]
    [InlineData(8193, 1)]
    [InlineData(1, 8193)]
    [InlineData(5000, 5000)]
    public void Cover_dimensions_and_pixel_count_are_rejected_before_full_decoding(int width, int height)
    {
        Assert.Throws<InvalidDataException>(() => CoverInputSafety.ValidateDimensions(width, height));
    }

    [Fact]
    public void Oversized_encoded_cover_is_rejected_before_image_header_parsing()
    {
        using var folder = new TemporaryFolder();
        var source = folder.CreateFile("oversized.png");
        using (var output = File.OpenWrite(source)) output.SetLength(CoverInputSafety.MaxEncodedBytes + 1);
        Assert.Throws<InvalidDataException>(() => new CoverImageService().CreateProcessedCover(source, folder.Path));
        Assert.Empty(Directory.GetFiles(folder.Path, "Cover_*.jpg"));
    }

    [Fact]
    public void Oversized_png_header_is_rejected_without_allocating_full_pixel_buffer()
    {
        using var folder = new TemporaryFolder();
        var source = folder.CreateFile("wide.png");
        using (var image = new Image<Rgba32>(1, 1)) image.SaveAsPng(source);
        var bytes = File.ReadAllBytes(source);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), 100_000);
        // Recompute the IHDR CRC so Identify sees a structurally valid header.
        uint crc = uint.MaxValue;
        foreach (var value in bytes.AsSpan(12, 17))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), ~crc);
        File.WriteAllBytes(source, bytes);
        Assert.Throws<InvalidDataException>(() => new CoverImageService().CreateProcessedCover(source, folder.Path));
    }

    [Fact]
    public void CreateProcessedCover_CreatesManagedJpegAndReportsActualProcessedAsset()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "source.png");
        using (var image = new Image<Rgba32>(120, 80))
            image.SaveAsPng(source);

        var service = new CoverImageService();
        var result = service.CreateProcessedCover(source, folder.Path, "Der Herr der Ringe");

        Assert.True(File.Exists(result.ProcessedJpegPath));
        Assert.Matches(@"^Cover_[0-9a-f]{12}_Der_Herr_der_Ringe\.jpg$", Path.GetFileName(result.ProcessedJpegPath));
        Assert.Equal(120, result.OriginalWidth);
        Assert.Equal(80, result.OriginalHeight);
        Assert.Equal(CoverImageService.DefaultCoverSize, result.TargetSize);

        var info = service.TryGetProcessedCoverInfo(result.ProcessedJpegPath);
        Assert.NotNull(info);
        Assert.Equal("JPEG", info!.Format);
        Assert.Equal(2000, info.Width);
        Assert.Equal(2000, info.Height);
        Assert.True(info.FileSizeBytes > 0);
    }

    [Fact]
    public void QualityWarningFollowsCurrentUiCultureAndCentralResource()
    {
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            using var folder = new TemporaryFolder();
            var source = Path.Combine(folder.Path, "small.png");
            using (var image = new Image<Rgba32>(120, 80))
                image.SaveAsPng(source);

            var result = new CoverImageService().CreateProcessedCover(source, folder.Path, warningThresholdPixels: 1000);

            Assert.Equal(LocalizationText.Format("Cover.QualityWarning", 120, 80), result.Warning);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void EnsureManagedCoverForTitle_CopiesWithoutReencodingAndLeavesLegacyUntouched()
    {
        using var folder = new TemporaryFolder();
        var managed = Path.Combine(folder.Path, "Cover_abcdef123456_Alter_Titel.jpg");
        var payload = Enumerable.Range(0, 512).Select(i => (byte)(i % 251)).ToArray();
        File.WriteAllBytes(managed, payload);

        var service = new CoverImageService();
        var retitled = service.EnsureManagedCoverForTitle(managed, "Neuer Titel");

        Assert.Equal("Cover_abcdef123456_Neuer_Titel.jpg", Path.GetFileName(retitled));
        Assert.True(File.Exists(managed));
        Assert.True(File.Exists(retitled));
        Assert.Equal(payload, File.ReadAllBytes(retitled));

        var legacy = Path.Combine(folder.Path, "foobar_abcdef123456_cover_2000.jpg");
        File.WriteAllBytes(legacy, payload);
        Assert.Equal(legacy, service.EnsureManagedCoverForTitle(legacy, "Anderer Titel"));

        var state = new ProjectMetadataState();
        state.ApplyCoverMetadata("source.png", managed, MetadataOrigin.SourceAggregation);
        state.ApplyCoverMetadata("source.png", retitled, state.CanonicalMetadata.Cover.Origin);
        Assert.Equal(retitled, state.CanonicalMetadata.Cover.ProcessedPath);
        Assert.Equal(MetadataOrigin.SourceAggregation, state.CanonicalMetadata.Cover.Origin);
    }
}
