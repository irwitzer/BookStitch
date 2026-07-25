using System.Collections.Generic;
using System.IO;

namespace BookStitch.Services;

public sealed class OutputFolderLayoutService
{
    public const string LayoutNone = "None";
    public const string LayoutAuthor = "Author";
    public const string LayoutTitle = "Title";
    public const string LayoutAuthorTitleNested = "AuthorTitleNested";
    public const string LayoutAuthorAlbumTitleNested = "AuthorAlbumTitleNested";
    public const string LayoutSeriesAuthorTitleNested = "SeriesAuthorTitleNested";
    public const string LayoutAuthorTitleSingle = "AuthorTitleSingle"; // Alte Einstellung aus Vorversionen, wird nicht mehr als Auswahl angeboten.

    public const string DefaultLayout = LayoutAuthorTitleNested;

    public static IReadOnlyList<string> Layouts { get; } =
    [
        LayoutNone,
        LayoutAuthor,
        LayoutTitle,
        LayoutAuthorTitleNested,
        LayoutAuthorAlbumTitleNested,
        LayoutSeriesAuthorTitleNested
    ];

    public static string NormalizeLayout(string? layout)
    {
        if (string.IsNullOrWhiteSpace(layout))
            return DefaultLayout;

        return Layouts.Contains(layout)
            ? layout
            : DefaultLayout;
    }

    public string BuildOutputPath(
        string? baseOutputFolder,
        string? author,
        string? title,
        string? outputFileName,
        string? layout = DefaultLayout,
        string? album = null,
        string? series = null)
    {
        if (string.IsNullOrWhiteSpace(baseOutputFolder))
            return string.Empty;

        var relativePath = BuildRelativeOutputPath(
            author,
            title,
            outputFileName,
            layout,
            album,
            series);

        return Path.Combine(baseOutputFolder, relativePath);
    }

    public string BuildRelativeOutputPath(
        string? author,
        string? title,
        string? outputFileName,
        string? layout = DefaultLayout,
        string? album = null,
        string? series = null)
    {
        var normalizedLayout = NormalizeLayout(layout);
        var parts = new List<string>();

        switch (normalizedLayout)
        {
            case LayoutNone:
                break;

            case LayoutAuthor:
                parts.Add(CleanFolderSegment(author, "Autor"));
                break;

            case LayoutTitle:
                parts.Add(CleanFolderSegment(title, "Titel"));
                break;

            case LayoutAuthorAlbumTitleNested:
                parts.Add(CleanFolderSegment(author, "Autor"));
                parts.Add(CleanFolderSegment(album, "Album"));
                parts.Add(CleanFolderSegment(title, "Titel"));
                break;

            case LayoutSeriesAuthorTitleNested:
                parts.Add(CleanFolderSegment(series, "Reihe"));
                parts.Add(CleanFolderSegment(author, "Autor"));
                parts.Add(CleanFolderSegment(title, "Titel"));
                break;

            case LayoutAuthorTitleNested:
            default:
                parts.Add(CleanFolderSegment(author, "Autor"));
                parts.Add(CleanFolderSegment(title, "Titel"));
                break;
        }

        parts.Add(CleanFileName(outputFileName));

        return Path.Combine(parts.ToArray());
    }

    public string BuildOutputPath(
        string? baseOutputFolder,
        string? author,
        string? title,
        string? outputFileName,
        bool includeAuthorFolder,
        bool includeTitleFolder)
    {
        var layout = includeAuthorFolder && includeTitleFolder
            ? LayoutAuthorTitleNested
            : includeAuthorFolder
                ? LayoutAuthor
                : includeTitleFolder
                    ? LayoutTitle
                    : LayoutNone;

        return BuildOutputPath(baseOutputFolder, author, title, outputFileName, layout);
    }

    public string BuildRelativeOutputPath(
        string? author,
        string? title,
        string? outputFileName,
        bool includeAuthorFolder,
        bool includeTitleFolder)
    {
        var layout = includeAuthorFolder && includeTitleFolder
            ? LayoutAuthorTitleNested
            : includeAuthorFolder
                ? LayoutAuthor
                : includeTitleFolder
                    ? LayoutTitle
                    : LayoutNone;

        return BuildRelativeOutputPath(author, title, outputFileName, layout);
    }

    private static string CleanFolderSegment(string? value, string fallback)
    {
        var cleaned = FileNameTemplateService.CleanWindowsFileName(value);
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static string CleanFileName(string? value)
    {
        var cleaned = FileNameTemplateService.CleanWindowsFileName(value);
        return string.IsNullOrWhiteSpace(cleaned) ? "Hoerbuch.m4a" : cleaned;
    }
}
