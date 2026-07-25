using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public static class FileNameTemplateService
{
    public static string BuildOutputFileName(
        string? title,
        string? author,
        string? narrator,
        string? template,
        string? extension,
        string? album = null,
        string? series = null)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "Titel" : title.Trim();
        var safeAuthor = string.IsNullOrWhiteSpace(author) ? "Autor" : author.Trim();
        var safeNarrator = string.IsNullOrWhiteSpace(narrator) ? string.Empty : narrator.Trim();
        var safeAlbum = string.IsNullOrWhiteSpace(album) ? string.Empty : album.Trim();
        var safeSeries = string.IsNullOrWhiteSpace(series) ? string.Empty : series.Trim();

        var fileName = ApplyTemplate(
            template,
            safeTitle,
            safeAuthor,
            safeNarrator,
            safeAlbum,
            safeSeries);

        fileName = CleanWindowsFileName(fileName);

        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "Hoerbuch";

        return fileName + (extension ?? "");
    }

    public static string ApplyTemplate(
        string? template,
        string title,
        string author,
        string narrator,
        string album = "",
        string series = "")
    {
        var normalizedTemplate = string.IsNullOrWhiteSpace(template)
            ? "{Autor} - {Titel}"
            : template;

        var result = normalizedTemplate
            .Replace("{Titel}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{Autor}", author, StringComparison.OrdinalIgnoreCase)
            .Replace("{Sprecher}", narrator, StringComparison.OrdinalIgnoreCase)
            .Replace("{Album}", album, StringComparison.OrdinalIgnoreCase)
            .Replace("{Reihe}", series, StringComparison.OrdinalIgnoreCase);

        // Leere optionale Metadaten dürfen keine doppelten oder führenden Trennzeichen hinterlassen.
        result = Regex.Replace(result, @"\s*-\s*-\s*", " - ");
        result = Regex.Replace(result, @"^\s*-\s*|\s*-\s*$", string.Empty);
        return Regex.Replace(result, @"\s+", " ").Trim();
    }

    public static string CleanWindowsFileName(string? text)
    {
        var cleaned = (text ?? "").Trim();

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(invalidChar, ' ');

        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        cleaned = Regex.Replace(cleaned, @"\s+-\s+", " - ");
        cleaned = cleaned.Trim(' ', '.', '-');

        return cleaned;
    }
}
