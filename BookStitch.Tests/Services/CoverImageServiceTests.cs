using System.Globalization;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
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
        File.WriteAllBytes(source, CoverTestImages.Create(1, 1));
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
        File.WriteAllBytes(source, CoverTestImages.Create(900, 600));

        var service = new CoverImageService();
        var result = service.CreateProcessedCover(source, folder.Path, "Der Herr der Ringe");

        Assert.True(File.Exists(result.ProcessedJpegPath));
        Assert.Matches(@"^Cover_[0-9a-f]{12}_Der_Herr_der_Ringe\.jpg$", Path.GetFileName(result.ProcessedJpegPath));
        Assert.Equal(900, result.OriginalWidth);
        Assert.Equal(600, result.OriginalHeight);
        Assert.Equal(1000, result.TargetSize);

        var info = service.TryGetProcessedCoverInfo(result.ProcessedJpegPath);
        Assert.NotNull(info);
        Assert.Equal("JPEG", info!.Format);
        Assert.Equal(1000, info.Width);
        Assert.Equal(1000, info.Height);
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
            File.WriteAllBytes(source, CoverTestImages.Create(900, 600));

            var result = new CoverImageService().CreateProcessedCover(source, folder.Path, warningThresholdPixels: 1000);

            Assert.Equal(LocalizationText.Format("Cover.QualityWarning", 900, 600, 1000), result.Warning);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Theory]
    [InlineData(200, 1000)]
    [InlineData(300, 1000)]
    [InlineData(500, 1000)]
    [InlineData(1499, 1000)]
    [InlineData(1500, 2000)]
    [InlineData(2249, 2000)]
    [InlineData(2250, 3000)]
    public void Automatic_size_follows_usable_square(int side, int expected)
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "cover.png");
        File.WriteAllBytes(source, CoverTestImages.Create(side + 20, side));
        var original = File.ReadAllBytes(source);
        var service = new CoverImageService();
        var result = service.CreateProcessedCover(source, folder.Path);
        Assert.Equal(expected, result.TargetSize);
        Assert.Equal(expected, service.TryGetProcessedCoverInfo(result.ProcessedJpegPath)!.Width);
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Fact]
    public void New_tiny_cover_is_rejected_but_legacy_preview_and_information_remain_available()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "legacy.png");
        File.WriteAllBytes(source, CoverTestImages.Create(120, 80));
        var service = new CoverImageService();
        Assert.Throws<InvalidDataException>(() => service.CreateProcessedCover(source, folder.Path));
        CoverInputSafety.ValidateFile(source);
        Assert.Equal(120, service.TryGetProcessedCoverInfo(source)!.Width);
        Assert.Empty(Directory.GetFiles(folder.Path, "Cover_*.jpg"));
    }

    [Fact]
    public async Task Cancellation_does_not_publish_a_cover()
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "cover.png");
        File.WriteAllBytes(source, CoverTestImages.Create(600, 600));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CoverImageService()
            .CreateProcessedCoverAsync(source, folder.Path, cancellationToken: cts.Token));
        Assert.Empty(Directory.GetFiles(folder.Path, "Cover_*.jpg"));
    }

    [Theory]
    [InlineData("Auto", 255)]
    [InlineData("White", 255)]
    [InlineData("Black", 0)]
    [InlineData("invalid", 255)]
    public void Transparency_preference_controls_new_JPEG_background(string mode, int expected)
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "transparent.png");
        File.WriteAllBytes(source, CoverTestImages.Create(200, 200, alpha: 0));
        var result = new CoverImageService(() => mode).CreateProcessedCover(source, folder.Path);
        using var stream = File.OpenRead(result.ProcessedJpegPath);
        var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        var rgb = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame,
            System.Windows.Media.PixelFormats.Bgr24, null, 0);
        var pixel = new byte[3];
        rgb.CopyPixels(new System.Windows.Int32Rect(500, 500, 1, 1), pixel, 3, 0);
        Assert.All(pixel, component => Assert.InRange((int)component, Math.Max(0, expected - 5), Math.Min(255, expected + 5)));
    }

    [Theory]
    [InlineData(199, 300)]
    [InlineData(300, 199)]
    public void Either_edge_below_200_rejects_new_import(int width, int height)
    {
        using var folder = new TemporaryFolder();
        var source = Path.Combine(folder.Path, "small.png");
        File.WriteAllBytes(source, CoverTestImages.Create(width, height));
        Assert.Throws<InvalidDataException>(() => new CoverImageService().CreateProcessedCover(source, folder.Path));
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
