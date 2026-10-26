using System.Globalization;
using System.IO;
using System.Text;

namespace BookStitch.Services;

public static class CoverFileNamePolicy
{
    public const int MaxTitleSegmentLength = 120;
    private const string Prefix = "Cover_";
    private static readonly HashSet<char> Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*', '\0'];

    public static string NormalizeTitleSegment(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var normalized = title.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        var pendingUnderscore = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingUnderscore = builder.Length > 0;
                continue;
            }

            if (rune.Value <= char.MaxValue)
            {
                var ch = (char)rune.Value;
                var category = char.GetUnicodeCategory(ch);
                if (Forbidden.Contains(ch) || char.IsControl(ch) || category == UnicodeCategory.Format)
                    continue;
            }
            else if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
            {
                continue;
            }

            if (pendingUnderscore && builder.Length > 0 && builder[^1] != '_')
                builder.Append('_');
            pendingUnderscore = false;
            builder.Append(rune.ToString());
            if (builder.Length >= MaxTitleSegmentLength)
                break;
        }

        var result = builder.ToString().Trim('_', ' ', '.');
        while (result.Contains("__", StringComparison.Ordinal))
            result = result.Replace("__", "_", StringComparison.Ordinal);
        if (result.Length <= MaxTitleSegmentLength)
            return result;

        var safeLength = MaxTitleSegmentLength;
        if (safeLength > 0 && safeLength < result.Length && char.IsHighSurrogate(result[safeLength - 1]))
            safeLength--;
        return result[..safeLength].TrimEnd('_', ' ', '.');
    }

    public static string BuildManagedFileName(string hash, string? title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        var segment = NormalizeTitleSegment(title);
        return segment.Length == 0
            ? $"{Prefix}{hash}.jpg"
            : $"{Prefix}{hash}_{segment}.jpg";
    }

    public static bool TryGetManagedHash(string? fileNameOrPath, out string hash)
    {
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
            return false;
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath);
        if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var rest = name[Prefix.Length..];
        var separator = rest.IndexOf('_');
        var candidate = separator >= 0 ? rest[..separator] : rest;
        if (candidate.Length != 12 || candidate.Any(ch => !Uri.IsHexDigit(ch)))
            return false;
        hash = candidate.ToLowerInvariant();
        return true;
    }
}
