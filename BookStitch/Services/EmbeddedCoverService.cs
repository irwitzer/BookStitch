using System.IO;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed record EmbeddedCoverExtractionCandidate(
    string AudioFile,
    bool HasCoverEvidence = false,
    string Fingerprint = "");

public sealed class EmbeddedCoverService
{
    public static void RecordUserRemoval(ProjectMetadata metadata, string sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
            metadata.SuppressValue(ProjectMetadataField.Cover, GetCoverFingerprint(sourcePath));
    }

    public static void RecordUserSelection(ProjectMetadata metadata, string sourcePath)
    {
        metadata.UnsuppressValue(ProjectMetadataField.Cover, GetCoverFingerprint(sourcePath));
    }

    private static string GetCoverFingerprint(string sourcePath)
    {
        using var stream = File.OpenRead(sourcePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private const int MaxDistinctInvalidCoverCandidates = 5;
    private readonly TagMetadataReader _tagMetadataReader;

    public EmbeddedCoverService()
        : this(new TagMetadataReader())
    {
    }

    public EmbeddedCoverService(TagMetadataReader tagMetadataReader)
    {
        _tagMetadataReader = tagMetadataReader;
    }

    public string? ExtractFirstValidCover(IEnumerable<string> audioFiles, string targetFolder)
    {
        return ExtractFirstValidCover(
            audioFiles.Select(audioFile => new EmbeddedCoverExtractionCandidate(audioFile)),
            targetFolder);
    }

    public string? ExtractFirstValidCover(IEnumerable<EmbeddedCoverExtractionCandidate> candidates, string targetFolder,
        IReadOnlyCollection<string>? suppressedFingerprints = null)
    {
        var suppressed = (suppressedFingerprints ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(targetFolder);
        var seenEvidenceFingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalidCandidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var audioFile = candidate.AudioFile;
            if (string.IsNullOrWhiteSpace(audioFile) || !File.Exists(audioFile))
                continue;

            var evidenceFingerprint = NormalizeFingerprint(candidate.Fingerprint);
            if (suppressed.Contains(evidenceFingerprint) ||
                (!string.IsNullOrWhiteSpace(evidenceFingerprint) &&
                !seenEvidenceFingerprints.Add(evidenceFingerprint)))
            {
                continue;
            }

            try
            {
                var cover = _tagMetadataReader.ReadFirstEmbeddedCover(audioFile);
                if (cover is null || cover.Bytes.Length == 0 || cover.Bytes.LongLength > CoverInputSafety.MaxEncodedBytes)
                {
                    if (ShouldStopAfterInvalidCandidate(candidate, invalidCandidateKeys))
                        break;

                    continue;
                }

                if (suppressed.Contains(Convert.ToHexString(SHA256.HashData(cover.Bytes))))
                    continue;

                using var input = new MemoryStream(cover.Bytes, writable: false);
                CoverInputSafety.Validate(input);
                using var image = Image.Load(CoverInputSafety.DecoderOptions, input);
                if (image.Width <= 0 || image.Height <= 0)
                {
                    if (ShouldStopAfterInvalidCandidate(candidate, invalidCandidateKeys))
                        break;

                    continue;
                }

                var extension = ResolveExtension(cover.MimeType);
                var hash = Convert.ToHexString(SHA256.HashData(cover.Bytes))[..12].ToLowerInvariant();
                var path = Path.Combine(targetFolder, $"embedded_{hash}{extension}");
                if (!File.Exists(path))
                    File.WriteAllBytes(path, cover.Bytes);

                return path;
            }
            catch
            {
                if (ShouldStopAfterInvalidCandidate(candidate, invalidCandidateKeys))
                    break;
            }
        }

        return null;
    }

    private static bool ShouldStopAfterInvalidCandidate(
        EmbeddedCoverExtractionCandidate candidate,
        ISet<string> invalidCandidateKeys)
    {
        if (!candidate.HasCoverEvidence)
            return false;

        var key = NormalizeFingerprint(candidate.Fingerprint);
        if (string.IsNullOrWhiteSpace(key))
            key = NormalizePath(candidate.AudioFile);

        if (!string.IsNullOrWhiteSpace(key))
            invalidCandidateKeys.Add(key);

        return invalidCandidateKeys.Count >= MaxDistinctInvalidCoverCandidates;
    }

    private static string NormalizeFingerprint(string? fingerprint) =>
        fingerprint?.Trim() ?? string.Empty;

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Trim();
        }
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
