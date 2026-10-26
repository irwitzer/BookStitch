using System.Globalization;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterResolveServiceTests
{
    private readonly ChapterResolveService _service = new();

    [Fact]
    public void Resolve_QualityWinsBeforeSourcePriority()
    {
        var snapshot = Snapshot(
            Candidate(ChapterCandidateSource.EmbeddedChapterTitle, "Chapter", ChapterCandidateQuality.Generic),
            Candidate(ChapterCandidateSource.TrackTitle, "Track", ChapterCandidateQuality.UniformMeaningful),
            Candidate(ChapterCandidateSource.Filename, "File", ChapterCandidateQuality.IndividualMeaningful, 0.5));

        var result = ResolveSingle(snapshot);

        Assert.Equal(ChapterCandidateSource.Filename, result.Source);
        Assert.Equal("File", result.Text);
        Assert.Equal(ChapterResolveDecisionReason.HighestQuality, result.DecisionReason);
        Assert.Equal(ChapterCandidateSource.TrackTitle, result.RunnerUp?.Source);
    }

    [Fact]
    public void Resolve_IndividualEvidenceWinsBeforeSourcePriority()
    {
        var snapshot = Snapshot(
            Candidate(ChapterCandidateSource.EmbeddedChapterTitle, "Embedded", ChapterCandidateQuality.IndividualMeaningful, 0.6),
            Candidate(ChapterCandidateSource.TrackTitle, "Track", ChapterCandidateQuality.IndividualMeaningful, 0.8));

        var result = ResolveSingle(snapshot);

        Assert.Equal(ChapterCandidateSource.TrackTitle, result.Source);
        Assert.Equal(ChapterResolveDecisionReason.HigherIndividualEvidence, result.DecisionReason);
        Assert.Equal(ChapterCandidateSource.EmbeddedChapterTitle, result.RunnerUp?.Source);
    }

    [Fact]
    public void Resolve_UniformCandidatesUseSourcePriorityWithoutSupportRatioBonus()
    {
        var snapshot = Snapshot(
            Candidate(
                ChapterCandidateSource.CanonicalTitle,
                "Canonical",
                ChapterCandidateQuality.UniformMeaningful,
                groupSupportRatio: 1d),
            Candidate(
                ChapterCandidateSource.TrackTitle,
                "Track",
                ChapterCandidateQuality.UniformMeaningful,
                groupSupportRatio: 0.1d));

        var result = ResolveSingle(snapshot);

        Assert.Equal(ChapterCandidateSource.TrackTitle, result.Source);
        Assert.Equal(ChapterResolveDecisionReason.SourcePriority, result.DecisionReason);
    }

    [Fact]
    public void Resolve_UserConfirmedCanonicalWinsWithinUniformMeaningful()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(
                ChapterCandidateSource.Filename,
                "Filename",
                ChapterCandidateQuality.UniformMeaningful),
            Candidate(
                ChapterCandidateSource.CanonicalTitle,
                "User title",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.User)));

        Assert.Equal(ChapterCandidateSource.CanonicalTitle, result.Source);
        Assert.Equal(MetadataOrigin.User, result.MetadataOrigin);
        Assert.Equal(ChapterResolveDecisionReason.UserConfirmedCanonicalPreference, result.DecisionReason);
        Assert.Equal(ChapterCandidateSource.Filename, result.RunnerUp?.Source);
    }

    [Fact]
    public void Resolve_UserConfirmedCanonicalAlbumAlsoWinsUniformSourceTie()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(
                ChapterCandidateSource.TrackTitle,
                "Track",
                ChapterCandidateQuality.UniformMeaningful),
            Candidate(
                ChapterCandidateSource.CanonicalAlbum,
                "User album",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.User)));

        Assert.Equal(ChapterCandidateSource.CanonicalAlbum, result.Source);
        Assert.Equal(ChapterResolveDecisionReason.UserConfirmedCanonicalPreference, result.DecisionReason);
    }

    [Fact]
    public void Resolve_TwoUserConfirmedCanonicalCandidatesKeepExistingSourcePolicy()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(
                ChapterCandidateSource.CanonicalAlbum,
                "User album",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.User),
            Candidate(
                ChapterCandidateSource.CanonicalTitle,
                "User title",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.User)));

        Assert.Equal(ChapterCandidateSource.CanonicalTitle, result.Source);
        Assert.Equal(ChapterResolveDecisionReason.SourcePriority, result.DecisionReason);
    }

    [Fact]
    public void Resolve_UserConfirmedUniformCanonicalNeverDisplacesIndividualFilename()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(
                ChapterCandidateSource.Filename,
                "Individual filename",
                ChapterCandidateQuality.IndividualMeaningful,
                evidenceStrength: 0.4),
            Candidate(
                ChapterCandidateSource.CanonicalTitle,
                "User title",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.User)));

        Assert.Equal(ChapterCandidateSource.Filename, result.Source);
        Assert.Equal(ChapterResolveDecisionReason.HighestQuality, result.DecisionReason);
    }

    [Fact]
    public void Resolve_SourceAggregatedCanonicalWinsWithinUniformMeaningful()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(
                ChapterCandidateSource.Filename,
                "Filename",
                ChapterCandidateQuality.UniformMeaningful),
            Candidate(
                ChapterCandidateSource.CanonicalTitle,
                "Aggregated title",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.SourceAggregation)));

        Assert.Equal(ChapterCandidateSource.CanonicalTitle, result.Source);
        Assert.Equal(MetadataOrigin.SourceAggregation, result.MetadataOrigin);
        Assert.Equal(ChapterResolveDecisionReason.SourceAggregatedCanonicalPreference, result.DecisionReason);
    }

    [Fact]
    public void Resolve_SourceAggregatedCanonicalAlbumWinsWithinUniformMeaningful()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(ChapterCandidateSource.TrackTitle, "Track", ChapterCandidateQuality.UniformMeaningful),
            Candidate(
                ChapterCandidateSource.CanonicalAlbum,
                "Aggregated album",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.SourceAggregation)));

        Assert.Equal(ChapterCandidateSource.CanonicalAlbum, result.Source);
        Assert.Equal(ChapterResolveDecisionReason.SourceAggregatedCanonicalPreference, result.DecisionReason);
    }

    [Fact]
    public void Resolve_UserCanonicalWinsBeforeSourceAggregatedCanonical()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(
                ChapterCandidateSource.CanonicalAlbum,
                "Aggregated album",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.SourceAggregation),
            Candidate(
                ChapterCandidateSource.CanonicalTitle,
                "User title",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.User)));

        Assert.Equal(ChapterCandidateSource.CanonicalTitle, result.Source);
        Assert.Equal(ChapterResolveDecisionReason.UserConfirmedCanonicalPreference, result.DecisionReason);
    }

    [Fact]
    public void Resolve_TwoSourceAggregatedCanonicalCandidatesKeepExistingSourcePolicy()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(
                ChapterCandidateSource.CanonicalAlbum,
                "Aggregated album",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.SourceAggregation),
            Candidate(
                ChapterCandidateSource.CanonicalTitle,
                "Aggregated title",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.SourceAggregation)));

        Assert.Equal(ChapterCandidateSource.CanonicalTitle, result.Source);
        Assert.Equal(ChapterResolveDecisionReason.SourcePriority, result.DecisionReason);
    }

    [Theory]
    [InlineData(MetadataOrigin.FolderStructure)]
    [InlineData(MetadataOrigin.SingleFileName)]
    [InlineData(MetadataOrigin.Derived)]
    [InlineData(MetadataOrigin.LegacyMigrated)]
    [InlineData(MetadataOrigin.GlobalPreference)]
    public void Resolve_NonAggregatedCanonicalOriginsKeepExistingUniformSourcePriority(MetadataOrigin origin)
    {
        var result = ResolveSingle(Snapshot(
            Candidate(ChapterCandidateSource.Filename, "Filename", ChapterCandidateQuality.UniformMeaningful),
            Candidate(
                ChapterCandidateSource.CanonicalTitle,
                "Canonical",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: origin)));

        Assert.Equal(ChapterCandidateSource.Filename, result.Source);
        Assert.Equal(ChapterResolveDecisionReason.SourcePriority, result.DecisionReason);
    }

    [Fact]
    public void Resolve_SourceAggregatedUniformCanonicalNeverDisplacesIndividualCandidate()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(
                ChapterCandidateSource.TrackTitle,
                "Individual track",
                ChapterCandidateQuality.IndividualMeaningful,
                evidenceStrength: 0.4),
            Candidate(
                ChapterCandidateSource.CanonicalTitle,
                "Aggregated title",
                ChapterCandidateQuality.UniformMeaningful,
                metadataOrigin: MetadataOrigin.SourceAggregation)));

        Assert.Equal(ChapterCandidateSource.TrackTitle, result.Source);
        Assert.Equal(ChapterResolveDecisionReason.HighestQuality, result.DecisionReason);
    }

    [Fact]
    public void Resolve_MeaningfulCandidateWinsAgainstGenericSource()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(ChapterCandidateSource.EmbeddedChapterTitle, "Chapter", ChapterCandidateQuality.Generic),
            Candidate(ChapterCandidateSource.CanonicalTitle, "Book", ChapterCandidateQuality.UniformMeaningful)));

        Assert.Equal(ChapterCandidateSource.CanonicalTitle, result.Source);
        Assert.Equal("Book", result.Text);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void Resolve_RealGenericCandidateWinsBeforeArtificialFallback()
    {
        var result = ResolveSingle(Snapshot(
            Candidate(ChapterCandidateSource.EmbeddedChapterTitle, "Chapter", ChapterCandidateQuality.Generic)));

        Assert.Equal("Chapter", result.Text);
        Assert.Equal(ChapterCandidateSource.EmbeddedChapterTitle, result.Source);
        Assert.Equal(ChapterCandidateQuality.Generic, result.Quality);
        Assert.False(result.IsFallback);
    }

    public static IEnumerable<object[]> ExplicitUiCultures() =>
        UiLanguageService.Choices
            .Where(choice => choice.Id != UiLanguageIds.System)
            .Select(choice => new object[] { choice.Id });

    [Theory]
    [MemberData(nameof(ExplicitUiCultures))]
    public void Resolve_NoUsableCandidateUsesUiCultureFallback(string cultureName)
    {
        var unusable = Candidate(
            ChapterCandidateSource.AuthorArtistContext,
            string.Empty,
            quality: null,
            isUsable: false);
        var culture = CultureInfo.GetCultureInfo(cultureName);

        var result = ResolveSingle(Snapshot(unusable), culture);

        Assert.Equal(ChapterFallbackLabelProvider.GetLabel(culture), result.Text);
        Assert.Null(result.Source);
        Assert.Equal(ChapterCandidateQuality.Generic, result.Quality);
        Assert.True(result.IsFallback);
        Assert.Equal(ChapterResolveDecisionReason.Fallback, result.DecisionReason);
    }

    [Fact]
    public void Resolve_UsesOutputBaseTextInsteadOfNormalizedText()
    {
        var candidate = Candidate(
            ChapterCandidateSource.TrackTitle,
            "Prolog",
            ChapterCandidateQuality.UniformMeaningful) with
        {
            NormalizedText = "1 - Prolog",
            AnalysisText = "Prolog",
            OutputBaseText = "Prolog"
        };

        Assert.Equal("Prolog", ResolveSingle(Snapshot(candidate)).Text);
    }

    [Fact]
    public void Resolve_UnusableCandidateCannotInfluenceUsableWinner()
    {
        var invalidHighPriority = Candidate(
            ChapterCandidateSource.EmbeddedChapterTitle,
            string.Empty,
            quality: null,
            isUsable: false);
        var usable = Candidate(
            ChapterCandidateSource.CanonicalAlbum,
            "Usable Album",
            ChapterCandidateQuality.Generic);

        var result = ResolveSingle(Snapshot(invalidHighPriority, usable));

        Assert.Equal(ChapterCandidateSource.CanonicalAlbum, result.Source);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void Resolve_UsableCandidateWithEmptyOutputFailsInvariant()
    {
        var candidate = Candidate(
            ChapterCandidateSource.TrackTitle,
            string.Empty,
            ChapterCandidateQuality.UniformMeaningful);

        Assert.Throws<InvalidOperationException>(() => _service.Resolve(Snapshot(candidate)));
    }

    [Fact]
    public void Resolve_IndividualCandidateWithoutEvidenceFailsInvariant()
    {
        var candidate = Candidate(
            ChapterCandidateSource.TrackTitle,
            "Individual",
            ChapterCandidateQuality.IndividualMeaningful,
            evidenceStrength: null);

        Assert.Throws<InvalidOperationException>(() => _service.Resolve(Snapshot(candidate)));
    }

    [Fact]
    public void Resolve_UsableAuthorArtistSourceFailsInvariant()
    {
        var candidate = Candidate(
            ChapterCandidateSource.AuthorArtistContext,
            "Author",
            ChapterCandidateQuality.IndividualMeaningful,
            0.8);

        Assert.Throws<InvalidOperationException>(() => _service.Resolve(Snapshot(candidate)));
    }

    [Fact]
    public void Resolve_DuplicateSourceForOneChapterFailsInvariant()
    {
        var first = Candidate(
            ChapterCandidateSource.TrackTitle,
            "First",
            ChapterCandidateQuality.UniformMeaningful);
        var second = first with { OutputBaseText = "Second", AnalysisText = "Second" };

        Assert.Throws<InvalidOperationException>(() => _service.Resolve(Snapshot(first, second)));
    }

    [Fact]
    public void Resolve_CandidateMixOutsideScopeResultsFailsInvariant()
    {
        var original = Candidate(
            ChapterCandidateSource.TrackTitle,
            "Current",
            ChapterCandidateQuality.UniformMeaningful);
        var snapshot = Snapshot(original);
        var stale = original with { OutputBaseText = "Stale", AnalysisText = "Stale" };
        var mixed = snapshot with
        {
            CandidatesByEffectiveChapter = new Dictionary<string, IReadOnlyList<AnalyzedChapterCandidate>>
            {
                [original.Provenance.EffectiveChapterId] = [stale]
            }
        };

        Assert.Throws<InvalidOperationException>(() => _service.Resolve(mixed));
    }

    [Fact]
    public void Resolve_UnexpectedPolicyTieIsDeterministicAndDiagnosed()
    {
        var priorities = new Dictionary<ChapterCandidateSource, int>
        {
            [ChapterCandidateSource.EmbeddedChapterTitle] = 1,
            [ChapterCandidateSource.TrackTitle] = 2,
            [ChapterCandidateSource.Filename] = 2,
            [ChapterCandidateSource.CanonicalTitle] = 4,
            [ChapterCandidateSource.CanonicalAlbum] = 5
        };
        var service = new ChapterResolveService(new ChapterSourcePolicy(priorities), _ => "Chapter");
        var track = Candidate(
            ChapterCandidateSource.TrackTitle,
            "Track",
            ChapterCandidateQuality.UniformMeaningful);
        var filename = Candidate(
            ChapterCandidateSource.Filename,
            "Filename",
            ChapterCandidateQuality.UniformMeaningful);

        var first = Assert.Single(service.Resolve(Snapshot(track, filename)).Chapters);
        var second = Assert.Single(service.Resolve(Snapshot(filename, track)).Chapters);

        Assert.Equal(first.Source, second.Source);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(ChapterResolveDecisionReason.DeterministicTieBreak, first.DecisionReason);
        Assert.Equal(ChapterResolveDecisionReason.DeterministicTieBreak, second.DecisionReason);
        Assert.NotNull(first.RunnerUp);
    }

    private ResolvedChapterContent ResolveSingle(
        ChapterAnalysisSnapshot snapshot,
        CultureInfo? culture = null) =>
        Assert.Single(_service.Resolve(snapshot, culture).Chapters);

    private static ChapterAnalysisSnapshot Snapshot(params AnalyzedChapterCandidate[] candidates)
    {
        var provenance = candidates.FirstOrDefault()?.Provenance ?? ChapterAnalysisTestFactory.Provenance(1);
        var scopeId = "resolve-scope";
        candidates = candidates.Select(candidate => candidate with
        {
            Provenance = provenance,
            ScopeId = scopeId
        }).ToArray();
        var scopeResult = new ChapterScopeAnalysisResult(
            scopeId,
            ChapterScopeKind.FolderTrack,
            new ChapterScopeInputSignature(ChapterAnalysisRules.CurrentRevision, "test-signature"),
            candidates,
            []);

        return new ChapterAnalysisSnapshot(
            Guid.NewGuid(),
            ChapterAnalysisRules.CurrentRevision,
            ProjectMetadataRevision: 1,
            [provenance],
            [scopeResult],
            new Dictionary<string, IReadOnlyList<AnalyzedChapterCandidate>>(StringComparer.Ordinal)
            {
                [provenance.EffectiveChapterId] = candidates
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [provenance.EffectiveChapterId] = scopeId
            });
    }

    private static AnalyzedChapterCandidate Candidate(
        ChapterCandidateSource source,
        string outputBaseText,
        ChapterCandidateQuality? quality,
        double? evidenceStrength = null,
        bool isUsable = true,
        double? groupSupportRatio = null,
        MetadataOrigin metadataOrigin = MetadataOrigin.None)
    {
        var provenance = ChapterAnalysisTestFactory.Provenance(1);
        return new AnalyzedChapterCandidate(
            source,
            provenance,
            outputBaseText,
            outputBaseText,
            outputBaseText.ToLowerInvariant(),
            outputBaseText,
            outputBaseText.ToLowerInvariant(),
            outputBaseText,
            quality,
            isUsable,
            quality == ChapterCandidateQuality.Generic,
            "exact",
            "analysis",
            groupSupportRatio,
            evidenceStrength,
            evidenceStrength,
            WasFuzzyAbsorbed: false,
            FuzzyEditDistance: null,
            ChapterTechnicalNumberingEvidence.None,
            "resolve-scope",
            metadataOrigin);
    }
}
