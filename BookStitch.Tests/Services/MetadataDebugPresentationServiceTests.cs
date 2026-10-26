using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MetadataDebugPresentationServiceTests
{
    [Fact]
    public void Create_UserEditReplacesDisplayedDecisionWithoutReaggregatingSources()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Aggregiert", MetadataOrigin.SourceAggregation);
        var aggregatedWinner = new MetadataDecisionCandidateDiagnostic(
            "Aggregiert",
            MetadataDecisionEvidenceType.AlbumFallback,
            10,
            10,
            1d);
        var decisions = new[]
        {
            new MetadataFieldDecisionDiagnostic(
                ProjectMetadataField.Title,
                "Aggregiert",
                MetadataOrigin.SourceAggregation,
                "Album-Fallback",
                aggregatedWinner,
                null)
        };

        metadata.SetValue(ProjectMetadataField.Title, "Vom Benutzer", MetadataOrigin.User);
        var snapshot = new MetadataDebugPresentationService().Create(metadata, decisions);

        var title = Assert.Single(snapshot.Fields, item => item.Field == ProjectMetadataField.Title);
        Assert.Equal("Vom Benutzer", title.CanonicalValue);
        Assert.Equal(MetadataOrigin.User, title.Origin);
        Assert.Equal(MetadataDecisionEvidenceType.UserOverride, title.Winner?.EvidenceType);
        Assert.Equal("Aggregiert", title.RunnerUp?.Value);
        Assert.Contains("Benutzerwert", title.RunnerUp?.RejectedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ExposesOnlyTheFourRequestedCanonicalFields()
    {
        var snapshot = new MetadataDebugPresentationService().Create(new ProjectMetadata(), []);

        Assert.Equal(
            [
                ProjectMetadataField.Title,
                ProjectMetadataField.Album,
                ProjectMetadataField.Authors,
                ProjectMetadataField.Narrators
            ],
            snapshot.Fields.Select(field => field.Field));
    }
}
