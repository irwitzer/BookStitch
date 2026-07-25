using System.IO;
using System.Security.Cryptography;
using SixLabors.ImageSharp;

namespace BookStitch.Services;

public sealed class EmbeddedCoverService
{
    public string? ExtractFirstValidCover(IEnumerable<string> audioFiles, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);

        foreach (var audioFile in audioFiles)
        {
            if (string.IsNullOrWhiteSpace(audioFile) || !File.Exists(audioFile))
                continue;

            try
            {
                using var tagFile = TagLib.File.Create(audioFile);
                foreach (var picture in tagFile.Tag.Pictures ?? [])
                {
                    var bytes = picture.Data?.Data;
                    if (bytes is null || bytes.Length == 0)
                        continue;

                    using var image = Image.Load(bytes);
                    if (image.Width <= 0 || image.Height <= 0)
                        continue;

                    var extension = ResolveExtension(picture.MimeType);
                    var hash = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();
                    var path = Path.Combine(targetFolder, $"embedded_{hash}{extension}");
                    if (!File.Exists(path))
                        File.WriteAllBytes(path, bytes);

                    return path;
                }
            }
            catch
            {
                // Ungültige Tags oder Bilder werden übersprungen.
            }
        }

        return null;
    }

    private static string ResolveExtension(string? mimeType)
    {
        return mimeType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
    }
}
