using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BookStitch.Services;

public sealed class CoverImageService
{
    public const int DefaultCoverSize = 2000;
    private const int JpegQuality = 88;

    public CoverProcessResult CreateProcessedCover(
        string sourcePath,
        string outputFolder,
        int warningThresholdPixels = CoverWarningSettingsService.DefaultThresholdPixels) =>
        CreateProcessedCover(sourcePath, outputFolder, titleHint: null, warningThresholdPixels);

    public CoverProcessResult CreateProcessedCover(
        string sourcePath,
        string outputFolder,
        string? titleHint,
        int warningThresholdPixels = CoverWarningSettingsService.DefaultThresholdPixels)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Es wurde kein Coverpfad übergeben.", nameof(sourcePath));

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Die Coverdatei wurde nicht gefunden.", sourcePath);

        Directory.CreateDirectory(outputFolder);

        using var input = File.OpenRead(sourcePath);
        CoverInputSafety.Validate(input);
        using var image = Image.Load(CoverInputSafety.DecoderOptions, input);
        var originalWidth = image.Width;
        var originalHeight = image.Height;

        image.Mutate(context => context.AutoOrient());

        var cropSide = Math.Min(image.Width, image.Height);
        var cropX = (image.Width - cropSide) / 2;
        var cropY = (image.Height - cropSide) / 2;

        image.Mutate(context => context
            .Crop(new Rectangle(cropX, cropY, cropSide, cropSide))
            .Resize(new ResizeOptions
            {
                Size = new Size(DefaultCoverSize, DefaultCoverSize),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Lanczos3
            }));

        var sourceHash = BuildShortHash(sourcePath);
        var targetPath = Path.Combine(outputFolder, CoverFileNamePolicy.BuildManagedFileName(sourceHash, titleHint));

        targetPath = SaveProcessedCover(image, targetPath);

        var warning = BuildQualityWarning(originalWidth, originalHeight, warningThresholdPixels);

        return new CoverProcessResult(
            SourcePath: sourcePath,
            ProcessedJpegPath: targetPath,
            OriginalWidth: originalWidth,
            OriginalHeight: originalHeight,
            TargetSize: DefaultCoverSize,
            Warning: warning);
    }


    private static string SaveProcessedCover(Image image, string preferredTargetPath)
    {
        var encoder = new JpegEncoder
        {
            Quality = JpegQuality
        };

        try
        {
            image.SaveAsJpeg(preferredTargetPath, encoder);
            return preferredTargetPath;
        }
        catch (IOException)
        {
            var folder = Path.GetDirectoryName(preferredTargetPath) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(preferredTargetPath);
            var extension = Path.GetExtension(preferredTargetPath);

            for (var suffix = 2; suffix <= 100; suffix++)
            {
                var alternativePath = Path.Combine(folder, $"{stem}_{suffix}{extension}");
                if (File.Exists(alternativePath))
                    continue;

                image.SaveAsJpeg(alternativePath, encoder);
                return alternativePath;
            }

            throw;
        }
    }

    private static string BuildQualityWarning(
        int originalWidth,
        int originalHeight,
        int warningThresholdPixels)
    {
        var threshold = CoverWarningSettingsService.NormalizeThresholdPixels(warningThresholdPixels);
        if (threshold == CoverWarningSettingsService.Disabled)
            return "";

        var shortestSide = Math.Min(originalWidth, originalHeight);

        if (shortestSide >= threshold)
            return "";

        return LocalizationText.Format("Cover.QualityWarning", originalWidth, originalHeight);
    }

    private static string BuildShortHash(string sourcePath)
    {
        var fileInfo = new FileInfo(sourcePath);
        var raw = $"{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    public CoverProcessedAssetInfo? TryGetProcessedCoverInfo(string? processedPath)
    {
        if (string.IsNullOrWhiteSpace(processedPath) || !File.Exists(processedPath))
            return null;

        try
        {
            CoverInputSafety.ValidateFile(processedPath);
            var info = Image.Identify(CoverInputSafety.DecoderOptions, processedPath);
            var format = Image.DetectFormat(processedPath);
            if (info is null || format is null)
                return null;

            return new CoverProcessedAssetInfo(
                Path.GetFileName(processedPath),
                NormalizeFormatName(format),
                info.Width,
                info.Height,
                new FileInfo(processedPath).Length);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string EnsureManagedCoverForTitle(string? processedPath, string? title)
    {
        if (string.IsNullOrWhiteSpace(processedPath) || !File.Exists(processedPath) ||
            !CoverFileNamePolicy.TryGetManagedHash(processedPath, out var hash))
        {
            return processedPath ?? string.Empty;
        }

        var folder = Path.GetDirectoryName(processedPath) ?? string.Empty;
        var desired = Path.Combine(folder, CoverFileNamePolicy.BuildManagedFileName(hash, title));
        if (Path.GetFullPath(desired).Equals(Path.GetFullPath(processedPath), StringComparison.OrdinalIgnoreCase))
            return processedPath;

        var candidate = desired;
        if (File.Exists(candidate))
        {
            if (FilesAreIdentical(processedPath, candidate))
                return candidate;

            var stem = Path.GetFileNameWithoutExtension(desired);
            string? availableCandidate = null;
            for (var suffix = 2; suffix <= 100; suffix++)
            {
                candidate = Path.Combine(folder, $"{stem}_{suffix}.jpg");
                if (!File.Exists(candidate))
                {
                    availableCandidate = candidate;
                    break;
                }
                if (FilesAreIdentical(processedPath, candidate))
                    return candidate;
            }

            if (availableCandidate is null)
                throw new IOException("Für das verwaltete Cover konnte kein kollisionsfreier Dateiname gebildet werden.");
            candidate = availableCandidate;
        }

        File.Copy(processedPath, candidate, overwrite: false);
        return candidate;
    }

    private static bool FilesAreIdentical(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
            return false;
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return SHA256.HashData(leftStream).AsSpan().SequenceEqual(SHA256.HashData(rightStream));
    }

    private static string NormalizeFormatName(IImageFormat format) =>
        format.Name.Equals("JPG", StringComparison.OrdinalIgnoreCase) || format.Name.Equals("JPEG", StringComparison.OrdinalIgnoreCase)
            ? "JPEG"
            : format.Name.ToUpperInvariant();

}

public sealed record CoverProcessedAssetInfo(
    string FileName,
    string Format,
    int Width,
    int Height,
    long FileSizeBytes);

public sealed record CoverProcessResult(
    string SourcePath,
    string ProcessedJpegPath,
    int OriginalWidth,
    int OriginalHeight,
    int TargetSize,
    string Warning);
