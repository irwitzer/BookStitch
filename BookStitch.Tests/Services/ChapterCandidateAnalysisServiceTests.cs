using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterCandidateAnalysisServiceTests
{
    private readonly ChapterCandidateAnalysisService _service = new();

    [Theory]
    [InlineData(61, 39, 0)]
    [InlineData(60, 40, 40)]
    [InlineData(51, 49, 49)]
    [InlineData(50, 50, 100)]
    public void Analyze_AppliesDominanceAndDiversityBoundaries(
        int firstCount,
        int secondCount,
        int expectedIndividualCount)
    {
        var scope = BuildCountScope(("Group A", firstCount), ("Group B", secondCount));

        var result = _service.Analyze(scope);

        Assert.Equal(
            expectedIndividualCount,
            result.Candidates.Count(candidate => candidate.Quality == ChapterCandidateQuality.IndividualMeaningful));
    }

    [Fact]
    public void Analyze_FortyThirtyThirtyMakesAllMeaningfulGroupsIndividual()
    {
        var result = _service.Analyze(BuildCountScope(
            ("Group A", 40),
            ("Group B", 30),
            ("Group C", 30)));

        Assert.All(result.Candidates, candidate =>
            Assert.Equal(ChapterCandidateQuality.IndividualMeaningful, candidate.Quality));
    }

    [Fact]
    public void Analyze_OneHundredDifferentValuesAreIndividual()
    {
        var specs = Enumerable.Range(1, 100)
            .Select(index => ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.TrackTitle,
                "Distinct passage " + index,
                index))
            .ToArray();

        var result = _service.Analyze(ChapterAnalysisTestFactory.Scope(specs));

        Assert.All(result.Candidates, candidate =>
            Assert.Equal(ChapterCandidateQuality.IndividualMeaningful, candidate.Quality));
    }

    [Theory]
    [MemberData(nameof(SmallScopeCases))]
    public void Analyze_AppliesSmallScopeRules(string[] values, int expectedIndividuals)
    {
        var specs = values.Select((value, index) =>
            ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.TrackTitle, value, index + 1)).ToArray();

        var result = _service.Analyze(ChapterAnalysisTestFactory.Scope(specs));

        Assert.Equal(
            expectedIndividuals,
            result.Candidates.Count(candidate => candidate.Quality == ChapterCandidateQuality.IndividualMeaningful));
    }

    public static TheoryData<string[], int> SmallScopeCases => new()
    {
        { new[] { "One" }, 0 },
        { new[] { "Same", "Same" }, 0 },
        { new[] { "One", "Two" }, 2 },
        { new[] { "Same", "Same", "Other" }, 0 },
        { new[] { "One", "Two", "Three" }, 3 }
    };

    [Fact]
    public void Analyze_ExcludesGenericCandidatesFromMeaningfulDiversity()
    {
        var specs = new List<ChapterAnalysisTestFactory.CandidateSpec>();
        for (var index = 1; index <= 70; index++)
            specs.Add(ChapterAnalysisTestFactory.Candidate(ChapterCandidateSource.TrackTitle, "Chapter", index));
        for (var index = 71; index <= 100; index++)
        {
            specs.Add(ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.TrackTitle,
                "Meaningful " + index,
                index));
        }

        var result = _service.Analyze(ChapterAnalysisTestFactory.Scope(specs.ToArray()));
        var summary = Assert.Single(result.SourceSummaries);

        Assert.Equal(70, summary.GenericCandidateCount);
        Assert.Equal(30, summary.ValidMeaningfulCount);
        Assert.Equal(30, result.Candidates.Count(candidate =>
            candidate.Quality == ChapterCandidateQuality.IndividualMeaningful));
    }

    [Fact]
    public void Analyze_UniformProjectMetadataGetsNoEvidenceStrengthBonus()
    {
        var specs = new List<ChapterAnalysisTestFactory.CandidateSpec>();
        for (var index = 1; index <= 100; index++)
        {
            specs.Add(ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.CanonicalTitle,
                "Uniform Book",
                index));
            specs.Add(ChapterAnalysisTestFactory.Candidate(
                ChapterCandidateSource.TrackTitle,
                index <= 60 ? "Track Group A" : "Track Group B",
                index));
        }

        var result = _service.Analyze(ChapterAnalysisTestFactory.Scope(specs.ToArray()));
        var projectCandidates = result.Candidates.Where(candidate => candidate.Source == ChapterCandidateSource.CanonicalTitle);
        var individualTrackCandidates = result.Candidates.Where(candidate =>
            candidate.Source == ChapterCandidateSource.TrackTitle &&
            candidate.Quality == ChapterCandidateQuality.IndividualMeaningful);

        Assert.All(projectCandidates, candidate =>
        {
            Assert.Equal(ChapterCandidateQuality.UniformMeaningful, candidate.Quality);
            Assert.Null(candidate.EvidenceStrength);
            Assert.Equal(1d, candidate.GroupSupportRatio);
        });
        Assert.Equal(40, individualTrackCandidates.Count());
        Assert.All(individualTrackCandidates, candidate => Assert.Equal(0.4d, candidate.EvidenceStrength));
    }

    [Fact]
    public void Analyze_UsesAnalysisComparisonKeyAfterTechnicalProjection()
    {
        var result = _service.Analyze(ChapterAnalysisTestFactory.Scope(
            ("1 Montag", 1),
            ("2 Montag", 2),
            ("3 Montag", 3)));

        Assert.All(result.Candidates, candidate =>
        {
            Assert.Equal("Montag", candidate.AnalysisText);
            Assert.Equal("montag", candidate.AnalysisComparisonKey);
            Assert.NotEqual(candidate.ComparisonKey, candidate.AnalysisComparisonKey);
            Assert.Equal(ChapterCandidateQuality.UniformMeaningful, candidate.Quality);
        });
        Assert.Single(result.Candidates.Select(candidate => candidate.ExactGroupId).Distinct());
    }

    [Fact]
    public void Analyze_ExactGroupsUseComparisonKeyAndFuzzyRemainsDiagnostic()
    {
        var exactResult = _service.Analyze(ChapterAnalysisTestFactory.Scope(
            ("Der Stein der Weisen", null),
            ("DER STEIN DER WEISEN", null),
            ("Der Stein der Weisen.", null)));
        var fuzzyResult = _service.Analyze(BuildCountScope(
            ("Der Stein der Weisen", 94),
            ("Der Stein der Wiesen", 3),
            ("Der Stein der Weisn", 3)));

        Assert.Single(exactResult.Candidates.Select(candidate => candidate.ExactGroupId).Distinct());
        Assert.Equal(6, fuzzyResult.Candidates.Count(candidate => candidate.WasFuzzyAbsorbed));
        Assert.Single(fuzzyResult.Candidates.Select(candidate => candidate.AnalysisGroupId).Distinct());
    }

    [Fact]
    public void Analyze_IsDeterministic()
    {
        var scope = BuildCountScope(("Dominant title", 90), ("Dominant titel", 10));

        var first = _service.Analyze(scope);
        var second = _service.Analyze(scope);

        Assert.Equal(first.ScopeInputSignature, second.ScopeInputSignature);
        Assert.Equal(first.Candidates, second.Candidates);
        Assert.Equal(first.SourceSummaries, second.SourceSummaries);
    }

    [Fact]
    public void Analyze_UsableCandidatesSatisfyResolveDataInvariant()
    {
        var meaningful = _service.Analyze(ChapterAnalysisTestFactory.Scope(
            ("1 - Prolog", 1),
            ("2 - Main", 2)));
        var unusable = _service.Analyze(ChapterAnalysisTestFactory.Scope(
            ("1", 1),
            ("2", 2)));

        Assert.All(meaningful.Candidates.Where(candidate => candidate.IsUsable), candidate =>
        {
            Assert.False(string.IsNullOrWhiteSpace(candidate.OutputBaseText));
            Assert.NotNull(candidate.Quality);
            Assert.NotEqual(Guid.Empty, candidate.Provenance.SourceId);
            Assert.False(string.IsNullOrWhiteSpace(candidate.ScopeId));
            if (candidate.Quality == ChapterCandidateQuality.IndividualMeaningful)
                Assert.NotNull(candidate.EvidenceStrength);
            else
                Assert.Null(candidate.EvidenceStrength);
        });
        Assert.All(unusable.Candidates, candidate => Assert.False(candidate.IsUsable));
    }

    private static ChapterAnalysisScope BuildCountScope(params (string Value, int Count)[] groups)
    {
        var specs = new List<ChapterAnalysisTestFactory.CandidateSpec>();
        var member = 1;
        foreach (var group in groups)
        {
            for (var index = 0; index < group.Count; index++)
            {
                specs.Add(ChapterAnalysisTestFactory.Candidate(
                    ChapterCandidateSource.TrackTitle,
                    group.Value,
                    member++));
            }
        }

        return ChapterAnalysisTestFactory.Scope(specs.ToArray());
    }
}
