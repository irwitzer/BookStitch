using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterCandidateNormalizerTests
{
    private readonly ChapterCandidateNormalizer _normalizer = new();

    public static TheoryData<string?, string> RequiredNormalizationCases => new()
    {
        { "001_Montagmorgen.mp3", "Montagmorgen" },
        { "001_Montagmorgen.mp3.mp3", "Montagmorgen" },
        { "001_Montagmorgen.MP3.mp3", "Montagmorgen" },
        { "001_Montagmorgen.flac.mp3", "Montagmorgen" },
        { "Track 003 - Montagmittag", "Montagmittag" },
        { "Track003 Montagmittag", "Montagmittag" },
        { "Chapter 004 Montagabend", "Montagabend" },
        { "Kapitel 004 Montagabend", "Montagabend" },
        { "Ch 004 Montagabend", "Montagabend" },
        { "Title 004 Montagabend", "Montagabend" },
        { "Titel 004 Montagabend", "Montagabend" },
        { "CD 2 Track 04 - Dienstagmorgen", "Dienstagmorgen" },
        { "Disc 2 Track 04 Dienstagmorgen", "Dienstagmorgen" },
        { "Chapter IV - Montag", "Montag" },
        { "Kapitel XII Montag", "Montag" },
        { "Chapter One - Monday", "Monday" },
        { "Kapitel Drei - Montag", "Montag" },
        { "Chapter IV", "Chapter" },
        { "Kapitel Drei", "Kapitel" },
        { "001 Montagmorgen", "Montagmorgen" },
        { "001 - Montagmorgen", "Montagmorgen" },
        { "001. Montagmorgen", "Montagmorgen" },
        { "01-03 Montagmorgen", "Montagmorgen" },
        { "01_03_Montagmorgen", "Montagmorgen" },
        { "0101John Grisham Bestechung", "John Grisham Bestechung" },
        { "Montagmorgen 001", "Montagmorgen" },
        { "Montagmorgen - 001", "Montagmorgen" },
        { "Montagmorgen (003)", "Montagmorgen" },
        { "Kapitel 3 Teil 2", "Teil 2" },
        { "Band 7 Kapitel 4 Montag", "Band 7 Montag" },
        { "001 Chapter", "Chapter" },
        { "Chapter 001", "Chapter" },
        { "001 Kapitel", "Kapitel" },
        { "Track 003", "Track" },
        { "CD 2", "" },
        { "Disc 03", "" },
        { "101 Dalmatiner", "101 Dalmatiner" },
        { "1984 - Roman", "1984 - Roman" },
        { "2001 Odyssee", "2001 Odyssee" },
        { "1984", "1984" },
        { "Tag 3", "Tag 3" },
        { "Teil 2", "Teil 2" },
        { "Raum 101", "Raum 101" },
        { "Fahrenheit 451", "Fahrenheit 451" },
        { "11. September", "11. September" },
        { "Montag 3", "Montag 3" },
        { "1Q84", "1Q84" },
        { "Catch22", "Catch22" },
        { "H2O", "H2O" },
        { "001 - Science-Fiction", "Science-Fiction" },
        { "Montag (Ungekürzt)", "Montag (Ungekürzt)" },
        { "   003___Montag   ", "Montag" },
        { "Der    Montag", "Der Montag" },
        { "Der_Montag", "Der Montag" },
        { "The Final Chapter", "The Final Chapter" },
        { "Chapter House", "Chapter House" },
        { "001", "" },
        { "", "" },
        { "   \t  ", "" },
        { null, "" }
    };

    public static TheoryData<string> PortugueseZeroToTwenty => new()
    {
        "zero", "um", "uma", "dois", "duas", "três", "tres", "quatro", "cinco", "seis", "sete",
        "oito", "nove", "dez", "onze", "doze", "treze", "catorze", "quatorze", "quinze",
        "dezesseis", "dezessete", "dezoito", "dezenove", "vinte"
    };

    [Theory]
    [MemberData(nameof(PortugueseZeroToTwenty))]
    public void Normalize_PortugueseZeroToTwentyRequiresChapterMarkerAndPreservesCanonicalUserText(string word)
    {
        foreach (var marker in new[] { "Capítulo", "Capitulo", "CAPÍTULO" })
        {
            var raw = marker + " " + word + " – Chegada";
            foreach (var source in new[] { ChapterCandidateSource.TrackTitle, ChapterCandidateSource.EmbeddedChapterTitle, ChapterCandidateSource.Filename })
            {
                var result = _normalizer.Normalize(source, raw);
                Assert.Equal("Chegada", result.NormalizedText);
                Assert.Equal("Chegada", _normalizer.Normalize(source, result.NormalizedText).NormalizedText);
            }
            Assert.Equal(marker, _normalizer.Normalize(ChapterCandidateSource.TrackTitle, marker + " " + word).NormalizedText);
            Assert.Equal(raw, _normalizer.NormalizeUserDefinedCanonical(ChapterCandidateSource.CanonicalTitle, raw).NormalizedText);
        }
        Assert.Equal(word + " – Chegada", _normalizer.Normalize(ChapterCandidateSource.TrackTitle, word + " – Chegada").NormalizedText);
    }

    [Theory]
    [InlineData("Capítulo vinte e um – Chegada")]
    [InlineData("Capítulo vinte e dois – Chegada")]
    [InlineData("Capítulo cento e um – Chegada")]
    [InlineData("Capítulo duzentos – Chegada")]
    [InlineData("Capítulo um sonho")]
    [InlineData("Capítulo dois mundos")]
    [InlineData("Capítulo dezesseis vozes")]
    [InlineData("Capítulo vinte-e-um – Chegada")]
    [InlineData("Capítulo vinteum – Chegada")]
    [InlineData("Capítulo um:sonho")]
    [InlineData("Chapter dois – Arrival")]
    [InlineData("Chapitre dois – Arrivée")]
    [InlineData("Capitolo dois – Arrivo")]
    [InlineData("Luku dois – Saapuminen")]
    public void Normalize_PortugueseExtensionPreservesCompoundsAmbiguityAndOtherMarkers(string raw)
    {
        Assert.Equal(raw, _normalizer.Normalize(ChapterCandidateSource.TrackTitle, raw).NormalizedText);
    }

    [Theory]
    [InlineData("cero")]
    [InlineData("uno")]
    [InlineData("dos")]
    [InlineData("tres")]
    [InlineData("cuatro")]
    [InlineData("cinco")]
    [InlineData("seis")]
    [InlineData("siete")]
    [InlineData("ocho")]
    [InlineData("nueve")]
    [InlineData("diez")]
    [InlineData("once")]
    [InlineData("doce")]
    [InlineData("trece")]
    [InlineData("catorce")]
    [InlineData("quince")]
    [InlineData("dieciséis")]
    [InlineData("diecisiete")]
    [InlineData("dieciocho")]
    [InlineData("diecinueve")]
    [InlineData("veinte")]
    public void Normalize_SpanishZeroToTwentyRemainsUnchanged(string word)
    {
        Assert.Equal("Llegada", _normalizer.Normalize(ChapterCandidateSource.TrackTitle, "Capítulo " + word + " – Llegada").NormalizedText);
    }

    [Theory]
    [InlineData("Kapittel 12 – Ankomst", "Ankomst")]
    [InlineData("Luku 12 – Saapuminen", "Saapuminen")]
    [InlineData("Bölüm 12 - Varış", "Varış")]
    [InlineData("Chương 12 : Đến nơi", "Đến nơi")]
    [InlineData("অধ্যায় 12 - আগমন", "আগমন")]
    [InlineData("Luku 12", "")]
    [InlineData("BÖLÜM 003 – Başlangıç", "Başlangıç")]
    public void Normalize_NewNumericOnlyChapterPrefixesRequireUnambiguousStructure(string raw, string expected)
    {
        foreach (var source in new[] { ChapterCandidateSource.TrackTitle, ChapterCandidateSource.EmbeddedChapterTitle, ChapterCandidateSource.Filename })
        {
            var result = _normalizer.Normalize(source, raw);
            Assert.Equal(expected, result.NormalizedText);
            Assert.Equal(expected, _normalizer.Normalize(source, result.NormalizedText).NormalizedText);
        }
    }

    [Theory]
    [InlineData("Luku")]
    [InlineData("Bölüm")]
    [InlineData("Chương")]
    [InlineData("অধ্যায়")]
    [InlineData("Kapittel")]
    [InlineData("Luku three – A story")]
    [InlineData("Luku yksi – Saapuminen")]
    [InlineData("Bölüm üç - Varış")]
    [InlineData("Chương một - Đến nơi")]
    [InlineData("অধ্যায় এক - আগমন")]
    [InlineData("Luku 12 vuotta")]
    [InlineData("Luku 12A – Saapuminen")]
    [InlineData("Luku 12345 – Saapuminen")]
    [InlineData("Luku ১২ – Saapuminen")]
    [InlineData("Bölümler 12 – Varış")]
    [InlineData("Minun Luku 12 – tarinani")]
    [InlineData("Chapitre de ma vie")]
    [InlineData("Capítulo de mi vida")]
    [InlineData("Capítulo vinte e um – Chegada")]
    public void Normalize_ConservativePatchPreservesBareMarkersNumberWordsAndSemanticTitles(string raw)
    {
        Assert.Equal(raw, _normalizer.Normalize(ChapterCandidateSource.TrackTitle, raw).NormalizedText);
    }

    [Theory]
    [MemberData(nameof(RequiredNormalizationCases))]
    public void Normalize_CoversRequiredMatrixAndIsIdempotent(string? rawText, string expected)
    {
        var first = _normalizer.Normalize(ChapterCandidateSource.TrackTitle, rawText);
        var second = _normalizer.Normalize(ChapterCandidateSource.TrackTitle, first.NormalizedText);

        Assert.Equal(expected, first.NormalizedText);
        Assert.Equal(first.NormalizedText, second.NormalizedText);
        Assert.Equal(first.ComparisonKey, second.ComparisonKey);
    }

    [Theory]
    [InlineData(ChapterCandidateSource.EmbeddedChapterTitle)]
    [InlineData(ChapterCandidateSource.TrackTitle)]
    [InlineData(ChapterCandidateSource.Filename)]
    [InlineData(ChapterCandidateSource.CanonicalAlbum)]
    [InlineData(ChapterCandidateSource.CanonicalTitle)]
    [InlineData(ChapterCandidateSource.AuthorArtistContext)]
    public void Normalize_AppliesCoreRulesEquallyAcrossSources(ChapterCandidateSource source)
    {
        var result = _normalizer.Normalize(source, "001 Montag.mp3");

        Assert.Equal("Montag", result.NormalizedText);
    }

    [Fact]
    public void Normalize_FilenameUsesOnlyBasename()
    {
        var result = _normalizer.Normalize(
            ChapterCandidateSource.Filename,
            @"C:\Books\Harry Potter\001_Montag.mp3");

        Assert.Equal("Montag", result.NormalizedText);
        Assert.DoesNotContain("Books", result.NormalizedText, StringComparison.Ordinal);
        Assert.Contains(result.AppliedTransforms, transform =>
            transform.Kind == ChapterNormalizationTransformKind.ExtractedFilename);
    }

    [Theory]
    [InlineData("Deutsch", "Eckhart Omama - Kopie.mp3")]
    [InlineData("Deutsch nummeriert", "Eckhart Omama - Kopie (2).mp3")]
    [InlineData("Englisch", "Eckhart Omama - Copy.mp3")]
    [InlineData("Englisch nummeriert", "Eckhart Omama - Copy (3).mp3")]
    [InlineData("Spanisch", "Eckhart Omama - Copia.mp3")]
    [InlineData("Spanisch nummeriert", "Eckhart Omama - Copia (2).mp3")]
    [InlineData("Französisch", "Eckhart Omama - Copie.mp3")]
    [InlineData("Französisch nummeriert", "Eckhart Omama - Copie (2).mp3")]
    [InlineData("Italienisch", "Eckhart Omama - Copia.mp3")]
    [InlineData("Italienisch nummeriert", "Eckhart Omama - Copia (4).mp3")]
    [InlineData("Niederländisch", "Eckhart Omama - Kopie.mp3")]
    [InlineData("Niederländisch nummeriert", "Eckhart Omama - Kopie (5).mp3")]
    public void Normalize_FilenameRemovesSupportedCopySuffixes(string languageCase, string rawText)
    {
        Assert.False(string.IsNullOrWhiteSpace(languageCase));
        var result = _normalizer.Normalize(ChapterCandidateSource.Filename, rawText);

        Assert.Equal("Eckhart Omama", result.NormalizedText);
        Assert.Equal(rawText, result.RawText);
        Assert.Contains(result.AppliedTransforms, transform =>
            transform.Kind == ChapterNormalizationTransformKind.RemovedCopySuffix);
    }

    [Fact]
    public void Normalize_CopySuffixRuleDoesNotApplyToNonFilenameEvidence()
    {
        var result = _normalizer.Normalize(
            ChapterCandidateSource.TrackTitle,
            "Eckhart Omama - Copy");

        Assert.Equal("Eckhart Omama - Copy", result.NormalizedText);
        Assert.DoesNotContain(result.AppliedTransforms, transform =>
            transform.Kind == ChapterNormalizationTransformKind.RemovedCopySuffix);
    }

    [Theory]
    [InlineData("Die Kopie des Briefes.mp3", "Die Kopie des Briefes")]
    [InlineData("A Copy of My Life.mp3", "A Copy of My Life")]
    [InlineData("La copia perdida.mp3", "La copia perdida")]
    public void Normalize_FilenamePreservesSemanticCopyWords(string rawText, string expected)
    {
        var result = _normalizer.Normalize(ChapterCandidateSource.Filename, rawText);

        Assert.Equal(expected, result.NormalizedText);
        Assert.DoesNotContain(result.AppliedTransforms, transform =>
            transform.Kind == ChapterNormalizationTransformKind.RemovedCopySuffix);
    }

    [Fact]
    public void Normalize_CopyArtifactsShareOneFilenameAnalysisGroup()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.Filename, "Eckhart Omama.mp3", 1),
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.Filename, "Eckhart Omama - Kopie.mp3", 2),
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.Filename, "Eckhart Omama - Kopie (2).mp3", 3));

        var result = new ChapterCandidateAnalysisService().Analyze(scope);

        Assert.Single(result.Candidates.Select(candidate => candidate.AnalysisGroupId).Distinct());
        Assert.All(result.Candidates, candidate =>
            Assert.Equal(ChapterCandidateQuality.UniformMeaningful, candidate.Quality));
    }

    [Fact]
    public void Normalize_NonFilenameSourceDoesNotInterpretPathSegments()
    {
        var result = _normalizer.Normalize(
            ChapterCandidateSource.TrackTitle,
            @"C:\Books\Harry Potter\001_Montag.mp3");

        Assert.Equal(@"C:\Books\Harry Potter\001 Montag", result.NormalizedText);
    }

    [Theory]
    [InlineData("Chapter Nine Hundred Ninety-Nine - Finale", "Finale")]
    [InlineData("Kapitel neunhundertneunundneunzig - Finale", "Finale")]
    [InlineData("Chapter IIV Montag", "Chapter IIV Montag")]
    [InlineData("Chapter VX Montag", "Chapter VX Montag")]
    [InlineData("One Summer", "One Summer")]
    [InlineData("Drei Tage", "Drei Tage")]
    public void Normalize_NumberWordsAndRomanNumeralsStayStrict(string rawText, string expected)
    {
        var result = _normalizer.Normalize(ChapterCandidateSource.TrackTitle, rawText);

        Assert.Equal(expected, result.NormalizedText);
    }

    [Theory]
    [InlineData("1John", "1John")]
    [InlineData("12Monkeys", "12Monkeys")]
    [InlineData("Montag001", "Montag001")]
    [InlineData("Montag.mp3x", "Montag.mp3x")]
    public void Normalize_PreservesAmbiguousAlphanumericAndUnsupportedSuffixes(string rawText, string expected)
    {
        var result = _normalizer.Normalize(ChapterCandidateSource.TrackTitle, rawText);

        Assert.Equal(expected, result.NormalizedText);
    }

    [Fact]
    public void Normalize_RemovesEverySupportedAudioExtensionRecursively()
    {
        var suffixes = string.Concat(AudioInputFormatCatalog.All.Select(format => format.Extension));

        var result = _normalizer.Normalize(
            ChapterCandidateSource.TrackTitle,
            "001_Montag" + suffixes);

        Assert.Equal("Montag", result.NormalizedText);
        Assert.Equal(
            AudioInputFormatCatalog.All.Count,
            result.AppliedTransforms.Count(transform =>
                transform.Kind == ChapterNormalizationTransformKind.RemovedAudioExtension));
    }

    [Fact]
    public void Normalize_PreservesRawTextExactlyIncludingNull()
    {
        const string rawText = "  001_Montag.mp3  ";

        var result = _normalizer.Normalize(ChapterCandidateSource.Filename, rawText);
        var nullResult = _normalizer.Normalize(ChapterCandidateSource.TrackTitle, null);

        Assert.Same(rawText, result.RawText);
        Assert.Null(nullResult.RawText);
    }

    [Fact]
    public void Normalize_ProducesStableDiagnosticsForComplexInput()
    {
        var result = _normalizer.Normalize(
            ChapterCandidateSource.Filename,
            "003___Montag.mp3.mp3");

        Assert.Equal(2, result.AppliedTransforms.Count(transform =>
            transform.Kind == ChapterNormalizationTransformKind.RemovedAudioExtension));
        Assert.Contains(result.AppliedTransforms, transform =>
            transform.Kind == ChapterNormalizationTransformKind.ReplacedUnderscores);
        Assert.Contains(result.AppliedTransforms, transform =>
            transform.Kind == ChapterNormalizationTransformKind.RemovedNumericPrefix &&
            transform.Value == "003");
        Assert.Contains(result.AppliedTransforms, transform =>
            transform.Kind == ChapterNormalizationTransformKind.CollapsedWhitespace);
    }

    [Fact]
    public void Normalize_UsesNfcAndRemovesControlsWithoutChangingVisibleCasing()
    {
        var result = _normalizer.Normalize(
            ChapterCandidateSource.TrackTitle,
            "  DER\u0001 Cafe\u0301\tMontag  ");

        Assert.Equal("DER Café Montag", result.NormalizedText);
        Assert.Equal("der café montag", result.ComparisonKey);
    }

    [Theory]
    [InlineData("Capítulo 3 - Lunes", "Lunes")]
    [InlineData("Piste 3 - Lundi", "Lundi")]
    [InlineData("Capitolo 3 - Lunedì", "Lunedì")]
    [InlineData("Hoofdstuk 3 - Maandag", "Maandag")]
    public void Normalize_RemovesMultilingualTechnicalMarkers(string rawText, string expected)
    {
        var result = _normalizer.Normalize(ChapterCandidateSource.TrackTitle, rawText);

        Assert.Equal(expected, result.NormalizedText);
    }

    [Theory]
    [InlineData("Chapitre 3 - L'arrivée", "L'arrivée")]
    [InlineData("Глава 3 - Прибытие", "Прибытие")]
    [InlineData("Hoofdstuk 3 - Aankomst", "Aankomst")]
    [InlineData("Rozdział 3 - Przybycie", "Przybycie")]
    [InlineData("Kapitola 3 - Příchod", "Příchod")]
    [InlineData("Capítulo 3 - Llegada", "Llegada")]
    [InlineData("Capitolo 3 - Arrivo", "Arrivo")]
    [InlineData("Kapitola 3 - Príchod", "Príchod")]
    [InlineData("Κεφάλαιο 3 - Άφιξη", "Άφιξη")]
    [InlineData("第1章 标题", "标题")]
    [InlineData("第1章标题", "标题")]
    [InlineData("第一章 タイトル", "タイトル")]
    [InlineData("第一章タイトル", "タイトル")]
    [InlineData("第1話はじまり", "はじまり")]
    [InlineData("第1节序言", "序言")]
    public void Normalize_RemovesUnambiguousInternationalChapterPrefixes(string rawText, string expected)
    {
        var result = _normalizer.Normalize(ChapterCandidateSource.TrackTitle, rawText);

        Assert.Equal(expected, result.NormalizedText);
    }

    [Theory]
    [InlineData("星")]
    [InlineData("夜の章")]
    [InlineData("Глава семьи")]
    [InlineData("Μέρος της ζωής")]
    [InlineData("1标题")]
    public void Normalize_PreservesSemanticAndAmbiguousInternationalTitles(string rawText)
    {
        var result = _normalizer.Normalize(ChapterCandidateSource.TrackTitle, rawText);

        Assert.Equal(rawText, result.NormalizedText);
    }

    [Theory]
    [InlineData("Chapitre trois - Arrivée", "Arrivée")]
    [InlineData("Глава три - Прибытие", "Прибытие")]
    [InlineData("Rozdział trzy - Przybycie", "Przybycie")]
    [InlineData("Kapitola tři - Příchod", "Příchod")]
    public void Normalize_RecognizesConservativeMarkerBoundNumberWords(string rawText, string expected)
    {
        Assert.Equal(expected, _normalizer.Normalize(ChapterCandidateSource.TrackTitle, rawText).NormalizedText);
    }

    [Theory]
    [InlineData(ChapterCandidateSource.CanonicalTitle, "  Chapter 11.mp3\t")]
    [InlineData(ChapterCandidateSource.CanonicalAlbum, "  Track 03 - Album.flac\t")]
    public void NormalizeUserDefinedCanonical_PreservesSemanticProjectText(
        ChapterCandidateSource source,
        string rawText)
    {
        var result = _normalizer.NormalizeUserDefinedCanonical(source, rawText);

        Assert.Equal(rawText.Trim(), result.NormalizedText);
        Assert.DoesNotContain(result.AppliedTransforms, transform =>
            transform.Kind == ChapterNormalizationTransformKind.RemovedTechnicalNumbering ||
            transform.Kind == ChapterNormalizationTransformKind.RemovedAudioExtension);
    }

    [Fact]
    public void NormalizeUserDefinedCanonical_RejectsSourceEvidenceKinds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _normalizer.NormalizeUserDefinedCanonical(ChapterCandidateSource.TrackTitle, "Chapter 11"));
    }
}
