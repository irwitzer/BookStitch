using System.IO;
using System.Reflection;
using TagLib;
using IoFile = System.IO.File;
using TagLibFile = TagLib.File;

namespace BookStitch.Services;

public sealed class FinalAudioTagData
{
    public string Title { get; init; } = "";
    public string Album { get; init; } = "";
    public string Author { get; init; } = "";
    public string Narrator { get; init; } = "";
    public string Genre { get; init; } = "";
    public string CoverPath { get; init; } = "";
}

public sealed class FinalTagService
{
    public void WriteFinalTags(string audioPath, FinalAudioTagData tagData)
    {
        if (string.IsNullOrWhiteSpace(audioPath))
            throw new ArgumentException("Der Pfad zur Ausgabedatei ist leer.", nameof(audioPath));

        if (!IoFile.Exists(audioPath))
            throw new FileNotFoundException("Die Ausgabedatei wurde nicht gefunden.", audioPath);

        using var file = TagLibFile.Create(audioPath);
        var tag = file.Tag;

        var title = Clean(tagData.Title);
        var album = Clean(tagData.Album);
        var author = Clean(tagData.Author);
        var narrator = Clean(tagData.Narrator);
        var genre = Clean(tagData.Genre);

        if (!string.IsNullOrWhiteSpace(title))
            tag.Title = title;

        if (!string.IsNullOrWhiteSpace(album))
            tag.Album = album;
        else if (!string.IsNullOrWhiteSpace(title))
            tag.Album = title;

        if (!string.IsNullOrWhiteSpace(author))
        {
            tag.Performers = [author];
            tag.AlbumArtists = [author];
        }
        else
        {
            tag.Performers = [];
            tag.AlbumArtists = [];
        }

        tag.Composers = [];

        if (!string.IsNullOrWhiteSpace(genre))
            tag.Genres = [genre];

        tag.Comment = BuildComment(narrator);
        WriteNarratorFieldsIfAvailable(tag, narrator);

        if (IsUsableCover(tagData.CoverPath))
        {
            tag.Pictures =
            [
                new Picture(tagData.CoverPath)
                {
                    Type = PictureType.FrontCover,
                    MimeType = "image/jpeg",
                    Description = "Cover"
                }
            ];
        }

        file.Save();
    }

    private static string Clean(string? value)
    {
        return (value ?? "").Trim();
    }

    private static string BuildComment(string narrator)
    {
        if (string.IsNullOrWhiteSpace(narrator))
            return "";

        return "Sprecher: " + narrator.Trim();
    }

    private static void WriteNarratorFieldsIfAvailable(Tag tag, string narrator)
    {
        if (string.IsNullOrWhiteSpace(narrator))
            return;

        var cleanNarrator = narrator.Trim();
        var narratorText = "Sprecher: " + cleanNarrator;

        // Manche TagLibSharp-Versionen/Container bieten solche Felder direkt an,
        // andere nicht. Reflection hält das absichtlich optional und build-sicher.
        TrySetStringProperty(tag, "Narrator", cleanNarrator);
        TrySetStringProperty(tag, "Description", narratorText);

        // Für M4A/M4B versucht TagLibSharp freie iTunes/MP4-Felder je nach Version
        // über SetText/SetDashBox bereitzustellen. Wenn die Methode nicht existiert,
        // bleibt es still beim bereits sicheren Kommentar-Feld.
        TrySetFreeFormText(tag, "NARRATOR", cleanNarrator);
        TrySetFreeFormText(tag, "Narrator", cleanNarrator);
        TrySetFreeFormText(tag, "DESCRIPTION", narratorText);
        TrySetFreeFormText(tag, "Description", narratorText);
    }

    private static void TrySetStringProperty(Tag tag, string propertyName, string value)
    {
        try
        {
            var property = tag.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);

            if (property is null || !property.CanWrite || property.PropertyType != typeof(string))
                return;

            property.SetValue(tag, value);
        }
        catch
        {
            // Optionales Zusatzfeld. Darf den Export nie abbrechen.
        }
    }

    private static void TrySetFreeFormText(Tag tag, string name, string value)
    {
        TryInvokeFreeFormTextMethod(tag, "SetText", name, value);
        TryInvokeFreeFormTextMethod(tag, "SetDashBox", name, value);
    }

    private static void TryInvokeFreeFormTextMethod(Tag tag, string methodName, string name, string value)
    {
        try
        {
            var methods = tag.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                if (!method.Name.Equals(methodName, StringComparison.Ordinal))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 3)
                    continue;

                if (parameters[0].ParameterType != typeof(string) ||
                    parameters[1].ParameterType != typeof(string) ||
                    parameters[2].ParameterType != typeof(string[]))
                {
                    continue;
                }

                method.Invoke(tag, ["com.apple.iTunes", name, new[] { value }]);
                return;
            }
        }
        catch
        {
            // Optionales Zusatzfeld. Darf den Export nie abbrechen.
        }
    }

    private static bool IsUsableCover(string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath) || !IoFile.Exists(coverPath))
            return false;

        var extension = Path.GetExtension(coverPath).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg";
    }
}
