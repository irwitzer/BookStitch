using System.Text.Json;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterAnalysisCorpusTests
{
    private readonly ChapterTechnicalNumberingAnalyzer _numberingAnalyzer = new();
    private readonly ChapterGenericClassifier _genericClassifier = new();

    [Fact]
    public void SyntheticSpecificationCorpus_RemainsExplicitlyLabeledAndPasses()
    {
        var corpus = LoadCorpus();

        Assert.Equal(1, corpus.SchemaVersion);
        Assert.Equal("synthetic-specification", corpus.Provenance);
        Assert.True(corpus.NumberingFamilies.Count >= 10);
        Assert.True(corpus.GenericCases.Count >= 30);

        foreach (var family in corpus.NumberingFamilies)
        {
            Assert.Equal(family.Values.Count, family.TrackNumbers.Count);
            Assert.Equal(family.Values.Count, family.Expected.Count);
            var scope = ChapterAnalysisTestFactory.Scope(family.Values
                .Select((value, index) => (value, family.TrackNumbers[index]))
                .ToArray());

            var actual = _numberingAnalyzer.Analyze(scope)
                .Select(result => result.OutputBaseText)
                .ToArray();

            Assert.True(
                family.Expected.SequenceEqual(actual, StringComparer.Ordinal),
                $"Corpus family '{family.Name}' differed. Expected: [{string.Join(", ", family.Expected)}], actual: [{string.Join(", ", actual)}].");
        }

        foreach (var item in corpus.GenericCases)
        {
            Assert.Equal(
                item.ExpectedGeneric,
                _genericClassifier.IsGeneric(item.Value));
        }
    }

    private static ChapterAnalysisCorpus LoadCorpus()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ChapterAnalysisCorpus.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ChapterAnalysisCorpus>(
                   json,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("Chapter analysis corpus could not be read.");
    }

    private sealed class ChapterAnalysisCorpus
    {
        public int SchemaVersion { get; set; }
        public string Provenance { get; set; } = string.Empty;
        public List<NumberingFamily> NumberingFamilies { get; set; } = [];
        public List<GenericCase> GenericCases { get; set; } = [];
    }

    private sealed class NumberingFamily
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Values { get; set; } = [];
        public List<int?> TrackNumbers { get; set; } = [];
        public List<string> Expected { get; set; } = [];
    }

    private sealed class GenericCase
    {
        public string Value { get; set; } = string.Empty;
        public bool ExpectedGeneric { get; set; }
    }
}
