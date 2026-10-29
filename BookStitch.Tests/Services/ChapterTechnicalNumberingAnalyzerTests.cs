using System.Globalization;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterTechnicalNumberingAnalyzerTests
{
    [Theory]
    [InlineData(8, 2, " - ", "", "")]
    [InlineData(98, 3, " - ", "", "")]
    [InlineData(998, 4, " - ", "", "")]
    [InlineData(9998, 5, " - ", "", "")]
    [InlineData(98, 3, " – ", "", "")]
    [InlineData(998, 4, "\t—\t", "", "")]
    [InlineData(98, 3, " - ", "(", ")")]
    [InlineData(998, 4, " - ", "[", "]")]
    public void Analyze_SpacedDashFamilyRemainsOneFamilyAcrossPaddingBoundary(
        int start, int width, string separator, string open, string close)
    {
        var scope = ChapterAnalysisTestFactory.Scope(Enumerable.Range(start, 5)
            .Select(number => ($"{open}{number.ToString("D" + width, CultureInfo.InvariantCulture)}{close}{separator}Armageddon", (int?)number))
            .ToArray());
        var results = _analyzer.Analyze(scope);
        Assert.All(results, result =>
        {
            Assert.Equal("Armageddon", result.OutputBaseText);
            Assert.True(result.Evidence.FamilyDetected);
        });
    }

    [Theory]
    [InlineData(125, 126, 127, true)]
    [InlineData(null, null, null, false)]
    [InlineData(1, 2, 3, false)]
    [InlineData(125, 126, 128, false)]
    public void Analyze_MixedPureAndNamedOrdinalsRequireMatchingSourceEvidence(
        int? first, int? second, int? third, bool expectedFamily)
    {
        var results = _analyzer.Analyze(ChapterAnalysisTestFactory.Scope(
            ("125 Zweiter Teil Kapitel 34", first),
            ("126 Kapitel 34", second),
            ("127 Kapitel 35", third)));

        Assert.All(results, result => Assert.Equal(expectedFamily, result.Evidence.FamilyDetected));
        Assert.Equal(expectedFamily ? "Zweiter Teil" : "125 Zweiter Teil", results[0].OutputBaseText);
        Assert.Equal(!expectedFamily, results[1].IsUsable);
        Assert.Equal(!expectedFamily, results[2].IsUsable);
    }

    private readonly ChapterTechnicalNumberingAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_StripsStrictLeadingFamilyWithoutChangingNormalizedText()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            ("1 - Prolog", null),
            ("2 - Reise", null),
            ("3 - Ende", null));

        var results = _analyzer.Analyze(scope);

        Assert.Equal(["1 - Prolog", "2 - Reise", "3 - Ende"],
            results.Select(result => result.Candidate.Normalization.NormalizedText));
        Assert.Equal(["Prolog", "Reise", "Ende"], results.Select(result => result.OutputBaseText));
        Assert.All(results, result => Assert.True(result.Evidence.FamilyDetected));
    }

    [Fact]
    public void Analyze_StripsSeparatorlessCjkNumbersOnlyForAConsistentFamily()
    {
        var family = _analyzer.Analyze(ChapterAnalysisTestFactory.Scope(
            ("1标题", null),
            ("2旅程", null),
            ("3结尾", null)));
        var isolated = _analyzer.Analyze(ChapterAnalysisTestFactory.Scope(("1标题", null)));

        Assert.Equal(["标题", "旅程", "结尾"], family.Select(result => result.OutputBaseText));
        Assert.All(family, result => Assert.True(result.Evidence.FamilyDetected));
        Assert.Equal("1标题", Assert.Single(isolated).OutputBaseText);
    }

    [Fact]
    public void Analyze_PureNumericOrdinalFamilyBecomesUnusable()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            ("1", 1),
            ("2", 2),
            ("3", 3),
            ("4", 4));

        var results = _analyzer.Analyze(scope);

        Assert.All(results, result =>
        {
            Assert.False(result.IsUsable);
            Assert.Equal(string.Empty, result.AnalysisText);
            Assert.Equal(ChapterTechnicalNumberingKind.PureNumber, result.Evidence.Kind);
        });
    }

    [Theory]
    [InlineData("(", ")", 8, 5, 2)]
    [InlineData("[", "]", 98, 5, 3)]
    [InlineData("{", "}", 998, 5, 4)]
    [InlineData("(", ")", 9998, 5, 5)]
    public void Analyze_WrappedPureOrdinalFamilyIgnoresPaddingWidthAcrossDigitBoundaries(
        string open,
        string close,
        int start,
        int count,
        int paddingWidth)
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            Enumerable.Range(start, count)
                .Select(number => (
                    $"{open}{number.ToString("D" + paddingWidth, CultureInfo.InvariantCulture)}{close}",
                    (int?)number))
                .ToArray());

        var results = _analyzer.Analyze(scope);

        Assert.Equal(count, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.IsUsable);
            Assert.Equal(string.Empty, result.AnalysisText);
            Assert.Equal(string.Empty, result.OutputBaseText);
            Assert.Equal(ChapterTechnicalNumberingKind.PureNumber, result.Evidence.Kind);
            Assert.True(result.Evidence.FamilyDetected);
        });

        for (var index = 0; index < count; index++)
            Assert.Equal(start + index, results[index].Evidence.RemovedNumber);
    }

    [Fact]
    public void Analyze_WrappedLeadingFamilyIgnoresPaddingWidthAt9To10()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            ("(08) Prolog", 8),
            ("(09) Reise", 9),
            ("(10) Wendepunkt", 10),
            ("(11) Ende", 11));

        var results = _analyzer.Analyze(scope);

        Assert.Equal(
            ["Prolog", "Reise", "Wendepunkt", "Ende"],
            results.Select(result => result.OutputBaseText));
        Assert.All(results, result => Assert.True(result.Evidence.FamilyDetected));
    }

    [Fact]
    public void Analyze_WrappedNumericFamilyDoesNotBridgeGap()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            ("(08)", 8),
            ("(09)", 9),
            ("(11)", 11));

        var results = _analyzer.Analyze(scope);

        Assert.All(results, result => Assert.False(result.Evidence.FamilyDetected));
    }
    [Fact]
    public void Analyze_TwoCandidatesRequireExplicitOrdinalEvidence()
    {
        var withoutOrdinals = _analyzer.Analyze(ChapterAnalysisTestFactory.Scope(
            ("1 - One", null),
            ("2 - Two", null)));
        var withOrdinals = _analyzer.Analyze(ChapterAnalysisTestFactory.Scope(
            ("1 - One", 1),
            ("2 - Two", 2)));

        Assert.Equal(["1 - One", "2 - Two"], withoutOrdinals.Select(result => result.OutputBaseText));
        Assert.Equal(["One", "Two"], withOrdinals.Select(result => result.OutputBaseText));
    }

    [Fact]
    public void Analyze_DoesNotBridgeMissingScopeMembers()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.TrackTitle, "1 - One", 1),
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.TrackTitle, "2 - Two", 3),
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.TrackTitle, "3 - Three", 4));

        var results = _analyzer.Analyze(scope);

        Assert.Equal(["1 - One", "2 - Two", "3 - Three"], results.Select(result => result.OutputBaseText));
    }

    [Fact]
    public void Analyze_StripsOneContinuousFamilyAcrossTheLeadingZeroBoundary()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            Enumerable.Range(1, 224)
                .Select(index => ($"{index:000}_NSA - Nationales Sicherheits-Amt", (int?)index))
                .ToArray());

        var results = _analyzer.Analyze(scope);

        Assert.Equal(224, results.Count);
        Assert.All(results, result => Assert.Equal("NSA - Nationales Sicherheits-Amt", result.OutputBaseText));
        Assert.All(results, result => Assert.True(result.Evidence.FamilyDetected));
    }

    [Fact]
    public void Analyze_StripsReorderedDuplicatedFamilyAcrossTheLeadingZeroBoundary()
    {
        var specs = Enumerable.Range(1, 130)
            .SelectMany(index => new[]
            {
                ChapterAnalysisTestFactory.Candidate(
                    ChapterCandidateSource.Filename,
                    $"{index:000}_King_Billy_Summers.mp3",
                    index * 2 - 1,
                    trackNumber: index),
                ChapterAnalysisTestFactory.Candidate(
                    ChapterCandidateSource.Filename,
                    $"{index:000}_King_Billy_Summers.mp3",
                    index * 2,
                    trackNumber: index)
            })
            .OrderBy(spec => spec.MemberNumber % 2)
            .ThenBy(spec => spec.MemberNumber)
            .Select((spec, index) => spec with { MemberNumber = index + 1 })
            .ToArray();
        var scope = ChapterAnalysisTestFactory.Scope(specs);

        var results = _analyzer.Analyze(scope);

        Assert.All(results, result => Assert.Equal("King Billy Summers", result.OutputBaseText));
        Assert.DoesNotContain(results, result => result.OutputBaseText.StartsWith("100 ", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_GenericTrackTitleFamilyCrossing100TreatsPureNumberTailAsTechnical()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            Enumerable.Range(1, 112)
                .Select(index =>
                {
                    var sourceOrdinal = index >= 107 ? index - 1 : index;
                    var chapterNumber = Math.Min(36, ((index - 1) / 3) + 1);
                    return ChapterAnalysisTestFactory.Candidate(
                        ChapterCandidateSource.TrackTitle,
                        $"{sourceOrdinal:000} Kapitel {chapterNumber}",
                        index,
                        trackNumber: index);
                })
                .ToArray());

        var results = _analyzer.Analyze(scope);

        Assert.Equal(112, results.Count);
        Assert.All(results.Take(99), result =>
        {
            Assert.True(result.IsUsable);
            Assert.Equal("Kapitel", result.OutputBaseText);
            Assert.True(result.Evidence.FamilyDetected);
        });
        Assert.All(results.Skip(99), result =>
        {
            Assert.False(result.IsUsable);
            Assert.Equal(string.Empty, result.OutputBaseText);
            Assert.Equal(ChapterTechnicalNumberingKind.PureNumber, result.Evidence.Kind);
            Assert.True(result.Evidence.FamilyDetected);
        });
        Assert.DoesNotContain(results, result => result.OutputBaseText == "100");
        Assert.DoesNotContain(results, result => result.OutputBaseText == "106");
    }

    [Fact]
    public void Analyze_PreservesSemanticNumericTitlesOutsideTechnicalBoundaryFamilies()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.TrackTitle,
                "101 Dalmatiner",
                1,
                trackNumber: 1),
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.TrackTitle,
                "101 Dalmatiner",
                2,
                trackNumber: 2),
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.TrackTitle,
                "1984",
                3,
                trackNumber: 3));

        var results = _analyzer.Analyze(scope);

        Assert.Equal(["101 Dalmatiner", "101 Dalmatiner", "1984"],
            results.Select(result => result.OutputBaseText));
        Assert.All(results, result => Assert.False(result.Evidence.FamilyDetected));
    }

    [Fact]
    public void Analyze_StructuredLongNumericFilenameFamilyBecomesUnusableWhenNoSemanticTextRemains()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            Enumerable.Range(1, 112)
                .Select(index => ChapterAnalysisTestFactory.Candidate(
                    ChapterCandidateSource.Filename,
                    $"9783837165128_{index:000}_225.mp3",
                    index,
                    trackNumber: index))
                .ToArray());

        var results = _analyzer.Analyze(scope);

        Assert.Equal(112, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.IsUsable);
            Assert.Equal(string.Empty, result.AnalysisText);
            Assert.Equal(string.Empty, result.OutputBaseText);
            Assert.Equal(ChapterTechnicalNumberingKind.StructuredNumericIdentifier, result.Evidence.Kind);
            Assert.True(result.Evidence.FamilyDetected);
            Assert.True(result.Evidence.OrdinalCorrelated);
        });
    }

    [Fact]
    public void Analyze_StructuredLongNumericFilenameFamilyKeepsSemanticSuffixOnly()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.Filename,
                "9783837165128_001_225_Prolog.mp3",
                1,
                trackNumber: 1),
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.Filename,
                "9783837165128_002_225_Die_Reise.mp3",
                2,
                trackNumber: 2),
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.Filename,
                "9783837165128_003_225_Epilog.mp3",
                3,
                trackNumber: 3));

        var results = _analyzer.Analyze(scope);

        Assert.Equal(["Prolog", "Die Reise", "Epilog"], results.Select(result => result.OutputBaseText));
        Assert.All(results, result => Assert.Equal(
            ChapterTechnicalNumberingKind.StructuredNumericIdentifier,
            result.Evidence.Kind));
    }

    [Fact]
    public void Analyze_LongNumericOnlyFilenameOrTrackTitleIsUnusableWithoutIsbnSpecificLogic()
    {
        var filename = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.Filename,
                "1234567890123.mp3",
                1));
        var trackTitle = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.TrackTitle,
                "1234567890123",
                1));

        var filenameResult = Assert.Single(_analyzer.Analyze(filename));
        var trackTitleResult = Assert.Single(_analyzer.Analyze(trackTitle));

        Assert.False(filenameResult.IsUsable);
        Assert.False(trackTitleResult.IsUsable);
        Assert.Equal(ChapterTechnicalNumberingKind.LongNumericIdentifier, filenameResult.Evidence.Kind);
        Assert.Equal(ChapterTechnicalNumberingKind.LongNumericIdentifier, trackTitleResult.Evidence.Kind);
    }

    [Fact]
    public void Analyze_LeadingLongNumericIdentifierIsRemovedWhenSemanticTextFollows()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.Filename,
                "1234567890123_Prolog.mp3",
                1));

        var result = Assert.Single(_analyzer.Analyze(scope));

        Assert.True(result.IsUsable);
        Assert.Equal("Prolog", result.OutputBaseText);
        Assert.Equal(ChapterTechnicalNumberingKind.LongNumericIdentifier, result.Evidence.Kind);
    }

    [Fact]
    public void Analyze_PreservesShortNumericSemanticTitlesAndYearInStructuredSuffix()
    {
        var numericTitles = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.Filename, "1984.mp3", 1),
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.Filename, "2001.mp3", 2));
        var structured = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.Filename,
                "9783837165128_001_225_1984_Roman.mp3",
                1,
                trackNumber: 1),
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.Filename,
                "9783837165128_002_225_1984_Roman.mp3",
                2,
                trackNumber: 2));

        var numericResults = _analyzer.Analyze(numericTitles);
        var structuredResults = _analyzer.Analyze(structured);

        Assert.Equal(["1984", "2001"], numericResults.Select(result => result.OutputBaseText));
        Assert.Equal(["1984 Roman", "1984 Roman"], structuredResults.Select(result => result.OutputBaseText));
    }

    [Fact]
    public void Analyze_DoesNotStripAmbiguousStructuredNumericPrefixWithoutProvenOrdinal()
    {
        var scope = ChapterAnalysisTestFactory.Scope(
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.Filename,
                "9783837165128_010_225_Prolog.mp3",
                1),
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.Filename,
                "9783837165128_020_225_Reise.mp3",
                2),
            ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.Filename,
                "9783837165128_030_225_Epilog.mp3",
                3));

        var results = _analyzer.Analyze(scope);

        Assert.Equal(
            ["9783837165128 010 225 Prolog", "9783837165128 020 225 Reise", "9783837165128 030 225 Epilog"],
            results.Select(result => result.OutputBaseText));
        Assert.All(results, result => Assert.False(result.Evidence.FamilyDetected));
    }

    [Theory]
    [MemberData(nameof(PreservedFamilies))]
    public void Analyze_PreservesSemanticAndUnsupportedFamilies(string[] texts)
    {
        var scope = ChapterAnalysisTestFactory.Scope(texts.Select(text => (text, (int?)null)).ToArray());

        var results = _analyzer.Analyze(scope);

        Assert.Equal(texts, results.Select(result => result.OutputBaseText));
        Assert.All(results, result => Assert.False(result.Evidence.FamilyDetected));
    }

    public static TheoryData<string[]> PreservedFamilies => new()
    {
        new[] { "Tag 1", "Tag 2", "Tag 3" },
        new[] { "Teil 1", "Teil 2", "Teil 3" },
        new[] { "Band 7", "Band 8", "Band 9" },
        new[] { "1914", "1915", "1916", "1917" },
        new[] { "(1914)", "(1915)", "(1916)", "(1917)" },
        new[] { "1 - One", "3 - Three", "4 - Four" },
        new[] { "1 - One", "2. Two", "3 - Three" },
        new[] { "Raum 101", "Raum 102", "Raum 103" },
        new[] { "11. September", "12. September", "13. September" }
    };
}
