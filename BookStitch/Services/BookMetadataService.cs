using BookStitch.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed class BookMetadataService
{
    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".aac",
        ".m4a",
        ".m4b",
        ".wav",
        ".flac"
    };

    public BookMetadataSuggestion GuessFromFolder(
        string scannedFolderPath,
        string displayFolderPath,
        IReadOnlyCollection<TrackInfo> tracks)
    {
        if (tracks.Count == 0)
            return BookMetadataSuggestion.Empty;

        var tagSuggestion = ReadFromAudioTags(scannedFolderPath);

        var title = !string.IsNullOrWhiteSpace(tagSuggestion.Title)
            ? tagSuggestion.Title
            : GuessTitleFromFolderOrTracks(displayFolderPath, tracks);

        var author = !string.IsNullOrWhiteSpace(tagSuggestion.Author)
            ? tagSuggestion.Author
            : GuessAuthorFromTracks(tracks);

        return new BookMetadataSuggestion(title, author, tagSuggestion.Narrator);
    }

    private static BookMetadataSuggestion ReadFromAudioTags(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return BookMetadataSuggestion.Empty;

        var albums = new List<string>();
        var authors = new List<string>();
        var narrators = new List<string>();

        foreach (var file in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                     .Where(file => SupportedAudioExtensions.Contains(Path.GetExtension(file)))
                     .Take(80))
        {
            try
            {
                using var tagFile = TagLib.File.Create(file);
                AddIfPresent(albums, tagFile.Tag.Album);
                AddIfPresent(authors, tagFile.Tag.FirstAlbumArtist);
                AddIfPresent(authors, tagFile.Tag.FirstPerformer);
                AddIfPresent(narrators, TryExtractNarratorFromComment(tagFile.Tag.Comment));
            }
            catch
            {
                // Einzelne kaputte Tags dürfen die Projekterkennung nicht blockieren.
            }
        }

        return new BookMetadataSuggestion(
            PickDominantValue(albums),
            PickDominantValue(authors),
            PickDominantValue(narrators));
    }

    private static string GuessTitleFromFolderOrTracks(
        string displayFolderPath,
        IReadOnlyCollection<TrackInfo> tracks)
    {
        if (!string.IsNullOrWhiteSpace(displayFolderPath) &&
            !IsDriveRootPath(displayFolderPath))
        {
            var folderName = new DirectoryInfo(displayFolderPath).Name;
            var folderTitle = CleanMetadataGuess(folderName);

            if (!string.IsNullOrWhiteSpace(folderTitle))
                return folderTitle;
        }

        return GuessTitleFromFirstTrack(tracks);
    }

    private static string GuessTitleFromFirstTrack(IReadOnlyCollection<TrackInfo> tracks)
    {
        var firstTrack = tracks
            .OrderBy(track => track.DiscNumber)
            .ThenBy(track => track.TrackNumber)
            .ThenBy(track => track.Index)
            .FirstOrDefault();

        if (firstTrack is null)
            return "";

        var title = !string.IsNullOrWhiteSpace(firstTrack.TagTitle)
            ? firstTrack.TagTitle
            : firstTrack.ChapterTitle;

        return CleanMetadataGuess(title ?? "");
    }

    private static bool IsDriveRootPath(string folderPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(folderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var root = Path.GetPathRoot(fullPath)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return !string.IsNullOrWhiteSpace(root) &&
                   string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GuessAuthorFromTracks(IReadOnlyCollection<TrackInfo> tracks)
    {
        var commonArtist = tracks
            .Select(track => track.Artist)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value)
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();

        if (commonArtist is not null && commonArtist.Count() >= Math.Max(2, tracks.Count / 2))
            return commonArtist.Key;

        return "";
    }

    private static void AddIfPresent(List<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values.Add(value.Trim());
    }

    private static string PickDominantValue(List<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault() ?? "";
    }

    private static string TryExtractNarratorFromComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return "";

        var match = Regex.Match(comment, @"(?i)\b(?:sprecher|narrator|gelesen von)\s*[:\-]\s*(?<name>[^;\r\n]+)");
        return match.Success ? match.Groups["name"].Value.Trim() : "";
    }

    private static string CleanMetadataGuess(string text)
    {
        var cleaned = text.Trim();
        cleaned = cleaned.Replace('_', ' ');
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim();
    }
}

public sealed record BookMetadataSuggestion(string Title, string Author, string Narrator)
{
    public static BookMetadataSuggestion Empty { get; } = new("", "", "");
}
