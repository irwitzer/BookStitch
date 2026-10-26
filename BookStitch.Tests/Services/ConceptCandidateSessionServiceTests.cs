using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConceptCandidateSessionServiceTests
{
    [Fact]
    public void PresentScalar_SourceWinner_IsMarkedAutomatic()
    {
        var service = new ConceptCandidateSessionService();
        var source = new[] { Scalar(ProjectMetadataField.Title, "E wie Emil", 5, automatic: true) };

        var result = service.PresentScalar(
            ProjectMetadataField.Title,
            source,
            new MetadataValue<string>("E wie Emil", MetadataOrigin.SourceAggregation),
            []);

        var candidate = Assert.Single(result);
        Assert.True(candidate.IsCanonicalWinner);
        Assert.True(candidate.IsAutomaticWinner);
        Assert.Equal(MetadataOrigin.SourceAggregation, candidate.EvidenceOrigin);
        Assert.Equal(5, candidate.SourceCount);
    }

    [Fact]
    public void PresentScalar_UserOverride_IsFirst_AndOriginalAutomaticWinnerRemains()
    {
        var service = new ConceptCandidateSessionService();
        var source = new[] { Scalar(ProjectMetadataField.Title, "E wie Emil", 5, automatic: true) };

        service.PresentScalar(
            ProjectMetadataField.Title,
            source,
            new MetadataValue<string>("E wie Emil", MetadataOrigin.SourceAggregation),
            []);

        var result = service.PresentScalar(
            ProjectMetadataField.Title,
            source,
            new MetadataValue<string>("Mein Titel", MetadataOrigin.User),
            []);

        Assert.Equal("Mein Titel", result[0].DisplayValue);
        Assert.True(result[0].IsCanonicalWinner);
        Assert.Equal(MetadataOrigin.User, result[0].EvidenceOrigin);
        Assert.Equal("E wie Emil", result[1].DisplayValue);
        Assert.True(result[1].IsAutomaticWinner);
        Assert.Equal(MetadataOrigin.SourceAggregation, result[1].EvidenceOrigin);
    }

    [Fact]
    public void PresentScalar_PreviousCanonicalValue_RemainsRetainedAfterNewCanonical()
    {
        var service = new ConceptCandidateSessionService();

        service.PresentScalar(
            ProjectMetadataField.Album,
            [],
            new MetadataValue<string>("Früher", MetadataOrigin.User),
            []);

        var result = service.PresentScalar(
            ProjectMetadataField.Album,
            [],
            new MetadataValue<string>("Heute", MetadataOrigin.User),
            []);

        Assert.Equal("Heute", result[0].DisplayValue);
        Assert.Contains(result, candidate => candidate.DisplayValue == "Früher");
    }

    [Fact]
    public void PresentScalar_SourceAndUserSameIdentity_MergeAndKeepSourceEvidence()
    {
        var service = new ConceptCandidateSessionService();
        var source = new[] { Scalar(ProjectMetadataField.Title, "Alpha", 7, automatic: true) };

        var result = service.PresentScalar(
            ProjectMetadataField.Title,
            source,
            new MetadataValue<string>(" alpha ", MetadataOrigin.User),
            []);

        var candidate = Assert.Single(result);
        Assert.True(candidate.IsCanonicalWinner);
        Assert.Equal(MetadataOrigin.SourceAggregation, candidate.EvidenceOrigin);
        Assert.Equal(7, candidate.SourceCount);
    }

    [Fact]
    public void CaseOnlyRetainedUserValue_DeduplicatesWithSourceAndKeepsEvidenceAndAutomaticWinner()
    {
        var service = new ConceptCandidateSessionService();
        service.RetainUserValue(ProjectMetadataField.Title, "Hallo");

        var result = service.PresentScalar(
            ProjectMetadataField.Title,
            [Scalar(ProjectMetadataField.Title, "hallo", 20, automatic: true)],
            new MetadataValue<string>("Hallo", MetadataOrigin.User),
            []);

        var candidate = Assert.Single(result);
        Assert.Equal(MetadataOrigin.SourceAggregation, candidate.EvidenceOrigin);
        Assert.Equal(20, candidate.SourceCount);
        Assert.True(candidate.IsCanonicalWinner);
        Assert.True(candidate.IsAutomaticWinner);
    }

    [Fact]
    public void PresentList_CanonicalSourceAndRetained_AreMergedByFieldIdentity()
    {
        var service = new ConceptCandidateSessionService();
        var initialSources = new[]
        {
            List(ProjectMetadataField.Authors, "Quelle A", 4),
            List(ProjectMetadataField.Authors, "Quelle B", 2)
        };

        service.PresentList(
            ProjectMetadataField.Authors,
            initialSources,
            new MetadataValue<IReadOnlyList<string>>(["Manuell", "Quelle A"], MetadataOrigin.User));

        var result = service.PresentList(
            ProjectMetadataField.Authors,
            [List(ProjectMetadataField.Authors, "Neu", 3)],
            new MetadataValue<IReadOnlyList<string>>(["Neu"], MetadataOrigin.User));

        Assert.Equal(result.Count, result.Select(candidate => candidate.ValueToCommit).Distinct().Count());
        Assert.Contains(result, candidate => Equals(candidate.ValueToCommit, "Manuell"));
        Assert.Contains(result, candidate => Equals(candidate.ValueToCommit, "Quelle A") &&
                                             candidate.EvidenceOrigin == MetadataOrigin.SourceAggregation);
        Assert.Contains(result, candidate => Equals(candidate.ValueToCommit, "Neu") && candidate.IsCanonicalWinner);
    }

    [Fact]
    public void PresentList_DeletedConfirmedValue_RemainsCandidate()
    {
        var service = new ConceptCandidateSessionService();

        service.PresentList(
            ProjectMetadataField.Narrators,
            [],
            new MetadataValue<IReadOnlyList<string>>(["Sprecher A"], MetadataOrigin.User));

        var result = service.PresentList(
            ProjectMetadataField.Narrators,
            [],
            new MetadataValue<IReadOnlyList<string>>([], MetadataOrigin.None));

        var candidate = Assert.Single(result);
        Assert.Equal("Sprecher A", candidate.ValueToCommit);
        Assert.False(candidate.IsCanonicalWinner);
    }

    [Fact]
    public void CandidateEvidence_SourceCountOnlyRepresentsRealSourceEvidence()
    {
        var source = new ConceptMetadataCandidate(
            ProjectMetadataField.Title,
            "Quelle",
            "Quelle",
            4,
            MetadataOrigin.SourceAggregation);
        var user = new ConceptMetadataCandidate(
            ProjectMetadataField.Title,
            "Benutzer",
            "Benutzer",
            0,
            MetadataOrigin.User);
        var list = new ConceptMetadataCandidate(
            ProjectMetadataField.Title,
            "Liste",
            "Liste",
            0,
            MetadataOrigin.StaticList);

        Assert.True(source.HasSourceEvidence);
        Assert.True(source.ShowSourceCount);
        Assert.False(user.HasSourceEvidence);
        Assert.False(user.ShowSourceCount);
        Assert.False(list.HasSourceEvidence);
        Assert.False(list.ShowSourceCount);
    }

    [Fact]
    public void RetainedAndSyntheticUserCandidatesDoNotInventSourceCounts()
    {
        var service = new ConceptCandidateSessionService();
        service.RetainUserValue(ProjectMetadataField.Title, "Manuell");

        var scalar = service.PresentScalar(
            ProjectMetadataField.Title,
            [],
            new MetadataValue<string>("Manuell", MetadataOrigin.User),
            []);
        var list = service.PresentList(
            ProjectMetadataField.Authors,
            [],
            new MetadataValue<IReadOnlyList<string>>(["Autor"], MetadataOrigin.User),
            []);

        Assert.All(scalar, candidate => Assert.Equal(0, candidate.SourceCount));
        Assert.All(scalar, candidate => Assert.False(candidate.HasSourceEvidence));
        Assert.Equal(0, Assert.Single(list).SourceCount);
        Assert.False(Assert.Single(list).HasSourceEvidence);
    }

    [Fact]
    public void Reset_RemovesRetainedAndRememberedSessionState()
    {
        var service = new ConceptCandidateSessionService();
        var source = new[] { Scalar(ProjectMetadataField.Title, "Automatic", 3, automatic: true) };

        service.PresentScalar(
            ProjectMetadataField.Title,
            source,
            new MetadataValue<string>("Automatic", MetadataOrigin.SourceAggregation),
            []);
        service.PresentScalar(
            ProjectMetadataField.Title,
            source,
            new MetadataValue<string>("User", MetadataOrigin.User),
            []);

        service.Reset();

        var result = service.PresentScalar(
            ProjectMetadataField.Title,
            [],
            new MetadataValue<string>(null, MetadataOrigin.None),
            []);

        Assert.Empty(result);
    }

    [Fact]
    public void RetainUserValue_PreservesRapidSuccessiveGenericCommitsBeforeRefresh()
    {
        var service = new ConceptCandidateSessionService();

        service.RetainUserValue(ProjectMetadataField.Barcode, "4000000000041");
        service.RetainUserValue(ProjectMetadataField.Barcode, "4012345678901");

        var result = service.PresentScalar(
            ProjectMetadataField.Barcode,
            [],
            new MetadataValue<string>("4012345678901", MetadataOrigin.User),
            []);

        Assert.Contains(result, candidate => Equals(candidate.ValueToCommit, "4000000000041"));
        Assert.Contains(result, candidate => Equals(candidate.ValueToCommit, "4012345678901") &&
                                             candidate.IsCanonicalWinner);
    }

    [Theory]
    [InlineData(ProjectMetadataField.ISBN)]
    [InlineData(ProjectMetadataField.Barcode)]
    [InlineData(ProjectMetadataField.ASIN)]
    [InlineData(ProjectMetadataField.SortTitle)]
    public void GenericScalar_UserCanonicalWithoutSource_IsPresentedAndRetained(ProjectMetadataField field)
    {
        var service = new ConceptCandidateSessionService();

        var first = service.PresentScalar(
            field,
            [],
            new MetadataValue<string>("User Value", MetadataOrigin.User),
            []);
        var second = service.PresentScalar(
            field,
            [],
            new MetadataValue<string>(null, MetadataOrigin.None),
            []);

        Assert.True(Assert.Single(first).IsCanonicalWinner);
        Assert.Contains(second, candidate => Equals(candidate.ValueToCommit, "User Value"));
    }

    [Theory]
    [InlineData(ProjectMetadataField.Keywords)]
    [InlineData(ProjectMetadataField.Categories)]
    [InlineData(ProjectMetadataField.SortNarrators)]
    public void GenericMulti_UserCanonical_RemainsAfterRemoval(ProjectMetadataField field)
    {
        var service = new ConceptCandidateSessionService();

        service.PresentList(
            field,
            [],
            new MetadataValue<IReadOnlyList<string>>(["User Value"], MetadataOrigin.User));

        var result = service.PresentList(
            field,
            [],
            new MetadataValue<IReadOnlyList<string>>([], MetadataOrigin.None));

        var candidate = Assert.Single(result);
        Assert.Equal("User Value", candidate.ValueToCommit);
        Assert.False(candidate.IsCanonicalWinner);
    }

    [Fact]
    public void GenericScalar_RetainedUserCandidateSurvivesNewSources_AndSourceEvidenceWinsOnDedup()
    {
        var service = new ConceptCandidateSessionService();

        service.PresentScalar(
            ProjectMetadataField.Barcode,
            [],
            new MetadataValue<string>("4000000000041", MetadataOrigin.User),
            []);

        var result = service.PresentScalar(
            ProjectMetadataField.Barcode,
            [
                Scalar(ProjectMetadataField.Barcode, "4000000000041", 3),
                Scalar(ProjectMetadataField.Barcode, "4012345678901", 2)
            ],
            new MetadataValue<string>("4000000000041", MetadataOrigin.User),
            []);

        Assert.Equal(2, result.Count);
        var merged = Assert.Single(result, candidate => Equals(candidate.ValueToCommit, "4000000000041"));
        Assert.Equal(MetadataOrigin.SourceAggregation, merged.EvidenceOrigin);
        Assert.Equal(3, merged.SourceCount);
        Assert.True(merged.IsCanonicalWinner);
    }

    [Fact]
    public void Language_UnderscoreSourceAndCanonicalStaticTag_DedupeByCanonicalBcp47Identity()
    {
        var service = new ConceptCandidateSessionService();
        var staticCandidate = new ConceptMetadataCandidate(
            ProjectMetadataField.Languages,
            "Deutsch (Schweiz) — de-CH",
            "de-CH",
            0,
            MetadataOrigin.StaticList);

        var result = service.PresentList(
            ProjectMetadataField.Languages,
            [List(ProjectMetadataField.Languages, "de_CH", 2)],
            new MetadataValue<IReadOnlyList<string>>([], MetadataOrigin.None),
            [staticCandidate]);

        var candidate = Assert.Single(result);
        Assert.Equal("de-CH", candidate.ValueToCommit);
        Assert.Equal(MetadataOrigin.SourceAggregation, candidate.EvidenceOrigin);
        Assert.Equal("Deutsch (Schweiz) — de-CH", candidate.DisplayValue);
    }

    [Fact]
    public void Language_StaticAndSourceSameCanonicalValue_MergeDisplayAndSourceEvidence()
    {
        var service = new ConceptCandidateSessionService();
        var staticCandidate = new ConceptMetadataCandidate(
            ProjectMetadataField.Languages,
            "Deutsch (Schweiz) — de-CH",
            "de-CH",
            0,
            MetadataOrigin.StaticList);

        var result = service.PresentList(
            ProjectMetadataField.Languages,
            [List(ProjectMetadataField.Languages, "de-CH", 4)],
            new MetadataValue<IReadOnlyList<string>>([], MetadataOrigin.None),
            [staticCandidate]);

        var candidate = Assert.Single(result);
        Assert.Equal("Deutsch (Schweiz) — de-CH", candidate.DisplayValue);
        Assert.Equal("de-CH", candidate.ValueToCommit);
        Assert.Equal(MetadataOrigin.SourceAggregation, candidate.EvidenceOrigin);
        Assert.Equal(4, candidate.SourceCount);
    }

    [Fact]
    public void TypedScalar_EnumSourceStaticAndCanonical_AreMerged()
    {
        var service = new ConceptCandidateSessionService();
        var source = new ScalarSourceMetadataAlternative(
            ProjectMetadataField.AbridgedStatus,
            AbridgedStatus.Unabridged,
            3,
            "Ungekürzt")
        {
            EvidenceOrigin = MetadataOrigin.SourceAggregation
        };
        var staticCandidate = new ConceptMetadataCandidate(
            ProjectMetadataField.AbridgedStatus,
            "Ungekürzt",
            AbridgedStatus.Unabridged,
            0,
            MetadataOrigin.StaticList);

        var result = service.PresentValueScalar(
            ProjectMetadataField.AbridgedStatus,
            [source],
            new MetadataValue<AbridgedStatus?>(AbridgedStatus.Unabridged, MetadataOrigin.User),
            [staticCandidate]);

        var candidate = Assert.Single(result);
        Assert.Equal(AbridgedStatus.Unabridged, candidate.ValueToCommit);
        Assert.True(candidate.IsCanonicalWinner);
        Assert.Equal(MetadataOrigin.SourceAggregation, candidate.EvidenceOrigin);
        Assert.Equal(3, candidate.SourceCount);
    }

    [Fact]
    public void TypedScalar_MetadataDateAndInteger_AreSupportedWithoutStringIdentity()
    {
        var service = new ConceptCandidateSessionService();
        var date = new MetadataDate(2024, 3, 2);

        var dateResult = service.PresentValueScalar(
            ProjectMetadataField.ReleaseDate,
            [],
            new MetadataValue<MetadataDate?>(date, MetadataOrigin.User));
        var intResult = service.PresentValueScalar(
            ProjectMetadataField.Year,
            [],
            new MetadataValue<int?>(2024, MetadataOrigin.User));

        Assert.Equal(date, Assert.Single(dateResult).ValueToCommit);
        Assert.Equal(2024, Assert.Single(intResult).ValueToCommit);
    }

    private static ScalarSourceMetadataAlternative Scalar(
        ProjectMetadataField field,
        string value,
        int sourceCount,
        bool automatic = false) =>
        new(field, value, sourceCount, value)
        {
            EvidenceOrigin = MetadataOrigin.SourceAggregation,
            IsAutomaticWinner = automatic
        };

    private static ListSourceMetadataAlternative List(
        ProjectMetadataField field,
        string value,
        int sourceCount) =>
        new(field, [value], sourceCount, value)
        {
            EvidenceOrigin = MetadataOrigin.SourceAggregation,
            IsAutomaticWinner = true
        };
}
