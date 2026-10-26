using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BookStitch.Models;
using TagLib;
using TagLib.Asf;
using TagLib.Id3v2;
using TagLib.Mpeg4;
using TagLib.Ogg;
using ApeTag = TagLib.Ape.Tag;
using AsfTag = TagLib.Asf.Tag;
using Id3v1Tag = TagLib.Id3v1.Tag;
using Id3v2Tag = TagLib.Id3v2.Tag;
using RiffInfoTag = TagLib.Riff.InfoTag;
using TagLibFile = TagLib.File;
using XiphTag = TagLib.Ogg.XiphComment;

namespace BookStitch.Services;

public sealed record TagMetadataReadResult(
    OriginalTagMetadata Original,
    TimeSpan? TechnicalDurationFallback,
    int? TechnicalBitrateKbpsFallback,
    int? TechnicalChannelsFallback,
    bool HasEmbeddedCover,
    bool Failed,
    IReadOnlyList<string>? Diagnostics = null)
{
    public int? TechnicalSampleRateHzFallback { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Diagnostics ?? [];
}

public sealed record EmbeddedCoverData(byte[] Bytes, string? MimeType);

public class TagMetadataReader
{
    private static readonly IReadOnlyList<ITagMetadataAdapter> Adapters =
    [
        new Id3MetadataAdapter(),
        new Mp4MetadataAdapter(),
        new XiphMetadataAdapter(),
        new AsfMetadataAdapter(),
        new RiffMetadataAdapter(),
        new ApeMetadataAdapter(),
        new GenericTagMetadataAdapter()
    ];

    public virtual TagMetadataReadResult Read(string filePath)
    {
        try
        {
            using var tagFile = TagLibFile.Create(filePath);
            var properties = tagFile.Properties;
            var builder = new OriginalTagMetadataBuilder();

            foreach (var adapter in Adapters)
                adapter.Read(tagFile, builder);

            return new TagMetadataReadResult(
                builder.Build(),
                properties.Duration > TimeSpan.Zero ? properties.Duration : null,
                properties.AudioBitrate > 0 ? properties.AudioBitrate : null,
                properties.AudioChannels > 0 ? properties.AudioChannels : null,
                tagFile.Tag.Pictures is { Length: > 0 },
                Failed: false,
                builder.Diagnostics)
            {
                TechnicalSampleRateHzFallback = properties.AudioSampleRate > 0 ? properties.AudioSampleRate : null
            };
        }
        catch
        {
            return new TagMetadataReadResult(
                new OriginalTagMetadata(),
                TechnicalDurationFallback: null,
                TechnicalBitrateKbpsFallback: null,
                TechnicalChannelsFallback: null,
                HasEmbeddedCover: false,
                Failed: true,
                Diagnostics: ["Tagcontainer konnte nicht gelesen werden."]);
        }
    }

    public virtual EmbeddedCoverData? ReadFirstEmbeddedCover(string filePath)
    {
        try
        {
            using var tagFile = TagLibFile.Create(filePath);
            foreach (var picture in tagFile.Tag.Pictures ?? [])
            {
                var data = picture.Data;
                if (data is null || data.Count > CoverInputSafety.MaxEncodedBytes) continue;
                var bytes = data.Data;
                if (bytes is null || bytes.Length == 0 || bytes.LongLength > CoverInputSafety.MaxEncodedBytes)
                    continue;

                return new EmbeddedCoverData(bytes, picture.MimeType);
            }
        }
        catch
        {
            // Ungültige Tags oder Bilder werden vom Aufrufer übersprungen.
        }

        return null;
    }

    internal static OriginalTagMetadata CreateFromNamedTagsForTests(
        MetadataTagFormat format,
        IReadOnlyDictionary<string, IReadOnlyList<string>> tags) =>
        BuildNamedTagsForTests(format, tags).Build();

    internal static TagMetadataReadResult CreateReadResultFromNamedTagsForTests(
        MetadataTagFormat format,
        IReadOnlyDictionary<string, IReadOnlyList<string>> tags)
    {
        var builder = BuildNamedTagsForTests(format, tags);
        return new TagMetadataReadResult(
            builder.Build(),
            TechnicalDurationFallback: null,
            TechnicalBitrateKbpsFallback: null,
            TechnicalChannelsFallback: null,
            HasEmbeddedCover: false,
            Failed: false,
            builder.Diagnostics);
    }

    private static OriginalTagMetadataBuilder BuildNamedTagsForTests(
        MetadataTagFormat format,
        IReadOnlyDictionary<string, IReadOnlyList<string>> tags)
    {
        var builder = new OriginalTagMetadataBuilder();
        var ns = format switch
        {
            MetadataTagFormat.Mp4 => "----",
            MetadataTagFormat.Xiph => "Xiph",
            MetadataTagFormat.Asf => "ASF",
            MetadataTagFormat.Riff => "RIFF",
            MetadataTagFormat.Ape => "APEv2",
            MetadataTagFormat.Id3v2 => "TXXX",
            MetadataTagFormat.Id3v1 => "ID3v1",
            _ => string.Empty
        };

        var normalizedTags = new Dictionary<string, IReadOnlyList<string>>(tags, StringComparer.OrdinalIgnoreCase);
        foreach (var tag in normalizedTags)
        {
            var key = format == MetadataTagFormat.Mp4
                ? "----:com.apple.iTunes:" + tag.Key
                : tag.Key;
            builder.AddRaw(key, tag.Value, format, ns);
            MapNamedText(builder, tag.Key, tag.Value, format + ":" + tag.Key);
        }

        if (format == MetadataTagFormat.Mp4)
        {
            Mp4MetadataAdapter.ApplyBookStitchSeriesPreservation(builder, normalizedTags);
            Mp4MetadataAdapter.ApplyBookStitchCompatibilityDescription(builder, normalizedTags);
        }

        return builder;
    }

    private interface ITagMetadataAdapter
    {
        void Read(TagLibFile file, OriginalTagMetadataBuilder builder);
    }

    private sealed class Id3MetadataAdapter : ITagMetadataAdapter
    {
        public void Read(TagLibFile file, OriginalTagMetadataBuilder builder)
        {
            if (file.GetTag(TagTypes.Id3v2) is Id3v2Tag id3v2)
            {
                foreach (var frame in id3v2.GetFrames())
                    AddId3Frame(builder, frame);

                MapCommonTag(builder, id3v2, MetadataTagFormat.Id3v2, "ID3v2");
                builder.AddCandidate(ProjectMetadataField.Authors, CleanList(id3v2.AlbumArtists), MetadataCandidateQuality.Exact, "ID3v2:TPE2");
                builder.AddCandidate(ProjectMetadataField.Authors, CleanList(id3v2.Performers), MetadataCandidateQuality.CompatibleAlias, "ID3v2:TPE1");
                builder.AddCandidate(ProjectMetadataField.Narrators, CleanList(id3v2.PerformersRole), MetadataCandidateQuality.Exact, "ID3v2:TMCL");
                builder.AddCandidate(ProjectMetadataField.Composers, CleanList(id3v2.Composers), MetadataCandidateQuality.Exact, "ID3v2:TCOM");
                builder.AddCandidate(ProjectMetadataField.Narrators, CleanList(id3v2.Composers), MetadataCandidateQuality.CompatibleAlias, "ID3v2:TCOM");
                builder.AddCandidate(ProjectMetadataField.SortAuthors, CleanList(id3v2.AlbumArtistsSort), MetadataCandidateQuality.Exact, "ID3v2:TSO2");
                builder.AddCandidate(ProjectMetadataField.SortNarrators, CleanList(id3v2.PerformersSort), MetadataCandidateQuality.CompatibleAlias, "ID3v2:TSOP");
            }

            if (file.GetTag(TagTypes.Id3v1) is Id3v1Tag id3v1)
            {
                MapCommonTag(builder, id3v1, MetadataTagFormat.Id3v1, "ID3v1");
                builder.AddCandidate(ProjectMetadataField.Authors, CleanList(id3v1.Performers), MetadataCandidateQuality.LegacyHeuristic, "ID3v1:Artist");
            }
        }

        private static void AddId3Frame(OriginalTagMetadataBuilder builder, Frame frame)
        {
            try
            {
                switch (frame)
                {
                    case UserTextInformationFrame userText:
                        builder.AddRaw(
                            "TXXX:" + Clean(userText.Description),
                            CleanList(userText.Text),
                            MetadataTagFormat.Id3v2,
                            "TXXX");
                        MapNamedText(builder, userText.Description, userText.Text, "ID3v2:TXXX:" + Clean(userText.Description));
                        break;

                    case CommentsFrame comment:
                        var description = Clean(comment.Description);
                        var language = Clean(comment.Language);
                        builder.AddRaw(
                            $"COMM:{language}:{description}",
                            [comment.Text],
                            MetadataTagFormat.Id3v2,
                            "COMM");
                        if (IsNarratorKey(description))
                            builder.AddCandidate(ProjectMetadataField.Narrators, [comment.Text], MetadataCandidateQuality.CompatibleAlias, "ID3v2:COMM:" + description);
                        break;

                    case TextInformationFrame textFrame:
                        var frameId = frame.FrameId.ToString();
                        builder.AddRaw(frameId, CleanList(textFrame.Text), MetadataTagFormat.Id3v2, frameId);
                        MapId3TextFrame(builder, frameId, textFrame.Text);
                        break;
                }
            }
            catch
            {
                builder.AddDiagnostic("Ein ID3v2-Feld konnte nicht gelesen werden und wurde übersprungen.");
            }
        }

        private static void MapId3TextFrame(OriginalTagMetadataBuilder builder, string frameId, string[] values)
        {
            var quality = MetadataCandidateQuality.Exact;
            switch (frameId.ToUpperInvariant())
            {
                case "TIT2": builder.AddCandidate(ProjectMetadataField.Title, First(values), quality, "ID3v2:TIT2"); break;
                case "TIT3": builder.AddCandidate(ProjectMetadataField.Subtitle, First(values), quality, "ID3v2:TIT3"); break;
                case "TALB": builder.AddCandidate(ProjectMetadataField.Album, First(values), quality, "ID3v2:TALB"); break;
                case "TPE2": builder.AddCandidate(ProjectMetadataField.Authors, CleanList(values), quality, "ID3v2:TPE2"); break;
                case "TPE1": builder.AddCandidate(ProjectMetadataField.Authors, CleanList(values), MetadataCandidateQuality.CompatibleAlias, "ID3v2:TPE1"); break;
                case "TCOM":
                    builder.AddCandidate(ProjectMetadataField.Composers, CleanList(values), MetadataCandidateQuality.Exact, "ID3v2:TCOM");
                    builder.AddCandidate(ProjectMetadataField.Narrators, CleanList(values), MetadataCandidateQuality.CompatibleAlias, "ID3v2:TCOM");
                    break;
                case "TCON": builder.AddCandidate(ProjectMetadataField.Genres, CleanList(values), quality, "ID3v2:TCON"); break;
                case "TPUB": builder.AddCandidate(ProjectMetadataField.Publisher, First(values), quality, "ID3v2:TPUB"); break;
                case "TDRL": builder.AddCandidate(ProjectMetadataField.ReleaseDate, First(values), quality, "ID3v2:TDRL"); break;
                case "TCOP": builder.AddCandidate(ProjectMetadataField.Copyright, First(values), quality, "ID3v2:TCOP"); break;
                case "TRCK": builder.AddSourceTrack(values, "ID3v2:TRCK"); break;
                case "TPOS": builder.AddSourceDisc(values, "ID3v2:TPOS"); break;
                case "TSOT": builder.AddCandidate(ProjectMetadataField.SortTitle, First(values), quality, "ID3v2:TSOT"); break;
                case "TSO2": builder.AddCandidate(ProjectMetadataField.SortAuthors, CleanList(values), quality, "ID3v2:TSO2"); break;
                case "TSOA": builder.AddCandidate(ProjectMetadataField.SortAlbum, First(values), quality, "ID3v2:TSOA"); break;
            }
        }
    }

    private sealed class Mp4MetadataAdapter : ITagMetadataAdapter
    {
        private static readonly string[] FreeformNames =
        [
            "SUBTITLE", "NARRATOR", "LANGUAGE", "ORIGINALLANGUAGE", "TARGETLANGUAGE",
            "SERIES", "SERIES-PART", "SERIESSORT", "ISBN", "BARCODE", "ASIN", "AUDIBLE_ASIN",
            "CATALOGNUMBER", "RELEASEDATE", "ABRIDGED",
            "AUDIOWORKTYPE", "CONTENTTYPE", "EDITION", "VOLUME", "CATEGORY", "KEYWORDS",
            "TRANSLATOR", "PERFORMERS", "CONTRIBUTOR", "HOST", "GUEST", "SPEAKER", "SCRIPT",
            "PUBLISHER", "COPYRIGHT", "LICENSE", "PHONOGRAMCOPYRIGHT", "MUSICBRAINZRELEASEID",
            "ISRC", "GUID", "MUSICBRAINZRECORDINGID",
            "WEBSITE", "SOURCE", "TRANSCRIPT", "ENCODER", "ENCODEDBY",
            "BOOKSTITCH_SERIES", "BOOKSTITCH_COMPATIBILITY"
        ];

        public void Read(TagLibFile file, OriginalTagMetadataBuilder builder)
        {
            if (file.GetTag(TagTypes.Apple) is not AppleTag tag)
                return;

            MapCommonTag(builder, tag, MetadataTagFormat.Mp4, "MP4");
            builder.AddStructuredSourceTrack(tag.Track, tag.TrackCount);
            builder.AddStructuredSourceDisc(tag.Disc, tag.DiscCount);
            if (tag.Track > 0)
                builder.AddRaw("trkn", [FormatSourceNumberPair(tag.Track, tag.TrackCount)], MetadataTagFormat.Mp4, "MP4");
            if (tag.Disc > 0)
                builder.AddRaw("disk", [FormatSourceNumberPair(tag.Disc, tag.DiscCount)], MetadataTagFormat.Mp4, "MP4");
            builder.AddCandidate(ProjectMetadataField.Authors, CleanList(tag.AlbumArtists), MetadataCandidateQuality.Exact, "MP4:aART");
            builder.AddCandidate(ProjectMetadataField.Authors, CleanList(tag.Performers), MetadataCandidateQuality.CompatibleAlias, "MP4:©ART");
            builder.AddCandidate(ProjectMetadataField.SortAuthors, CleanList(tag.AlbumArtistsSort), MetadataCandidateQuality.Exact, "MP4:soaa");
            builder.AddCandidate(ProjectMetadataField.SortTitle, Clean(tag.TitleSort), MetadataCandidateQuality.Exact, "MP4:sonm");
            builder.AddCandidate(ProjectMetadataField.SortAlbum, Clean(tag.AlbumSort), MetadataCandidateQuality.Exact, "MP4:soal");

            AddMp4Text(builder, tag, "©nrt", ProjectMetadataField.Narrators, MetadataCandidateQuality.Exact);
            AddMp4Text(builder, tag, "©mvn", ProjectMetadataField.Series, MetadataCandidateQuality.CompatibleAlias);

            var freeforms = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in FreeformNames)
            {
                IReadOnlyList<string> values;
                try
                {
                    values = CleanList(tag.GetDashBoxes("com.apple.iTunes", name));
                }
                catch
                {
                    builder.AddDiagnostic($"MP4-Freeform '{name}' konnte nicht gelesen werden und wurde übersprungen.");
                    continue;
                }

                if (values.Count == 0)
                    continue;

                freeforms[name] = values;
                builder.AddRaw("----:com.apple.iTunes:" + name, values, MetadataTagFormat.Mp4, "----");
                MapNamedText(builder, name, values, "MP4:----:" + name);
            }

            ApplyBookStitchSeriesPreservation(builder, freeforms);
            ApplyBookStitchCompatibilityDescription(builder, freeforms);
        }

        private static void AddMp4Text(
            OriginalTagMetadataBuilder builder,
            AppleTag tag,
            string atom,
            ProjectMetadataField field,
            MetadataCandidateQuality quality)
        {
            try
            {
                var values = CleanList(tag.GetText(new ByteVector(atom)));
                if (values.Count == 0)
                    return;

                builder.AddRaw(atom, values, MetadataTagFormat.Mp4, atom);
                builder.AddCandidate(field, values, quality, "MP4:" + atom);
            }
            catch
            {
                builder.AddDiagnostic("Ein MP4-Atom konnte nicht gelesen werden und wurde übersprungen.");
            }
        }

        internal static void ApplyBookStitchSeriesPreservation(
            OriginalTagMetadataBuilder builder,
            IReadOnlyDictionary<string, IReadOnlyList<string>> freeforms)
        {
            if (!freeforms.TryGetValue("BOOKSTITCH_SERIES", out var values) || values.Count == 0)
                return;

            try
            {
                var document = JsonSerializer.Deserialize<BookStitchSeriesPreservationDocument>(values[0]);
                if (document?.Version != 1 || document.Series is null)
                {
                    builder.AddDiagnostic("BOOKSTITCH_SERIES besitzt eine unbekannte Version und wurde nur als RawTag erhalten.");
                    return;
                }

                var series = document.Series
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                    .Select(item => new SeriesEntry(item.Name.Trim(), item.Part?.Trim() ?? string.Empty))
                    .ToArray();
                builder.AddCandidate(ProjectMetadataField.Series, series, MetadataCandidateQuality.CompatibleAlias, "MP4:BOOKSTITCH_SERIES");
            }
            catch
            {
                builder.AddDiagnostic("BOOKSTITCH_SERIES konnte nicht gelesen werden und wurde nur als RawTag erhalten.");
            }
        }

        internal static void ApplyBookStitchCompatibilityDescription(
            OriginalTagMetadataBuilder builder,
            IReadOnlyDictionary<string, IReadOnlyList<string>> freeforms)
        {
            if (!freeforms.TryGetValue("BOOKSTITCH_COMPATIBILITY", out var values) || values.Count == 0)
                return;

            var block = values[0].Trim();
            if (string.IsNullOrWhiteSpace(block))
                return;

            builder.TrimDescriptionSuffix(block);
            builder.TrimBookStitchCompatibilityDescriptionSuffix();
        }

        private sealed class BookStitchSeriesPreservationDocument
        {
            [JsonPropertyName("version")]
            public int Version { get; set; }

            [JsonPropertyName("series")]
            public List<BookStitchSeriesPreservationEntry>? Series { get; set; }
        }

        private sealed class BookStitchSeriesPreservationEntry
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("part")]
            public string? Part { get; set; }
        }
    }

    private sealed class XiphMetadataAdapter : ITagMetadataAdapter
    {
        public void Read(TagLibFile file, OriginalTagMetadataBuilder builder)
        {
            if (file.GetTag(TagTypes.Xiph) is not XiphTag tag)
                return;

            foreach (var key in tag)
            {
                var values = CleanList(tag.GetField(key));
                if (values.Count == 0)
                    continue;

                builder.AddRaw(key.ToUpperInvariant(), values, MetadataTagFormat.Xiph, "Xiph");
                MapNamedText(builder, key, values, "Xiph:" + key.ToUpperInvariant());
            }

            MapCommonTag(builder, tag, MetadataTagFormat.Xiph, "Xiph");
            builder.AddCandidate(ProjectMetadataField.Authors, CleanList(tag.GetField("AUTHOR")), MetadataCandidateQuality.Exact, "Xiph:AUTHOR");
            builder.AddCandidate(ProjectMetadataField.Authors, CleanList(tag.GetField("ARTIST")), MetadataCandidateQuality.CompatibleAlias, "Xiph:ARTIST");
            builder.AddCandidate(ProjectMetadataField.Narrators, CleanList(tag.GetField("NARRATOR")), MetadataCandidateQuality.Exact, "Xiph:NARRATOR");
            builder.AddCandidate(ProjectMetadataField.Narrators, CleanList(tag.GetField("PERFORMER")), MetadataCandidateQuality.Exact, "Xiph:PERFORMER");
        }
    }

    private sealed class AsfMetadataAdapter : ITagMetadataAdapter
    {
        public void Read(TagLibFile file, OriginalTagMetadataBuilder builder)
        {
            if (file.GetTag(TagTypes.Asf) is not AsfTag tag)
                return;

            foreach (var descriptor in tag)
            {
                var value = descriptor.ToString();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                builder.AddRaw(descriptor.Name, [value], MetadataTagFormat.Asf, "ASF");
                MapNamedText(builder, descriptor.Name, [value], "ASF:" + descriptor.Name);
            }

            MapCommonTag(builder, tag, MetadataTagFormat.Asf, "ASF");
        }
    }

    private sealed class RiffMetadataAdapter : ITagMetadataAdapter
    {
        public void Read(TagLibFile file, OriginalTagMetadataBuilder builder)
        {
            if (file.GetTag(TagTypes.RiffInfo) is not RiffInfoTag tag)
                return;

            MapCommonTag(builder, tag, MetadataTagFormat.Riff, "RIFF");
            builder.AddStructuredSourceTrack(tag.Track, 0);
            builder.AddCandidate(ProjectMetadataField.Authors, CleanList(tag.Performers), MetadataCandidateQuality.CompatibleAlias, "RIFF:IART");
        }
    }

    private sealed class ApeMetadataAdapter : ITagMetadataAdapter
    {
        public void Read(TagLibFile file, OriginalTagMetadataBuilder builder)
        {
            if (file.GetTag(TagTypes.Ape) is not ApeTag tag)
                return;

            foreach (var key in tag)
            {
                var item = tag.GetItem(key);
                if (item is null || item.IsEmpty || item.Type != TagLib.Ape.ItemType.Text)
                    continue;

                var values = CleanList(item.ToStringArray());
                if (values.Count == 0)
                    continue;

                builder.AddRaw(key, values, MetadataTagFormat.Ape, "APEv2");
                MapNamedText(builder, key, values, "APEv2:" + key);
            }

            MapCommonTag(builder, tag, MetadataTagFormat.Ape, "APEv2");
        }
    }

    private sealed class GenericTagMetadataAdapter : ITagMetadataAdapter
    {
        public void Read(TagLibFile file, OriginalTagMetadataBuilder builder)
        {
            if (builder.HasAnyCandidate)
                return;

            MapCommonTag(builder, file.Tag, MetadataTagFormat.Unknown, "Generic");
        }
    }

    private sealed class OriginalTagMetadataBuilder
    {
        private const int MaximumRawValueLength = 16 * 1024;
        private readonly List<MetadataCandidate> _candidates = [];
        private readonly List<RawTagEntry> _rawTags = [];
        private readonly List<string> _diagnostics = [];
        private uint _sourceTrackNumber;
        private uint _sourceTrackTotal;
        private uint _sourceDiscNumber;
        private uint _sourceDiscTotal;

        public bool HasAnyCandidate => _candidates.Count > 0;
        public IReadOnlyList<string> Diagnostics => _diagnostics.ToArray();

        public void AddCandidate(ProjectMetadataField field, string value, MetadataCandidateQuality quality, string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            _candidates.Add(new MetadataCandidate(field, value.Trim(), quality, sourceKey));
        }

        public void AddCandidate(ProjectMetadataField field, IReadOnlyList<string> values, MetadataCandidateQuality quality, string sourceKey)
        {
            if (values.Count == 0)
                return;

            _candidates.Add(new MetadataCandidate(field, values.ToArray(), quality, sourceKey));
        }

        public void AddCandidate(ProjectMetadataField field, IReadOnlyList<SeriesEntry> values, MetadataCandidateQuality quality, string sourceKey)
        {
            if (values.Count == 0)
                return;

            _candidates.Add(new MetadataCandidate(field, values.ToArray(), quality, sourceKey));
        }

        public void AddSourceTrack(IReadOnlyList<string> values, string sourceKey)
        {
            if (!TryParseSourceNumberPair(First(values), out var number, out var total))
                return;

            if (_sourceTrackNumber == 0)
                _sourceTrackNumber = number;
            if (_sourceTrackTotal == 0 && total > 0)
                _sourceTrackTotal = total;
        }

        public void AddSourceTrackTotal(IReadOnlyList<string> values, string sourceKey)
        {
            if (_sourceTrackTotal == 0 && TryParsePositiveSourceNumber(First(values), out var total))
                _sourceTrackTotal = total;
        }

        public void AddSourceDisc(IReadOnlyList<string> values, string sourceKey)
        {
            if (!TryParseSourceNumberPair(First(values), out var number, out var total))
                return;

            if (_sourceDiscNumber == 0)
                _sourceDiscNumber = number;
            if (_sourceDiscTotal == 0 && total > 0)
                _sourceDiscTotal = total;
        }

        public void AddSourceDiscTotal(IReadOnlyList<string> values, string sourceKey)
        {
            if (_sourceDiscTotal == 0 && TryParsePositiveSourceNumber(First(values), out var total))
                _sourceDiscTotal = total;
        }

        public void AddStructuredSourceTrack(uint number, uint total)
        {
            if (_sourceTrackNumber == 0 && number > 0)
                _sourceTrackNumber = number;
            if (_sourceTrackTotal == 0 && total > 0)
                _sourceTrackTotal = total;
        }

        public void AddStructuredSourceDisc(uint number, uint total)
        {
            if (_sourceDiscNumber == 0 && number > 0)
                _sourceDiscNumber = number;
            if (_sourceDiscTotal == 0 && total > 0)
                _sourceDiscTotal = total;
        }

        private static bool TryParseSourceNumberPair(string value, out uint number, out uint total)
        {
            number = 0;
            total = 0;
            var cleaned = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cleaned))
                return false;

            var parts = cleaned.Split('/');
            if (parts.Length is < 1 or > 2 ||
                !TryParsePositiveSourceNumber(parts[0], out number))
            {
                number = 0;
                return false;
            }

            if (parts.Length == 1)
                return true;

            if (!TryParsePositiveSourceNumber(parts[1], out total))
            {
                number = 0;
                total = 0;
                return false;
            }

            return true;
        }

        private static bool TryParsePositiveSourceNumber(string value, out uint number)
        {
            number = 0;
            var cleaned = value?.Trim() ?? string.Empty;
            if (cleaned.Length == 0 || cleaned.Any(ch => ch < '0' || ch > '9'))
                return false;

            return uint.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
        }

        public void AddRaw(string originalKey, IReadOnlyList<string> values, MetadataTagFormat format, string ns)
        {
            var cleanedValues = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Select(value =>
                {
                    if (value.Length <= MaximumRawValueLength)
                        return value;

                    AddDiagnostic($"RawTag '{originalKey}' wurde wegen Überlänge gekürzt.");
                    return value[..MaximumRawValueLength];
                })
                .ToArray();

            if (string.IsNullOrWhiteSpace(originalKey) || cleanedValues.Length == 0)
                return;

            _rawTags.Add(new RawTagEntry(originalKey, cleanedValues, format, ns));
        }

        public void AddDiagnostic(string diagnostic)
        {
            if (!string.IsNullOrWhiteSpace(diagnostic))
                _diagnostics.Add(diagnostic.Trim());
        }

        public void TrimDescriptionSuffix(string suffix)
        {
            var current = BestString(ProjectMetadataField.Description);
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(suffix))
                return;

            var trimmedSuffix = suffix.Trim();
            var trimmedCurrent = current.TrimEnd();
            if (!trimmedCurrent.EndsWith(trimmedSuffix, StringComparison.Ordinal))
                return;

            var baseDescription = trimmedCurrent[..^trimmedSuffix.Length].TrimEnd();
            _candidates.RemoveAll(candidate => candidate.Field == ProjectMetadataField.Description);
            AddCandidate(ProjectMetadataField.Description, baseDescription, MetadataCandidateQuality.Exact, "MP4:BOOKSTITCH_COMPATIBILITY");
        }

        public void TrimBookStitchCompatibilityDescriptionSuffix()
        {
            var current = BestString(ProjectMetadataField.Description);
            if (string.IsNullOrWhiteSpace(current))
                return;

            var markerIndex = FindBookStitchCompatibilityMarker(current);
            if (markerIndex < 0)
                return;

            var baseDescription = current[..markerIndex].TrimEnd();
            _candidates.RemoveAll(candidate => candidate.Field == ProjectMetadataField.Description);
            AddCandidate(ProjectMetadataField.Description, baseDescription, MetadataCandidateQuality.Exact, "MP4:BOOKSTITCH_COMPATIBILITY");
        }

        private static int FindBookStitchCompatibilityMarker(string value)
        {
            var markers = new[]
            {
                "\n\nBookStitch Compatibility Data",
                "\r\n\r\nBookStitch Compatibility Data",
                "\n\nBookStitch-Kompatibilitaetsdaten",
                "\r\n\r\nBookStitch-Kompatibilitaetsdaten",
                "\n\nBookStitch-Kompatibilitätsdaten",
                "\r\n\r\nBookStitch-Kompatibilitätsdaten"
            };

            foreach (var marker in markers)
            {
                var index = value.LastIndexOf(marker, StringComparison.Ordinal);
                if (index >= 0)
                    return index;
            }

            return -1;
        }

        public OriginalTagMetadata Build()
        {
            var rawDictionary = _rawTags
                .GroupBy(tag => tag.OriginalKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<string>)group.SelectMany(tag => tag.Values).Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            return new OriginalTagMetadata
            {
                TrackNumber = _sourceTrackNumber,
                TrackTotal = _sourceTrackTotal,
                DiscNumber = _sourceDiscNumber,
                DiscTotal = _sourceDiscTotal,
                Title = BestString(ProjectMetadataField.Title),
                Subtitle = BestString(ProjectMetadataField.Subtitle),
                OriginalTitle = BestString(ProjectMetadataField.OriginalTitle),
                Album = BestString(ProjectMetadataField.Album),
                ContentType = BestString(ProjectMetadataField.ContentType),
                Description = BestString(ProjectMetadataField.Description),
                Comment = BestString(ProjectMetadataField.Comment),
                Keywords = BestList(ProjectMetadataField.Keywords),
                Authors = BestList(ProjectMetadataField.Authors),
                Narrators = BestList(ProjectMetadataField.Narrators),
                Translators = BestList(ProjectMetadataField.Translators),
                Composers = BestList(ProjectMetadataField.Composers),
                Performers = BestList(ProjectMetadataField.Performers),
                Contributors = BestList(ProjectMetadataField.Contributors),
                Hosts = BestList(ProjectMetadataField.Hosts),
                Guests = BestList(ProjectMetadataField.Guests),
                Speakers = BestList(ProjectMetadataField.Speakers),
                Series = BestSeriesList(),
                Volume = BestString(ProjectMetadataField.Volume),
                Edition = BestString(ProjectMetadataField.Edition),
                AbridgedStatus = BestString(ProjectMetadataField.AbridgedStatus),
                AudioWorkType = BestString(ProjectMetadataField.AudioWorkType),
                Languages = BestList(ProjectMetadataField.Languages),
                OriginalLanguages = BestList(ProjectMetadataField.OriginalLanguages),
                TargetLanguages = BestList(ProjectMetadataField.TargetLanguages),
                Script = BestString(ProjectMetadataField.Script),
                Publisher = BestString(ProjectMetadataField.Publisher),
                ReleaseDate = BestString(ProjectMetadataField.ReleaseDate),
                Year = BestYear(),
                Copyright = BestString(ProjectMetadataField.Copyright),
                PhonogramCopyright = BestString(ProjectMetadataField.PhonogramCopyright),
                License = BestString(ProjectMetadataField.License),
                Genres = BestList(ProjectMetadataField.Genres),
                Categories = BestList(ProjectMetadataField.Categories),
                ISBN = BestString(ProjectMetadataField.ISBN),
                Barcode = BestString(ProjectMetadataField.Barcode),
                ASIN = BestString(ProjectMetadataField.ASIN),
                CatalogNumbers = BestList(ProjectMetadataField.CatalogNumbers),
                MusicBrainzReleaseId = BestString(ProjectMetadataField.MusicBrainzReleaseId),
                SortTitle = BestString(ProjectMetadataField.SortTitle),
                SortAuthors = BestList(ProjectMetadataField.SortAuthors),
                SortNarrators = BestList(ProjectMetadataField.SortNarrators),
                SortAlbum = BestString(ProjectMetadataField.SortAlbum),
                SortSeries = BestString(ProjectMetadataField.SortSeries),
                Website = BestString(ProjectMetadataField.Website),
                Source = BestString(ProjectMetadataField.Source),
                RawTags = rawDictionary,
                RawTagEntries = _rawTags.ToArray()
            };
        }

        private string BestString(ProjectMetadataField field) =>
            BestCandidate(field)?.Value as string ?? string.Empty;

        private uint BestYear()
        {
            var value = BestString(ProjectMetadataField.Year);
            return uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                ? year
                : 0;
        }

        private IReadOnlyList<string> BestList(ProjectMetadataField field) =>
            BestCandidate(field)?.Value is IEnumerable<string> values
                ? values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray()
                : [];

        private IReadOnlyList<SeriesEntry> BestSeriesList()
        {
            var candidates = _candidates
                .Where(candidate => candidate.Field == ProjectMetadataField.Series)
                .OrderBy(candidate => candidate.Quality)
                .ToList();
            if (candidates.Count == 0)
                return [];

            var bestQuality = candidates[0].Quality;
            var best = CleanSeriesCandidates(candidates.Where(candidate => candidate.Quality == bestQuality));
            if (best.Count == 0)
                return [];

            // BOOKSTITCH_SERIES may preserve additional ordered series that do not fit into
            // the conventional primary SERIES/SERIES-PART pair. Exact external values stay
            // authoritative; compatible preservation data may only fill matching/missing detail
            // and append additional entries.
            if (bestQuality == MetadataCandidateQuality.Exact)
            {
                var preserved = CleanSeriesCandidates(candidates.Where(candidate =>
                    candidate.SourceKey.Equals("MP4:BOOKSTITCH_SERIES", StringComparison.Ordinal)));
                if (preserved.Count > 0 && SeriesNamesMatch(best[0], preserved[0]))
                {
                    var first = new SeriesEntry(
                        best[0].Name,
                        string.IsNullOrWhiteSpace(best[0].Part) ? preserved[0].Part : best[0].Part);
                    var merged = new List<SeriesEntry> { first };
                    merged.AddRange(best.Skip(1));
                    foreach (var item in preserved.Skip(1))
                    {
                        if (!merged.Contains(item))
                            merged.Add(item);
                    }

                    return merged;
                }
            }

            return best;
        }

        private static IReadOnlyList<SeriesEntry> CleanSeriesCandidates(IEnumerable<MetadataCandidate> candidates)
        {
            var flattened = candidates
                .Where(candidate => candidate.Value is IEnumerable<SeriesEntry>)
                .SelectMany(candidate => (IEnumerable<SeriesEntry>)candidate.Value!)
                .Select(value => new SeriesEntry(value.Name?.Trim() ?? string.Empty, value.Part?.Trim() ?? string.Empty))
                .Where(value => !string.IsNullOrWhiteSpace(value.Name) || !string.IsNullOrWhiteSpace(value.Part))
                .ToList();

            var named = flattened
                .Where(value => !string.IsNullOrWhiteSpace(value.Name))
                .ToList();
            if (named.Count == 0)
                return [];

            var orphanPart = flattened
                .FirstOrDefault(value => string.IsNullOrWhiteSpace(value.Name) && !string.IsNullOrWhiteSpace(value.Part))
                ?.Part ?? string.Empty;
            if (named.Count == 1 && string.IsNullOrWhiteSpace(named[0].Part) && !string.IsNullOrWhiteSpace(orphanPart))
                named[0] = new SeriesEntry(named[0].Name, orphanPart);

            return named.Distinct().ToArray();
        }

        private static bool SeriesNamesMatch(SeriesEntry left, SeriesEntry right) =>
            !string.IsNullOrWhiteSpace(left.Name) &&
            !string.IsNullOrWhiteSpace(right.Name) &&
            string.Equals(left.Name.Trim(), right.Name.Trim(), StringComparison.OrdinalIgnoreCase);

        private MetadataCandidate? BestCandidate(ProjectMetadataField field) =>
            _candidates
                .Where(candidate => candidate.Field == field)
                .OrderBy(candidate => candidate.Quality)
                .FirstOrDefault();
    }

    private static void MapCommonTag(
        OriginalTagMetadataBuilder builder,
        TagLib.Tag tag,
        MetadataTagFormat format,
        string prefix)
    {
        AddCommonRaw(builder, tag, format, prefix);
        builder.AddCandidate(ProjectMetadataField.Title, Clean(tag.Title), MetadataCandidateQuality.Exact, prefix + ":Title");
        builder.AddCandidate(ProjectMetadataField.Subtitle, Clean(tag.Subtitle), MetadataCandidateQuality.Exact, prefix + ":Subtitle");
        builder.AddCandidate(ProjectMetadataField.Album, Clean(tag.Album), MetadataCandidateQuality.Exact, prefix + ":Album");
        builder.AddCandidate(ProjectMetadataField.Description, Clean(tag.Description), MetadataCandidateQuality.Exact, prefix + ":Description");
        builder.AddCandidate(ProjectMetadataField.Comment, Clean(tag.Comment), MetadataCandidateQuality.Exact, prefix + ":Comment");
        builder.AddCandidate(ProjectMetadataField.Authors, CleanList(tag.AlbumArtists), MetadataCandidateQuality.Exact, prefix + ":AlbumArtists");
        builder.AddCandidate(ProjectMetadataField.Authors, CleanList(tag.Performers), MetadataCandidateQuality.CompatibleAlias, prefix + ":Performers");
        builder.AddCandidate(ProjectMetadataField.Composers, CleanList(tag.Composers), MetadataCandidateQuality.Exact, prefix + ":Composers");
        builder.AddCandidate(ProjectMetadataField.Narrators, CleanList(tag.Composers), MetadataCandidateQuality.CompatibleAlias, prefix + ":Composers");
        builder.AddCandidate(ProjectMetadataField.Genres, CleanList(tag.Genres), MetadataCandidateQuality.Exact, prefix + ":Genres");
        builder.AddCandidate(ProjectMetadataField.Publisher, Clean(tag.Publisher), MetadataCandidateQuality.Exact, prefix + ":Publisher");
        builder.AddCandidate(ProjectMetadataField.Copyright, Clean(tag.Copyright), MetadataCandidateQuality.Exact, prefix + ":Copyright");
        builder.AddCandidate(ProjectMetadataField.SortTitle, Clean(tag.TitleSort), MetadataCandidateQuality.Exact, prefix + ":TitleSort");
        builder.AddCandidate(ProjectMetadataField.SortAuthors, CleanList(tag.AlbumArtistsSort), MetadataCandidateQuality.Exact, prefix + ":AlbumArtistsSort");
        builder.AddCandidate(ProjectMetadataField.SortAlbum, Clean(tag.AlbumSort), MetadataCandidateQuality.Exact, prefix + ":AlbumSort");
        builder.AddCandidate(ProjectMetadataField.Narrators, ExtractNarrators(tag.Comment), MetadataCandidateQuality.LegacyHeuristic, prefix + ":Comment");

        if (tag.Year > 0)
            builder.AddCandidate(ProjectMetadataField.Year, tag.Year.ToString(CultureInfo.InvariantCulture), MetadataCandidateQuality.Exact, prefix + ":Year");
    }

    private static void AddCommonRaw(
        OriginalTagMetadataBuilder builder,
        TagLib.Tag tag,
        MetadataTagFormat format,
        string prefix)
    {
        builder.AddRaw(prefix + ":Title", [tag.Title], format, prefix);
        builder.AddRaw(prefix + ":Subtitle", [tag.Subtitle], format, prefix);
        builder.AddRaw(prefix + ":Album", [tag.Album], format, prefix);
        builder.AddRaw(prefix + ":Description", [tag.Description], format, prefix);
        builder.AddRaw(prefix + ":Comment", [tag.Comment], format, prefix);
        builder.AddRaw(prefix + ":AlbumArtists", CleanList(tag.AlbumArtists), format, prefix);
        builder.AddRaw(prefix + ":Performers", CleanList(tag.Performers), format, prefix);
        builder.AddRaw(prefix + ":Composers", CleanList(tag.Composers), format, prefix);
        builder.AddRaw(prefix + ":Genres", CleanList(tag.Genres), format, prefix);
        builder.AddRaw(prefix + ":Publisher", [tag.Publisher], format, prefix);
        builder.AddRaw(prefix + ":Copyright", [tag.Copyright], format, prefix);
        builder.AddRaw(prefix + ":TitleSort", [tag.TitleSort], format, prefix);
        builder.AddRaw(prefix + ":AlbumArtistsSort", CleanList(tag.AlbumArtistsSort), format, prefix);
        builder.AddRaw(prefix + ":AlbumSort", [tag.AlbumSort], format, prefix);
    }

    private static void MapNamedText(
        OriginalTagMetadataBuilder builder,
        string key,
        IReadOnlyList<string> values,
        string sourceKey)
    {
        var normalized = NormalizeKey(key);
        switch (normalized)
        {
            case "TITLE": builder.AddCandidate(ProjectMetadataField.Title, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "SUBTITLE": builder.AddCandidate(ProjectMetadataField.Subtitle, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            // Original work title is independent of the current title and the original album (TOAL).
            case "ORIGINALTITLE": builder.AddCandidate(ProjectMetadataField.OriginalTitle, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "ORIGTITLE": builder.AddCandidate(ProjectMetadataField.OriginalTitle, First(values), MetadataCandidateQuality.CompatibleAlias, sourceKey); break;
            case "ALBUM": builder.AddCandidate(ProjectMetadataField.Album, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "DESCRIPTION": builder.AddCandidate(ProjectMetadataField.Description, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "COMMENT": builder.AddCandidate(ProjectMetadataField.Comment, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "AUTHOR": builder.AddCandidate(ProjectMetadataField.Authors, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "ARTIST": builder.AddCandidate(ProjectMetadataField.Authors, values, MetadataCandidateQuality.CompatibleAlias, sourceKey); break;
            case "ALBUMARTIST":
            case "ALBUMARTISTS":
                builder.AddCandidate(ProjectMetadataField.Authors, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "NARRATOR":
            case "NARRATORS":
            case "READBY":
            case "READ_BY":
                builder.AddCandidate(ProjectMetadataField.Narrators, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "PERFORMER":
                builder.AddCandidate(ProjectMetadataField.Narrators, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "COMPOSER":
            case "COMPOSERS":
            case "TCOM":
                builder.AddCandidate(ProjectMetadataField.Composers, values, MetadataCandidateQuality.Exact, sourceKey);
                builder.AddCandidate(ProjectMetadataField.Narrators, values, MetadataCandidateQuality.CompatibleAlias, sourceKey);
                break;
            case "TRANSLATOR":
            case "TRANSLATORS":
                builder.AddCandidate(ProjectMetadataField.Translators, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "PERFORMERS":
                builder.AddCandidate(ProjectMetadataField.Performers, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "CONTRIBUTOR":
            case "CONTRIBUTORS":
                builder.AddCandidate(ProjectMetadataField.Contributors, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "HOST":
            case "HOSTS":
                builder.AddCandidate(ProjectMetadataField.Hosts, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "GUEST":
            case "GUESTS":
                builder.AddCandidate(ProjectMetadataField.Guests, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "SPEAKER":
            case "SPEAKERS":
                builder.AddCandidate(ProjectMetadataField.Speakers, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "GENRE":
                builder.AddCandidate(ProjectMetadataField.Genres, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "LANGUAGE":
                builder.AddCandidate(ProjectMetadataField.Languages, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "ORIGINALLANGUAGE":
                builder.AddCandidate(ProjectMetadataField.OriginalLanguages, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "TARGETLANGUAGE":
                builder.AddCandidate(ProjectMetadataField.TargetLanguages, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "SERIES":
                builder.AddCandidate(ProjectMetadataField.Series, [new SeriesEntry(First(values), string.Empty)], MetadataCandidateQuality.Exact, sourceKey); break;
            case "SERIESPART":
            case "SERIES_PART":
                builder.AddCandidate(ProjectMetadataField.Series, [new SeriesEntry(string.Empty, First(values))], MetadataCandidateQuality.Exact, sourceKey); break;
            case "SERIESSORT":
                builder.AddCandidate(ProjectMetadataField.SortSeries, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "ISBN":
                builder.AddCandidate(ProjectMetadataField.ISBN, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "BARCODE":
            case "EAN":
                builder.AddCandidate(ProjectMetadataField.Barcode, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "ASIN":
            case "AUDIBLEASIN":
                builder.AddCandidate(ProjectMetadataField.ASIN, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "CATALOGNUMBER":
            case "CATALOGNUMBERS":
                builder.AddCandidate(ProjectMetadataField.CatalogNumbers, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "RELEASEDATE":
                builder.AddCandidate(ProjectMetadataField.ReleaseDate, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "YEAR":
                builder.AddCandidate(ProjectMetadataField.Year, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "CONTENTTYPE":
                builder.AddCandidate(ProjectMetadataField.ContentType, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "ABRIDGED":
            case "ABRIDGEDSTATUS":
                builder.AddCandidate(ProjectMetadataField.AbridgedStatus, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "AUDIOWORKTYPE":
            case "WORKTYPE":
                builder.AddCandidate(ProjectMetadataField.AudioWorkType, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "EDITION":
                builder.AddCandidate(ProjectMetadataField.Edition, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "VOLUME":
                builder.AddCandidate(ProjectMetadataField.Volume, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "TRCK":
            case "TRKN":
            case "TRACK":
            case "TRACKNUMBER":
            case "WMTRACKNUMBER":
            case "ITRK":
                builder.AddSourceTrack(values, sourceKey); break;
            case "TRACKTOTAL":
            case "TOTALTRACKS":
                builder.AddSourceTrackTotal(values, sourceKey); break;
            case "TPOS":
            case "DISK":
            case "DISC":
            case "DISCNUMBER":
            case "WMPARTOFSET":
                builder.AddSourceDisc(values, sourceKey); break;
            case "DISCTOTAL":
            case "TOTALDISCS":
                builder.AddSourceDiscTotal(values, sourceKey); break;
            case "SCRIPT":
                builder.AddCandidate(ProjectMetadataField.Script, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "PUBLISHER":
                builder.AddCandidate(ProjectMetadataField.Publisher, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "COPYRIGHT":
                builder.AddCandidate(ProjectMetadataField.Copyright, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "PHONOGRAMCOPYRIGHT":
                builder.AddCandidate(ProjectMetadataField.PhonogramCopyright, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "LICENSE":
                builder.AddCandidate(ProjectMetadataField.License, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "CATEGORY":
            case "CATEGORIES":
                builder.AddCandidate(ProjectMetadataField.Categories, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "KEYWORD":
            case "KEYWORDS":
                builder.AddCandidate(ProjectMetadataField.Keywords, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "SORTTITLE":
                builder.AddCandidate(ProjectMetadataField.SortTitle, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "SORTALBUM":
                builder.AddCandidate(ProjectMetadataField.SortAlbum, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "SORTAUTHOR":
            case "SORTAUTHORS":
                builder.AddCandidate(ProjectMetadataField.SortAuthors, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "NARRATORSORT":
            case "SORTNARRATOR":
            case "SORTNARRATORS":
                builder.AddCandidate(ProjectMetadataField.SortNarrators, values, MetadataCandidateQuality.Exact, sourceKey); break;
            case "MUSICBRAINZRELEASEID":
                builder.AddCandidate(ProjectMetadataField.MusicBrainzReleaseId, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "WEBSITE":
                builder.AddCandidate(ProjectMetadataField.Website, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
            case "SOURCE":
                builder.AddCandidate(ProjectMetadataField.Source, First(values), MetadataCandidateQuality.Exact, sourceKey); break;
        }
    }

    private static bool IsNarratorKey(string key) =>
        NormalizeKey(key) is "NARRATOR" or "NARRATORS" or "SPRECHER" or "GELESENVON";

    private static string NormalizeKey(string key) =>
        Regex.Replace(key ?? string.Empty, @"[^A-Za-z0-9]", string.Empty).ToUpperInvariant();

    private static string FormatSourceNumberPair(uint number, uint total) =>
        total > 0
            ? number.ToString(CultureInfo.InvariantCulture) + "/" + total.ToString(CultureInfo.InvariantCulture)
            : number.ToString(CultureInfo.InvariantCulture);

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string First(IEnumerable<string> values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static IReadOnlyList<string> CleanList(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

    private static IReadOnlyList<string> ExtractNarrators(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return [];

        var match = Regex.Match(
            comment,
            @"(?i)\b(?:sprecher|narrator|gelesen von)\s*[:\-]\s*(?<name>[^;\r\n]+)");

        return match.Success ? [match.Groups["name"].Value.Trim()] : [];
    }
}
