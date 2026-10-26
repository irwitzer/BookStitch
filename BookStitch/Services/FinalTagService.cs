using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BookStitch.Models;
using TagLib;
using TagLib.Mpeg4;
using IoFile = System.IO.File;
using TagLibFile = TagLib.File;

namespace BookStitch.Services;

public sealed class FinalAudioTagData
{
    public long? ProjectMetadataRevision { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string OriginalTitle { get; init; } = "";
    public string Album { get; init; } = "";
    public string Author { get; init; } = "";
    public IReadOnlyList<string> Authors { get; init; } = [];
    public string Narrator { get; init; } = "";
    public IReadOnlyList<string> Narrators { get; init; } = [];
    public IReadOnlyList<string> Translators { get; init; } = [];
    public IReadOnlyList<string> Composers { get; init; } = [];
    public IReadOnlyList<string> Performers { get; init; } = [];
    public IReadOnlyList<string> Contributors { get; init; } = [];
    public IReadOnlyList<string> Hosts { get; init; } = [];
    public IReadOnlyList<string> Guests { get; init; } = [];
    public IReadOnlyList<string> Speakers { get; init; } = [];
    public string Genre { get; init; } = "";
    public IReadOnlyList<string> Genres { get; init; } = [];
    public string Description { get; init; } = "";
    public string Comment { get; init; } = "";
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public string ContentType { get; init; } = "";
    public IReadOnlyList<SeriesEntry> Series { get; init; } = [];
    public string Volume { get; init; } = "";
    public string Edition { get; init; } = "";
    public AbridgedStatus? AbridgedStatus { get; init; }
    public AudioWorkType? AudioWorkType { get; init; }
    public IReadOnlyList<string> Languages { get; init; } = [];
    public IReadOnlyList<string> OriginalLanguages { get; init; } = [];
    public IReadOnlyList<string> TargetLanguages { get; init; } = [];
    public string Script { get; init; } = "";
    public string Publisher { get; init; } = "";
    public MetadataDate? ReleaseDate { get; init; }
    public int? Year { get; init; }
    public string Copyright { get; init; } = "";
    public string PhonogramCopyright { get; init; } = "";
    public string License { get; init; } = "";
    public IReadOnlyList<string> Categories { get; init; } = [];
    public string ISBN { get; init; } = "";
    public string Barcode { get; init; } = "";
    public string ASIN { get; init; } = "";
    public IReadOnlyList<string> CatalogNumbers { get; init; } = [];
    public string MusicBrainzReleaseId { get; init; } = "";
    public string SortTitle { get; init; } = "";
    public IReadOnlyList<string> SortAuthors { get; init; } = [];
    public IReadOnlyList<string> SortNarrators { get; init; } = [];
    public string SortAlbum { get; init; } = "";
    public string SortSeries { get; init; } = "";
    public string Website { get; init; } = "";
    public string Source { get; init; } = "";
    public int? OutputTrackNumber { get; init; }
    public int? OutputTrackTotal { get; init; }
    public TimeSpan? Duration { get; init; }
    public int? ChapterCount { get; init; }
    public string CoverPath { get; init; } = "";
    public bool CompatibilityDescriptionEnabled { get; init; } = true;
    public string OutputPresentationCulture { get; init; } = "";
    public IReadOnlySet<ProjectMetadataField> DisabledFields { get; init; } = new HashSet<ProjectMetadataField>();

    public bool IsFieldEnabled(ProjectMetadataField field) => !DisabledFields.Contains(field);

    public FinalAudioTagData WithDuration(TimeSpan? duration) => WithFinalTechnicalData(duration, ChapterCount);

    public FinalAudioTagData WithFinalTechnicalData(TimeSpan? duration, int? chapterCount) => new()
    {
        ProjectMetadataRevision = ProjectMetadataRevision,
        Title = Title,
        Subtitle = Subtitle,
        OriginalTitle = OriginalTitle,
        Album = Album,
        Author = Author,
        Authors = Authors.ToArray(),
        Narrator = Narrator,
        Narrators = Narrators.ToArray(),
        Translators = Translators.ToArray(),
        Composers = Composers.ToArray(),
        Performers = Performers.ToArray(),
        Contributors = Contributors.ToArray(),
        Hosts = Hosts.ToArray(),
        Guests = Guests.ToArray(),
        Speakers = Speakers.ToArray(),
        Genre = Genre,
        Genres = Genres.ToArray(),
        Description = Description,
        Comment = Comment,
        Keywords = Keywords.ToArray(),
        ContentType = ContentType,
        Series = Series.Select(item => new SeriesEntry(item.Name, item.Part)).ToArray(),
        Volume = Volume,
        Edition = Edition,
        AbridgedStatus = AbridgedStatus,
        AudioWorkType = AudioWorkType,
        Languages = Languages.ToArray(),
        OriginalLanguages = OriginalLanguages.ToArray(),
        TargetLanguages = TargetLanguages.ToArray(),
        Script = Script,
        Publisher = Publisher,
        ReleaseDate = ReleaseDate,
        Year = Year,
        Copyright = Copyright,
        PhonogramCopyright = PhonogramCopyright,
        License = License,
        Categories = Categories.ToArray(),
        ISBN = ISBN,
        Barcode = Barcode,
        ASIN = ASIN,
        CatalogNumbers = CatalogNumbers.ToArray(),
        MusicBrainzReleaseId = MusicBrainzReleaseId,
        SortTitle = SortTitle,
        SortAuthors = SortAuthors.ToArray(),
        SortNarrators = SortNarrators.ToArray(),
        SortAlbum = SortAlbum,
        SortSeries = SortSeries,
        Website = Website,
        Source = Source,
        OutputTrackNumber = OutputTrackNumber,
        OutputTrackTotal = OutputTrackTotal,
        Duration = duration,
        ChapterCount = chapterCount,
        CoverPath = CoverPath,
        CompatibilityDescriptionEnabled = CompatibilityDescriptionEnabled,
        OutputPresentationCulture = OutputPresentationCulture,
        DisabledFields = DisabledFields.ToHashSet()
    };
}

public interface IFinalTagService
{
    void WriteFinalTags(string audioPath, FinalAudioTagData tagData);
}

public sealed class FinalTagService : IFinalTagService
{
    public void WriteFinalTags(string audioPath, FinalAudioTagData tagData)
    {
        if (string.IsNullOrWhiteSpace(audioPath))
            throw new ArgumentException("Der Pfad zur Ausgabedatei ist leer.", nameof(audioPath));

        if (!IoFile.Exists(audioPath))
            throw new FileNotFoundException(LocalizationText.Get("Output.FileMissing"), audioPath);

        Mp4ChapterListPreservation? chapters = null;
        using (var file = TagLibFile.Create(audioPath))
        {
            var tag = file.GetTag(TagTypes.Apple) as AppleTag ?? file.Tag as AppleTag;
            if (tag is not null)
                new Mp4MetadataWriter().Write(new AppleTagMetadataSink(tag), tagData);
            else
                WriteGenericTags(file.Tag, tagData);

            // Capture the current FFmpeg output immediately before the first write.
            // Disposal releases TagLib's handles before independent restoration/verification.
            if (file is TagLib.Mpeg4.File)
                chapters = Mp4ChapterListPreservation.Capture(audioPath);
            file.Save();
        }
        chapters?.RestoreAndVerify();
    }

    private static void WriteGenericTags(Tag tag, FinalAudioTagData tagData)
    {
        var title = Clean(tagData.Title);
        var album = Clean(tagData.Album);
        var authors = CleanList(tagData.Authors);
        if (authors.Count == 0)
            authors = CleanScalarAsList(tagData.Author);

        var narrators = CleanList(tagData.Narrators);
        if (narrators.Count == 0)
            narrators = CleanScalarAsList(tagData.Narrator);

        var genres = CleanList(tagData.Genres);
        if (genres.Count == 0)
            genres = CleanScalarAsList(tagData.Genre);

        if (!string.IsNullOrWhiteSpace(title))
            tag.Title = title;

        if (!string.IsNullOrWhiteSpace(album))
            tag.Album = album;
        else if (!string.IsNullOrWhiteSpace(title))
            tag.Album = title;

        if (authors.Count > 0)
        {
            tag.Performers = authors.ToArray();
            tag.AlbumArtists = authors.ToArray();
        }
        else
        {
            tag.Performers = [];
            tag.AlbumArtists = [];
        }

        tag.Composers = tagData.IsFieldEnabled(ProjectMetadataField.Composers)
            ? CleanList(tagData.Composers).ToArray()
            : [];

        if (genres.Count > 0)
            tag.Genres = genres.ToArray();

        tag.Comment = BuildComment(narrators);

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
    }

    private static string Clean(string? value)
    {
        return (value ?? "").Trim();
    }

    private static string BuildComment(IReadOnlyList<string> narrators)
    {
        if (narrators.Count == 0)
            return "";

        return "Sprecher: " + string.Join(", ", narrators);
    }

    internal static IReadOnlyList<string> CleanList(IEnumerable<string>? values)
    {
        return values?
            .Select(Clean)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray() ?? [];
    }

    private static IReadOnlyList<string> CleanScalarAsList(string? value)
    {
        var clean = Clean(value);
        return string.IsNullOrWhiteSpace(clean) ? [] : [clean];
    }

    private static bool IsUsableCover(string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath) || !IoFile.Exists(coverPath))
            return false;

        var extension = Path.GetExtension(coverPath).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg";
    }
}

public sealed class Mp4MetadataWriter
{
    public void Write(IMp4MetadataSink sink, FinalAudioTagData data)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(data);

        ClearOwnedFields(sink);
        sink.ClearSourceOnlyFields();
        sink.SetStikAudiobook();

        var title = Clean(data.Title);
        var album = Clean(data.Album);
        var authors = Values(data.Authors, data.Author);
        var narrators = Values(data.Narrators, data.Narrator);
        var genres = Values(data.Genres, data.Genre);
        var primarySeries = CleanSeries(data.Series).FirstOrDefault();

        sink.Title = ActiveString(data, ProjectMetadataField.Title, title);
        sink.Subtitle = ActiveString(data, ProjectMetadataField.Subtitle, data.Subtitle);
        sink.Album = ActiveString(data, ProjectMetadataField.Album, string.IsNullOrWhiteSpace(album) ? title : album);
        sink.AlbumArtists = ActiveList(data, ProjectMetadataField.Authors, authors);
        sink.Performers = ActiveList(data, ProjectMetadataField.Authors, authors);
        sink.Composers = ActiveList(data, ProjectMetadataField.Composers, data.Composers);
        sink.Genres = ActiveList(data, ProjectMetadataField.Genres, genres);
        sink.Publisher = ActiveString(data, ProjectMetadataField.Publisher, data.Publisher);
        sink.Copyright = ActiveString(data, ProjectMetadataField.Copyright, data.Copyright);
        sink.TitleSort = ActiveString(data, ProjectMetadataField.SortTitle, data.SortTitle);
        sink.AlbumSort = ActiveString(data, ProjectMetadataField.SortAlbum, data.SortAlbum);
        sink.AlbumArtistsSort = ActiveList(data, ProjectMetadataField.SortAuthors, data.SortAuthors);
        sink.PerformersSort = ActiveList(data, ProjectMetadataField.SortAuthors, data.SortAuthors);
        sink.Year = data.IsFieldEnabled(ProjectMetadataField.Year) ? SafeYear(data.Year) : 0;

        if (data.IsFieldEnabled(ProjectMetadataField.OutputTrackNumber) &&
            data.OutputTrackNumber is > 0)
        {
            sink.Track = (uint)data.OutputTrackNumber.Value;
            sink.TrackCount = data.IsFieldEnabled(ProjectMetadataField.OutputTrackTotal) && data.OutputTrackTotal is > 0
                ? (uint)data.OutputTrackTotal.Value
                : 0;
        }

        sink.SetTextAtom("©nrt", ActiveList(data, ProjectMetadataField.Narrators, narrators));
        sink.SetTextAtom("©mvn", ActiveSeriesText(data, primarySeries?.Name));
        sink.SetTextAtom("©mvi", ActiveSeriesPartInteger(data, primarySeries?.Part));

        SetFreeform(sink, data, ProjectMetadataField.Subtitle, "SUBTITLE", data.Subtitle);
        SetFreeform(sink, data, ProjectMetadataField.OriginalTitle, "ORIGINALTITLE", data.OriginalTitle);
        SetFreeform(sink, data, ProjectMetadataField.Narrators, "NARRATOR", narrators);
        SetFreeform(sink, data, ProjectMetadataField.Languages, "LANGUAGE", data.Languages);
        SetFreeform(sink, data, ProjectMetadataField.OriginalLanguages, "ORIGINALLANGUAGE", data.OriginalLanguages);
        SetFreeform(sink, data, ProjectMetadataField.TargetLanguages, "TARGETLANGUAGE", data.TargetLanguages);
        SetFreeform(sink, data, ProjectMetadataField.Series, "SERIES", primarySeries?.Name);
        SetFreeform(sink, data, ProjectMetadataField.Series, "SERIES-PART", primarySeries?.Part);
        SetFreeform(sink, data, ProjectMetadataField.SortSeries, "SERIESSORT", data.SortSeries);
        SetFreeform(sink, data, ProjectMetadataField.ISBN, "ISBN", data.ISBN);
        SetFreeform(sink, data, ProjectMetadataField.Barcode, "BARCODE", data.Barcode);
        SetFreeform(sink, data, ProjectMetadataField.ASIN, "ASIN", data.ASIN);
        SetFreeform(sink, data, ProjectMetadataField.CatalogNumbers, "CATALOGNUMBER", data.CatalogNumbers);
        SetFreeform(sink, data, ProjectMetadataField.ReleaseDate, "RELEASEDATE", data.ReleaseDate?.ToString());
        SetFreeform(sink, data, ProjectMetadataField.AbridgedStatus, "ABRIDGED", Display(data.AbridgedStatus));
        SetFreeform(sink, data, ProjectMetadataField.AudioWorkType, "AUDIOWORKTYPE", Display(data.AudioWorkType));
        SetFreeform(sink, data, ProjectMetadataField.ContentType, "CONTENTTYPE", data.ContentType);
        SetFreeform(sink, data, ProjectMetadataField.Edition, "EDITION", data.Edition);
        SetFreeform(sink, data, ProjectMetadataField.Volume, "VOLUME", data.Volume);
        SetFreeform(sink, data, ProjectMetadataField.Categories, "CATEGORY", data.Categories);
        SetFreeform(sink, data, ProjectMetadataField.Keywords, "KEYWORDS", data.Keywords);
        SetFreeform(sink, data, ProjectMetadataField.Script, "SCRIPT", data.Script);
        SetFreeform(sink, data, ProjectMetadataField.Publisher, "PUBLISHER", data.Publisher);
        SetFreeform(sink, data, ProjectMetadataField.Copyright, "COPYRIGHT", data.Copyright);
        SetFreeform(sink, data, ProjectMetadataField.License, "LICENSE", data.License);
        SetFreeform(sink, data, ProjectMetadataField.PhonogramCopyright, "PHONOGRAMCOPYRIGHT", data.PhonogramCopyright);
        SetFreeform(sink, data, ProjectMetadataField.MusicBrainzReleaseId, "MUSICBRAINZRELEASEID", data.MusicBrainzReleaseId);
        SetFreeform(sink, data, ProjectMetadataField.SortTitle, "SORTTITLE", data.SortTitle);
        SetFreeform(sink, data, ProjectMetadataField.SortAlbum, "SORTALBUM", data.SortAlbum);
        SetFreeform(sink, data, ProjectMetadataField.SortAuthors, "SORTAUTHORS", data.SortAuthors);
        SetFreeform(sink, data, ProjectMetadataField.SortNarrators, "SORTNARRATORS", data.SortNarrators);
        SetFreeform(sink, data, ProjectMetadataField.Translators, "TRANSLATOR", data.Translators);
        SetFreeform(sink, data, ProjectMetadataField.Performers, "PERFORMERS", data.Performers);
        SetFreeform(sink, data, ProjectMetadataField.Contributors, "CONTRIBUTOR", data.Contributors);
        SetFreeform(sink, data, ProjectMetadataField.Hosts, "HOST", data.Hosts);
        SetFreeform(sink, data, ProjectMetadataField.Guests, "GUEST", data.Guests);
        SetFreeform(sink, data, ProjectMetadataField.Speakers, "SPEAKER", data.Speakers);
        SetFreeform(sink, data, ProjectMetadataField.Website, "WEBSITE", data.Website);
        SetFreeform(sink, data, ProjectMetadataField.Source, "SOURCE", data.Source);

        if (data.IsFieldEnabled(ProjectMetadataField.Series))
            SetFreeform(sink, "BOOKSTITCH_SERIES", RenderSeriesPreservation(CleanSeries(data.Series)));

        var compatibilityBlock = CompatibilityDescriptionRenderer.Render(data);
        if (!string.IsNullOrWhiteSpace(compatibilityBlock))
            SetFreeform(sink, "BOOKSTITCH_COMPATIBILITY", compatibilityBlock);

        var description = ActiveString(data, ProjectMetadataField.Description, data.Description);
        if (!string.IsNullOrWhiteSpace(compatibilityBlock) &&
            data.CompatibilityDescriptionEnabled &&
            data.IsFieldEnabled(ProjectMetadataField.Description))
        {
            description = CompatibilityDescriptionRenderer.AppendToDescription(description, compatibilityBlock);
        }

        sink.Description = description;
        sink.Comment = ActiveString(data, ProjectMetadataField.Comment, data.Comment);

        if (IsUsableCover(data.CoverPath))
            sink.SetCover(data.CoverPath);
    }

    private static void ClearOwnedFields(IMp4MetadataSink sink)
    {
        sink.ClearFreeforms();
        sink.SetTextAtom("©nrt", []);
        sink.SetTextAtom("©mvn", []);
        sink.SetTextAtom("©mvi", []);
        sink.Description = "";
        sink.Comment = "";
    }

    private static string ActiveString(FinalAudioTagData data, ProjectMetadataField field, string? value) =>
        data.IsFieldEnabled(field) ? Clean(value) : "";

    private static string[] ActiveList(FinalAudioTagData data, ProjectMetadataField field, IEnumerable<string>? values) =>
        data.IsFieldEnabled(field) ? FinalTagService.CleanList(values).ToArray() : [];

    private static string[] ActiveSeriesText(FinalAudioTagData data, string? value) =>
        data.IsFieldEnabled(ProjectMetadataField.Series) && !string.IsNullOrWhiteSpace(value)
            ? [value.Trim()]
            : [];

    private static string[] ActiveSeriesPartInteger(FinalAudioTagData data, string? value)
    {
        if (!data.IsFieldEnabled(ProjectMetadataField.Series) ||
            !int.TryParse(Clean(value), out var number) ||
            number <= 0)
        {
            return [];
        }

        return [number.ToString(System.Globalization.CultureInfo.InvariantCulture)];
    }

    private static IReadOnlyList<string> Values(IReadOnlyList<string> values, string scalarFallback)
    {
        var clean = FinalTagService.CleanList(values);
        if (clean.Count > 0)
            return clean;

        var scalar = Clean(scalarFallback);
        return string.IsNullOrWhiteSpace(scalar) ? [] : [scalar];
    }

    private static IReadOnlyList<SeriesEntry> CleanSeries(IEnumerable<SeriesEntry>? values) =>
        values?
            .Select(item => new SeriesEntry(Clean(item.Name), Clean(item.Part)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.Part))
            .ToArray() ?? [];

    private static uint SafeYear(int? year) => year is > 0 and <= 9999 ? (uint)year.Value : 0;

    private static string Display(AudioWorkType? value) =>
        value switch
        {
            AudioWorkType.Audiobook => "Audiobook",
            AudioWorkType.AudioDrama => "Audio Drama",
            _ => ""
        };

    private static string Display(AbridgedStatus? value) =>
        value switch
        {
            AbridgedStatus.Abridged => "1",
            AbridgedStatus.Unabridged => "0",
            _ => ""
        };

    private static void SetFreeform(IMp4MetadataSink sink, FinalAudioTagData data, ProjectMetadataField field, string name, string? value)
    {
        if (!data.IsFieldEnabled(field))
            return;

        SetFreeform(sink, name, Clean(value));
    }

    private static void SetFreeform(IMp4MetadataSink sink, FinalAudioTagData data, ProjectMetadataField field, string name, IEnumerable<string>? values)
    {
        if (!data.IsFieldEnabled(field))
            return;

        SetFreeform(sink, name, FinalTagService.CleanList(values));
    }

    private static void SetFreeform(IMp4MetadataSink sink, string name, string? value)
    {
        var clean = Clean(value);
        sink.SetFreeform(name, string.IsNullOrWhiteSpace(clean) ? [] : [clean]);
    }

    private static void SetFreeform(IMp4MetadataSink sink, string name, IReadOnlyList<string> values)
    {
        sink.SetFreeform(name, FinalTagService.CleanList(values).ToArray());
    }

    private static string RenderSeriesPreservation(IReadOnlyList<SeriesEntry> series)
    {
        if (series.Count == 0)
            return "";

        var document = new BookStitchSeriesPreservationDocument(
            1,
            series
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => new BookStitchSeriesPreservationEntry(item.Name, item.Part))
                .ToArray());

        return document.Series.Count == 0 ? "" : JsonSerializer.Serialize(document);
    }

    private static string Clean(string? value) => (value ?? "").Trim();

    private static bool IsUsableCover(string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath) || !IoFile.Exists(coverPath))
            return false;

        var extension = Path.GetExtension(coverPath).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg";
    }

    private sealed record BookStitchSeriesPreservationDocument(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("series")] IReadOnlyList<BookStitchSeriesPreservationEntry> Series);

    private sealed record BookStitchSeriesPreservationEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("part")] string Part);
}

public interface IMp4MetadataSink
{
    string Title { get; set; }
    string Subtitle { get; set; }
    string Album { get; set; }
    string Description { get; set; }
    string Comment { get; set; }
    string[] AlbumArtists { get; set; }
    string[] Performers { get; set; }
    string[] Composers { get; set; }
    string[] Genres { get; set; }
    string Publisher { get; set; }
    string Copyright { get; set; }
    string TitleSort { get; set; }
    string AlbumSort { get; set; }
    string[] AlbumArtistsSort { get; set; }
    string[] PerformersSort { get; set; }
    uint Year { get; set; }
    uint Track { get; set; }
    uint TrackCount { get; set; }
    void SetTextAtom(string atom, IReadOnlyList<string> values);
    void SetFreeform(string name, IReadOnlyList<string> values);
    void ClearFreeforms();
    void ClearSourceOnlyFields();
    void SetStikAudiobook();
    void SetCover(string coverPath);
}

public sealed class AppleTagMetadataSink(AppleTag tag) : IMp4MetadataSink
{
    private static readonly FieldInfo AppleItemListField = ResolveAppleItemListField();

    private AppleItemListBox ItemList =>
        AppleItemListField.GetValue(tag) as AppleItemListBox
        ?? throw new InvalidOperationException("TagLibSharp Apple item list is unavailable.");

    public string Title { get => tag.Title ?? ""; set => tag.Title = value; }
    public string Subtitle { get => tag.Subtitle ?? ""; set => tag.Subtitle = value; }
    public string Album { get => tag.Album ?? ""; set => tag.Album = value; }
    public string Description { get => tag.Description ?? ""; set => tag.Description = value; }
    public string Comment { get => tag.Comment ?? ""; set => tag.Comment = value; }
    public string[] AlbumArtists { get => tag.AlbumArtists; set => tag.AlbumArtists = value; }
    public string[] Performers { get => tag.Performers; set => tag.Performers = value; }
    public string[] Composers { get => tag.Composers; set => tag.Composers = value; }
    public string[] Genres { get => tag.Genres; set => tag.Genres = value; }
    public string Publisher { get => tag.Publisher ?? ""; set => tag.Publisher = value; }
    public string Copyright { get => tag.Copyright ?? ""; set => tag.Copyright = value; }
    public string TitleSort { get => tag.TitleSort ?? ""; set => tag.TitleSort = value; }
    public string AlbumSort { get => tag.AlbumSort ?? ""; set => tag.AlbumSort = value; }
    public string[] AlbumArtistsSort { get => tag.AlbumArtistsSort; set => tag.AlbumArtistsSort = value; }
    public string[] PerformersSort { get => tag.PerformersSort; set => tag.PerformersSort = value; }
    public uint Year { get => tag.Year; set => tag.Year = value; }
    public uint Track { get => tag.Track; set => tag.Track = value; }
    public uint TrackCount { get => tag.TrackCount; set => tag.TrackCount = value; }

    public void SetTextAtom(string atom, IReadOnlyList<string> values)
    {
        var key = ByteVector.FromString(atom, StringType.Latin1);
        var clean = FinalTagService.CleanList(values).ToArray();
        if (clean.Length == 0)
            tag.ClearData(key);
        else
            tag.SetText(key, clean);
    }

    public void SetFreeform(string name, IReadOnlyList<string> values)
    {
        AppleFreeformBoxWriter.Replace(ItemList, "com.apple.iTunes", name, values);
    }

    public void ClearFreeforms()
    {
        ItemList.RemoveChild(ByteVector.FromString("----", StringType.Latin1));
    }

    private static FieldInfo ResolveAppleItemListField()
    {
        var fields = typeof(AppleTag)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(AppleItemListBox))
            .ToArray();

        return fields.Length == 1
            ? fields[0]
            : throw new InvalidOperationException("TagLibSharp Apple item list field could not be resolved uniquely.");
    }

    public void ClearSourceOnlyFields()
    {
        tag.Disc = 0;
        tag.DiscCount = 0;
        tag.ISRC = "";
        tag.MusicBrainzTrackId = "";
        tag.MusicBrainzDiscId = "";
    }

    public void SetStikAudiobook()
    {
        tag.SetData(ByteVector.FromString("stik", StringType.Latin1), new ByteVector([0x02]), 0);
    }

    public void SetCover(string coverPath)
    {
        tag.Pictures =
        [
            new Picture(coverPath)
            {
                Type = PictureType.FrontCover,
                MimeType = "image/jpeg",
                Description = "Cover"
            }
        ];
    }
}

internal static class AppleFreeformBoxWriter
{
    private const uint FullBoxFlags = 1;
    private const uint TextDataFlags = 1;
    private static readonly ByteVector DashType = ByteVector.FromString("----", StringType.Latin1);
    private static readonly ByteVector MeanType = ByteVector.FromString("mean", StringType.Latin1);
    private static readonly ByteVector NameType = ByteVector.FromString("name", StringType.Latin1);
    private static readonly ByteVector DataType = ByteVector.FromString("data", StringType.Latin1);

    internal static void Replace(
        AppleItemListBox itemList,
        string mean,
        string name,
        IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(itemList);

        var cleanMean = (mean ?? string.Empty).Trim();
        var cleanName = (name ?? string.Empty).Trim();
        if (cleanMean.Length == 0 || cleanName.Length == 0)
            throw new ArgumentException("Freeform mean and name must not be empty.");

        foreach (var existing in (itemList.GetChildren(DashType) ?? []).OfType<AppleAnnotationBox>().ToArray())
        {
            if (Matches(existing, cleanMean, cleanName))
                itemList.RemoveChild(existing);
        }

        var cleanValues = FinalTagService.CleanList(values);
        if (cleanValues.Count == 0)
            return;

        var annotation = new AppleAnnotationBox(DashType);
        annotation.AddChild(new AppleAdditionalInfoBox(MeanType, 0, FullBoxFlags) { Text = cleanMean });
        annotation.AddChild(new AppleAdditionalInfoBox(NameType, 0, FullBoxFlags) { Text = cleanName });
        foreach (var value in cleanValues)
            annotation.AddChild(new AppleDataBox(DataType, TextDataFlags) { Text = value });

        itemList.AddChild(annotation);
    }

    private static bool Matches(AppleAnnotationBox annotation, string mean, string name)
    {
        var meanBox = annotation.GetChild(MeanType) as AppleAdditionalInfoBox;
        var nameBox = annotation.GetChild(NameType) as AppleAdditionalInfoBox;
        return string.Equals(meanBox?.Text, mean, StringComparison.Ordinal) &&
               string.Equals(nameBox?.Text, name, StringComparison.OrdinalIgnoreCase);
    }
}

public static class CompatibilityDescriptionRenderer
{
    private static readonly MetadataNormalizationService Normalization = new();

    public static string Render(FinalAudioTagData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var german = data.OutputPresentationCulture.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        var lines = new List<string>();
        Add(lines, Label(german, "Titel", "Title"), data, ProjectMetadataField.Title, data.Title);

        if (data.IsFieldEnabled(ProjectMetadataField.Album) && !EquivalentTitleAndAlbum(data.Title, data.Album))
            Add(lines, Label(german, "Album", "Album"), data, ProjectMetadataField.Album, data.Album);

        Add(lines, Label(german, "Autor", "Author"), data, ProjectMetadataField.Authors, data.Authors, data.Author);

        if (data.IsFieldEnabled(ProjectMetadataField.Series))
        {
            foreach (var series in data.Series)
            {
                var seriesName = Clean(series.Name);
                if (seriesName.Length == 0)
                    continue;

                var seriesPart = Clean(series.Part);
                lines.Add(Bullet(Label(german, "Reihe", "Series") + ": " +
                    (seriesPart.Length > 0 ? seriesName + " " + seriesPart : seriesName)));
            }
        }

        Add(lines, Label(german, "Sprecher", "Narrator"), data, ProjectMetadataField.Narrators, data.Narrators, data.Narrator);
        Add(lines, Label(german, "Sprache", "Language"), data, ProjectMetadataField.Languages, data.Languages);
        AddDuration(lines, Label(german, "Dauer", "Duration"), data.Duration);
        AddChapterCount(lines, Label(german, "Kapitel", "Chapters"), data.ChapterCount);

        return string.Join(" \n", lines);
    }

    public static string AppendToDescription(string? description, string compatibilityBlock)
    {
        var block = compatibilityBlock?.Trim() ?? string.Empty;
        if (block.Length == 0)
            return (description ?? string.Empty).TrimEnd();

        var cleanDescription = (description ?? string.Empty).TrimEnd();
        if (string.Equals(cleanDescription, block, StringComparison.Ordinal) ||
            cleanDescription.EndsWith("\n\n" + block, StringComparison.Ordinal))
        {
            return cleanDescription;
        }

        return cleanDescription.Length == 0
            ? block
            : cleanDescription + "\n\n" + block;
    }

    private static bool EquivalentTitleAndAlbum(string? title, string? album)
    {
        var normalizedTitle = Normalization.NormalizeForComparison(ProjectMetadataField.Title, title);
        var normalizedAlbum = Normalization.NormalizeForComparison(ProjectMetadataField.Album, album);
        return normalizedTitle.Length > 0 && string.Equals(normalizedTitle, normalizedAlbum, StringComparison.Ordinal);
    }

    private static void Add(List<string> lines, string label, FinalAudioTagData data, ProjectMetadataField field, string? value)
    {
        var clean = Clean(value);
        if (!data.IsFieldEnabled(field) || clean.Length == 0)
            return;

        lines.Add(Bullet(label + ": " + clean));
    }

    private static void Add(
        List<string> lines,
        string label,
        FinalAudioTagData data,
        ProjectMetadataField field,
        IReadOnlyList<string> values,
        string scalarFallback = "")
    {
        if (!data.IsFieldEnabled(field))
            return;

        var clean = FinalTagService.CleanList(values);
        if (clean.Count == 0)
            clean = string.IsNullOrWhiteSpace(scalarFallback) ? [] : [scalarFallback.Trim()];
        if (clean.Count == 0)
            return;

        lines.Add(Bullet(label + ": " + string.Join(", ", clean)));
    }

    private static void AddDuration(List<string> lines, string label, TimeSpan? duration)
    {
        if (duration is not { } value || value < TimeSpan.Zero)
            return;

        var totalHours = (long)value.TotalHours;
        lines.Add(Bullet($"{label}: {totalHours:00}:{value.Minutes:00}:{value.Seconds:00}"));
    }

    private static void AddChapterCount(List<string> lines, string label, int? chapterCount)
    {
        if (chapterCount is not > 0)
            return;

        lines.Add(Bullet(label + ": " + chapterCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string Bullet(string line) => "• " + line;

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    private static string Label(bool german, string germanLabel, string englishLabel) =>
        german ? germanLabel : englishLabel;
}
