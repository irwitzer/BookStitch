using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BookStitch.Services;

public sealed class CoverImageService
{
    public const int DefaultCoverSize = 2000;
    private const int MinimumRecommendedSourceSize = 500;
    private const int JpegQuality = 88;

    public CoverProcessResult CreateProcessedCover(string sourcePath, string outputFolder)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Es wurde kein Coverpfad übergeben.", nameof(sourcePath));

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Die Coverdatei wurde nicht gefunden.", sourcePath);

        Directory.CreateDirectory(outputFolder);

        using var image = Image.Load(sourcePath);
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
        var safeName = MakeSafeFileName(Path.GetFileNameWithoutExtension(sourcePath));
        var targetPath = Path.Combine(outputFolder, $"{safeName}_{sourceHash}_cover_{DefaultCoverSize}.jpg");

        targetPath = SaveProcessedCover(image, targetPath);

        var warning = BuildQualityWarning(originalWidth, originalHeight);

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

    private static string BuildQualityWarning(int originalWidth, int originalHeight)
    {
        var shortestSide = Math.Min(originalWidth, originalHeight);

        if (shortestSide >= MinimumRecommendedSourceSize)
            return "";

        return $"Das Cover wurde übernommen, die Quelldatei ist aber recht klein.\n\n" +
               $"• Größe: {originalWidth} × {originalHeight} Pixel\n\n" +
               "Das Bild kann im Player etwas unscharf wirken. Du kannst das Cover bis zum Zusammenfügen jederzeit noch ändern.";
    }

    private static string BuildShortHash(string sourcePath)
    {
        var fileInfo = new FileInfo(sourcePath);
        var raw = $"{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }

    private static string MakeSafeFileName(string value)
    {
        var cleaned = string.IsNullOrWhiteSpace(value)
            ? "cover"
            : value.Trim();

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(invalidChar, '_');

        cleaned = string.Join("_", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(cleaned)
            ? "cover"
            : cleaned;
    }
}

public sealed record CoverProcessResult(
    string SourcePath,
    string ProcessedJpegPath,
    int OriginalWidth,
    int OriginalHeight,
    int TargetSize,
    string Warning);
