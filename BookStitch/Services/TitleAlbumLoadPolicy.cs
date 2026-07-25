namespace BookStitch.Services;

public static class TitleAlbumLoadPolicy
{
    public static TitleAlbumLoadState Resolve(string? title, string? album, bool keepLinked)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        var normalizedAlbum = album?.Trim() ?? string.Empty;

        if (keepLinked)
        {
            var linkedValue = !string.IsNullOrWhiteSpace(normalizedAlbum)
                ? normalizedAlbum
                : normalizedTitle;

            return new TitleAlbumLoadState(linkedValue, linkedValue);
        }

        if (string.IsNullOrWhiteSpace(normalizedAlbum))
            normalizedAlbum = normalizedTitle;

        if (string.IsNullOrWhiteSpace(normalizedTitle))
            normalizedTitle = normalizedAlbum;

        return new TitleAlbumLoadState(normalizedTitle, normalizedAlbum);
    }
}

public sealed record TitleAlbumLoadState(string Title, string Album);
