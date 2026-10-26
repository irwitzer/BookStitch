namespace BookStitch.Models;

public sealed class OriginalTagMetadata
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string OriginalTitle { get; init; } = "";
    public string Album { get; init; } = "";
    public string ContentType { get; init; } = "";
    public string Description { get; init; } = "";
    public string Comment { get; init; } = "";
    public IReadOnlyList<string> Keywords { get; init; } = [];

    public IReadOnlyList<string> Authors { get; init; } = [];
    public IReadOnlyList<string> Narrators { get; init; } = [];
    public IReadOnlyList<string> Translators { get; init; } = [];
    public IReadOnlyList<string> Composers { get; init; } = [];
    public IReadOnlyList<string> Performers { get; init; } = [];
    public IReadOnlyList<string> Contributors { get; init; } = [];
    public IReadOnlyList<string> Hosts { get; init; } = [];
    public IReadOnlyList<string> Guests { get; init; } = [];
    public IReadOnlyList<string> Speakers { get; init; } = [];

    public IReadOnlyList<SeriesEntry> Series { get; init; } = [];
    public string Volume { get; init; } = "";
    public string Edition { get; init; } = "";
    public string AbridgedStatus { get; init; } = "";
    public string AudioWorkType { get; init; } = "";
    public uint TrackNumber { get; init; }
    public uint TrackTotal { get; init; }
    public uint DiscNumber { get; init; }
    public uint DiscTotal { get; init; }

    public IReadOnlyList<string> Languages { get; init; } = [];
    public IReadOnlyList<string> OriginalLanguages { get; init; } = [];
    public IReadOnlyList<string> TargetLanguages { get; init; } = [];
    public string Script { get; init; } = "";

    public string Publisher { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public uint Year { get; init; }
    public string Copyright { get; init; } = "";
    public string PhonogramCopyright { get; init; } = "";
    public string License { get; init; } = "";

    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];

    public string ISBN { get; init; } = "";
    public string Barcode { get; init; } = "";
    public string ASIN { get; init; } = "";
    public string ISRC { get; init; } = "";
    public string GUID { get; init; } = "";
    public IReadOnlyList<string> CatalogNumbers { get; init; } = [];
    public string MusicBrainzReleaseId { get; init; } = "";
    public string MusicBrainzRecordingId { get; init; } = "";

    public string SortTitle { get; init; } = "";
    public IReadOnlyList<string> SortAuthors { get; init; } = [];
    public IReadOnlyList<string> SortNarrators { get; init; } = [];
    public string SortAlbum { get; init; } = "";
    public string SortSeries { get; init; } = "";

    public string Website { get; init; } = "";
    public string Source { get; init; } = "";

    public string Encoder { get; init; } = "";
    public string EncodedBy { get; init; } = "";
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RawTags { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RawTagEntry> RawTagEntries { get; init; } = [];
}
