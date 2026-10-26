using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ScalarCandidatePresentationServiceTests
{
    private readonly ScalarCandidatePresentationService _service = new();

    [Fact]
    public void UserOverride_IsFirst_AndOriginalAutomaticWinnerKeepsStar()
    {
        var canonical = new MetadataValue<string>("UserTitleNeu03", MetadataOrigin.User);
        var decisions = new[]
        {
            new MetadataFieldDecisionDiagnostic(
                ProjectMetadataField.Title,
                "UserTitleNeu03",
                MetadataOrigin.User,
                "User override",
                new MetadataDecisionCandidateDiagnostic(
                    "UserTitleNeu03",
                    MetadataDecisionEvidenceType.UserOverride,
                    null,
                    null,
                    null),
                new MetadataDecisionCandidateDiagnostic(
                    "E Wie Emil",
                    MetadataDecisionEvidenceType.AlbumFallback,
                    12,
                    12,
                    1.0))
        };
        var candidates = new[]
        {
            Candidate("Stereo-Test", 1, automatic: true),
            Candidate("E Wie Emil", 1),
            Candidate("Die Sache Mit Der Garderobe I", 1),
            Candidate("UserTitleNeu01", 1, MetadataOrigin.User),
            Candidate("UserTitleNeu02", 1, MetadataOrigin.User),
            Candidate("UserTitleNeu03", 1, MetadataOrigin.User, canonical: true)
        };

        var automatic = _service.ResolveAutomaticWinnerValue(
            ProjectMetadataField.Title, canonical, decisions, candidates);
        var result = _service.ApplyAutomaticWinnerAndSort(
            ProjectMetadataField.Title, canonical, candidates, automatic);

        Assert.Equal("E Wie Emil", automatic);
        Assert.Equal("UserTitleNeu03", result[0].DisplayValue);
        Assert.Equal("E Wie Emil", result[1].DisplayValue);
        Assert.True(result[1].IsAutomaticWinner);
        Assert.False(result.Single(item => item.DisplayValue == "Stereo-Test").IsAutomaticWinner);
        Assert.Equal(1, result.Count(item => item.IsAutomaticWinner));
    }

    [Fact]
    public void InitialAutomaticCanonical_IsFirstEvenWhenRawCandidateServiceMarkedAnotherValue()
    {
        var canonical = new MetadataValue<string>("E Wie Emil", MetadataOrigin.SourceAggregation);
        var decisions = new[]
        {
            new MetadataFieldDecisionDiagnostic(
                ProjectMetadataField.Title,
                "E Wie Emil",
                MetadataOrigin.SourceAggregation,
                "Album fallback",
                new MetadataDecisionCandidateDiagnostic(
                    "E Wie Emil",
                    MetadataDecisionEvidenceType.AlbumFallback,
                    12,
                    12,
                    1.0),
                null)
        };
        var candidates = new[]
        {
            Candidate("Stereo-Test", 1, automatic: true),
            Candidate("E Wie Emil", 1),
            Candidate("Der Telegrafenbeamte", 1)
        };

        var automatic = _service.ResolveAutomaticWinnerValue(
            ProjectMetadataField.Title, canonical, decisions, candidates);
        var result = _service.ApplyAutomaticWinnerAndSort(
            ProjectMetadataField.Title, canonical, candidates, automatic);

        Assert.Equal("E Wie Emil", result[0].DisplayValue);
        Assert.True(result[0].IsAutomaticWinner);
        Assert.False(result[1].IsAutomaticWinner);
    }

    [Fact]
    public void DeletedCanonical_UsesRememberedAutomaticWinnerAndKeepsFrequencyOrderAfterIt()
    {
        var canonical = new MetadataValue<string>(null, MetadataOrigin.None);
        var candidates = new[]
        {
            Candidate("Low", 1),
            Candidate("E Wie Emil", 1),
            Candidate("High", 5),
            Candidate("Mid", 3)
        };

        var automatic = _service.ResolveAutomaticWinnerValue(
            ProjectMetadataField.Title,
            canonical,
            [],
            candidates,
            rememberedValue: "E Wie Emil");
        var result = _service.ApplyAutomaticWinnerAndSort(
            ProjectMetadataField.Title, canonical, candidates, automatic);

        Assert.Equal(new[] { "E Wie Emil", "High", "Mid", "Low" }, result.Select(item => item.DisplayValue));
        Assert.True(result[0].IsAutomaticWinner);
    }

    [Fact]
    public void SelectingAutomaticWinnerAsUser_KeepsSourceCandidateStarAndMovesItToTop()
    {
        var canonical = new MetadataValue<string>("E Wie Emil", MetadataOrigin.User);
        var candidates = new[]
        {
            Candidate("Stereo-Test", 4),
            Candidate("E Wie Emil", 1),
            Candidate("Other", 3)
        };

        var result = _service.ApplyAutomaticWinnerAndSort(
            ProjectMetadataField.Title, canonical, candidates, "E Wie Emil");

        Assert.Equal("E Wie Emil", result[0].DisplayValue);
        Assert.True(result[0].IsAutomaticWinner);
        Assert.Equal(MetadataOrigin.SourceAggregation, result[0].EvidenceOrigin);
        Assert.Equal("Stereo-Test", result[1].DisplayValue);
        Assert.Equal("Other", result[2].DisplayValue);
    }

    private static ScalarSourceMetadataAlternative Candidate(
        string value,
        int count,
        MetadataOrigin origin = MetadataOrigin.SourceAggregation,
        bool automatic = false,
        bool canonical = false) =>
        new(ProjectMetadataField.Title, value, count, value)
        {
            EvidenceOrigin = origin,
            IsAutomaticWinner = automatic,
            IsCanonicalWinner = canonical
        };
}
