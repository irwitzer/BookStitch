using System.Text.RegularExpressions;

namespace BookStitch.Services;

public static class ChapterMarkerCatalog
{
    private static readonly Regex EastAsianChapterPrefixRegex = new(
        @"^\s*第(?:[0-9]+|[〇零一二三四五六七八九十百千]+)[章話话節节部]\s*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Conservative additions: leading marker + ASCII digits only. A following title
    // requires an explicit separator; bare words and number words remain untouched.
    private static readonly Regex NumericOnlyChapterPrefixRegex = new(
        @"(?i)^\s*(?:" + BuildAlternation(["Kapittel", "Luku", "Bölüm", "Chương", "অধ্যায়"]) +
        @")\s+[0-9]{1,4}(?=$|\s+[-–—:]\s+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] TechnicalMarkers =
    [
        "Kapitel", "Track", "Titel",
        "Chapter", "Ch", "Title",
        "Capítulo", "Capitulo", "Pista", "Título", "Titulo",
        "Chapitre", "Piste", "Titre",
        "Capitolo", "Traccia", "Titolo",
        "Hoofdstuk",
        "Kapitola", "Rozdział", "Rozdzial",
        "Глава", "Κεφάλαιο",
        "CD", "Disc"
    ];

    private static readonly string[] GenericMarkerValues =
    [
        .. TechnicalMarkers,
        "Teil", "Band", "Part", "Volume", "Parte", "Partie", "Tome", "Deel",
        "Část", "Cast", "Díl", "Dil", "Časť", "Diel",
        "Część", "Czesc", "Часть", "Μέρος"
    ];

    private static readonly string[] ExplicitTrackMarkerValues =
    [
        "Kapitel", "Track", "Trk", "Titel",
        "Chapter", "Ch", "Title",
        "Capítulo", "Capitulo", "Pista", "Título", "Titulo",
        "Chapitre", "Piste", "Titre",
        "Capitolo", "Traccia", "Titolo",
        "Hoofdstuk",
        "Kapitola", "Rozdział", "Rozdzial",
        "Глава", "Κεφάλαιο"
    ];

    private static readonly string[] ExplicitDiscMarkerValues =
    [
        "CD", "Disc", "Disk", "Teil", "Band", "Part", "Volume", "Vol",
        "Parte", "Partie", "Tome", "Deel",
        "Část", "Cast", "Díl", "Dil", "Časť", "Diel",
        "Część", "Czesc", "Часть", "Μέρος"
    ];

    private static readonly HashSet<string> TechnicalMarkerSet =
        new(TechnicalMarkers, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> GenericMarkerSet =
        new(GenericMarkerValues, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TechnicalStructureMarkerSet = new(
        ExplicitTrackMarkerValues.Concat(ExplicitDiscMarkerValues),
        StringComparer.OrdinalIgnoreCase);

    private static readonly string ExplicitTrackMarkerRegexAlternation = BuildAlternation(ExplicitTrackMarkerValues);
    private static readonly string ExplicitDiscMarkerRegexAlternation = BuildAlternation(ExplicitDiscMarkerValues);

    internal static Regex ExplicitTrackNumberRegex { get; } = new(
        @"(?i)(?<!\p{L})(?:" + ExplicitTrackMarkerRegexAlternation + @")\.?\s*[-_. ]*(?<number>\d{1,4})(?<suffix>[a-zA-Z]?)(?=$|[^\p{L}\p{N}])|第(?<number>\d{1,4})[章話话節节]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static Regex LeadingExplicitTrackNumberRegex { get; } = new(
        @"(?i)^\s*(?:(?:" + ExplicitTrackMarkerRegexAlternation + @")\.?\s*[-_. ]*(?<number>\d{1,4})(?<suffix>[a-zA-Z]?)(?=$|[^\p{L}\p{N}])|第(?<number>\d{1,4})[章話话節节])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static Regex ExplicitDiscNumberRegex { get; } = new(
        @"(?i)(?<!\p{L})(?:" + ExplicitDiscMarkerRegexAlternation + @")\.?\s*[-_ ]*(?<number>\d{1,4})(?=$|[^\p{L}\p{N}])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TechnicalStructureNameRegex = new(
        @"(?i)^\s*(?:(?:" + ExplicitTrackMarkerRegexAlternation + @")\.?\s*[-_. ]*\d{1,4}[a-zA-Z]?|(?:" + ExplicitDiscMarkerRegexAlternation + @")\.?\s*[-_ ]*\d{1,4}|第\d{1,4}[章話话節节部])\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<string> TechnicalNumberingMarkers { get; } =
        Array.AsReadOnly(TechnicalMarkers);

    public static IReadOnlyList<string> GenericMarkers { get; } =
        Array.AsReadOnly(GenericMarkerValues);

    public static string TechnicalMarkerRegexAlternation { get; } = BuildAlternation(TechnicalMarkers);

    public static string FilenameCopySuffixRegexAlternation { get; } = BuildAlternation(
        ["Kopie", "Copy", "Copia", "Copie", "Kopia", "Копия"]);

    public static bool IsMarkerBoundNumberWord(string marker, string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (IsPortugueseOnlyChapterNumberWord(marker, normalized))
            return true;
        return marker.ToLowerInvariant() switch
        {
            "chapitre" => IsOneOf(normalized, "zéro", "un", "une", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix", "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf", "vingt"),
            "capítulo" or "capitulo" => IsOneOf(normalized, "cero", "uno", "una", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "dieciseis", "diecisiete", "dieciocho", "diecinueve", "veinte"),
            "capitolo" => IsOneOf(normalized, "zero", "uno", "una", "due", "tre", "quattro", "cinque", "sei", "sette", "otto", "nove", "dieci", "undici", "dodici", "tredici", "quattordici", "quindici", "sedici", "diciassette", "diciotto", "diciannove", "venti"),
            "hoofdstuk" => IsOneOf(normalized, "nul", "een", "twee", "drie", "vier", "vijf", "zes", "zeven", "acht", "negen", "tien", "elf", "twaalf", "dertien", "veertien", "vijftien", "zestien", "zeventien", "achttien", "negentien", "twintig"),
            "kapitola" => IsOneOf(normalized, "nula", "jeden", "jedna", "dva", "dvě", "tri", "tři", "čtyři", "pět", "šest", "sedm", "osm", "devět", "deset", "jedenáct", "jedenásť", "dvanáct", "dvanásť", "třináct", "trinásť", "čtrnáct", "štrnásť", "patnáct", "pätnásť", "šestnáct", "šestnásť", "sedmnáct", "sedemnásť", "osmnáct", "osemnásť", "devatenáct", "devätnásť", "dvacet", "dvadsať"),
            "rozdział" or "rozdzial" => IsOneOf(normalized, "zero", "jeden", "dwa", "trzy", "cztery", "pięć", "sześć", "siedem", "osiem", "dziewięć", "dziesięć", "jedenaście", "dwanaście", "trzynaście", "czternaście", "piętnaście", "szesnaście", "siedemnaście", "osiemnaście", "dziewiętnaście", "dwadzieścia"),
            "глава" => IsOneOf(normalized, "ноль", "один", "одна", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять", "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать", "двадцать"),
            "κεφάλαιο" => IsOneOf(normalized, "ένα", "δύο", "τρία", "τέσσερα", "πέντε", "έξι", "επτά", "οκτώ", "εννέα", "δέκα"),
            _ => false
        };
    }

    // zero, cinco, seis and tres are already covered by the existing English/Spanish
    // rules. Only additional Portuguese forms get the conservative boundary guard.
    internal static bool IsPortugueseOnlyChapterNumberWord(string marker, string value) =>
        (string.Equals(marker, "capítulo", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(marker, "capitulo", StringComparison.OrdinalIgnoreCase)) &&
        IsOneOf(value.Trim().ToLowerInvariant(),
            "um", "uma", "dois", "duas", "três", "quatro", "sete", "oito", "nove", "dez",
            "onze", "doze", "treze", "catorze", "quatorze", "quinze", "dezesseis", "dezessete",
            "dezoito", "dezenove", "vinte");

    private static bool IsOneOf(string value, params string[] candidates) =>
        candidates.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static bool TryMatchStructuredChapterPrefix(string value, out int length)
    {
        var match = EastAsianChapterPrefixRegex.Match(value);
        if (!match.Success)
            match = NumericOnlyChapterPrefixRegex.Match(value);
        length = match.Success ? match.Length : 0;
        return match.Success;
    }

    public static bool IsTechnicalNumberingMarker(string? value) =>
        value is not null && TechnicalMarkerSet.Contains(value);

    public static bool IsGenericMarker(string? value) =>
        value is not null && GenericMarkerSet.Contains(value);

    public static bool IsTechnicalStructureName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Trim();
        if (TechnicalStructureMarkerSet.Contains(normalized) ||
            normalized.Equals("MP3", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("AUDIO", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("AUDIOBOOK", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("TRACKS", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ORIGINALS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TechnicalStructureNameRegex.IsMatch(normalized);
    }

    public static bool IsContainerMarker(string? value) =>
        string.Equals(value, "CD", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Disc", StringComparison.OrdinalIgnoreCase);

    private static string BuildAlternation(IEnumerable<string> values) => string.Join(
        "|",
        values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length)
            .ThenBy(value => value, StringComparer.Ordinal)
            .Select(Regex.Escape));
}
