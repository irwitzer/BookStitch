using System.Text;
using BookStitch.Models;

namespace BookStitch.Services;

public sealed class MetadataNormalizationService
{
    public string NormalizeForComparison(ProjectMetadataField field, string? value)
    {
        var text = NormalizeText(value);
        if (text.Length == 0)
            return string.Empty;

        return field switch
        {
            ProjectMetadataField.ISBN or
            ProjectMetadataField.Barcode or
            ProjectMetadataField.ASIN or
            ProjectMetadataField.CatalogNumbers or
            ProjectMetadataField.MusicBrainzReleaseId => text,
            ProjectMetadataField.Languages or
            ProjectMetadataField.OriginalLanguages or
            ProjectMetadataField.TargetLanguages => text,
            _ => text.ToUpperInvariant()
        };
    }

    public string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value
            .Trim()
            .Normalize(NormalizationForm.FormC);

        normalized = normalized
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'')
            .Replace('\u201A', '\'')
            .Replace('\u201B', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace('\u201E', '"')
            .Replace('\u201F', '"')
            .Replace('\u2010', '-')
            .Replace('\u2011', '-')
            .Replace('\u2012', '-')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u2212', '-');

        var builder = new StringBuilder(normalized.Length);
        var previousWasWhiteSpace = false;
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhiteSpace)
                    builder.Append(' ');

                previousWasWhiteSpace = true;
                continue;
            }

            builder.Append(character);
            previousWasWhiteSpace = false;
        }

        return builder.ToString().Trim();
    }

    public string NormalizeFolderCandidate(string? value) =>
        NormalizeText((value ?? string.Empty).Replace('_', ' '));
}
