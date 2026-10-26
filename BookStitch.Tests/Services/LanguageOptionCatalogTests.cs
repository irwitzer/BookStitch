using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LanguageOptionCatalogTests
{
    [Fact]
    public void All_ContainsCuratedCommonTags_WithoutPlatformGeneratedCombinations()
    {
        var tags = LanguageOptionCatalog.All.Select(option => option.Value).ToArray();

        foreach (var expected in new[]
                 {
                     "de", "de-DE", "de-AT", "de-CH",
                     "en", "en-US", "en-GB",
                     "es", "fr", "pt-BR"
                 })
        {
            Assert.Contains(expected, tags, StringComparer.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("en-DE", tags, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            tags.Length,
            tags.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void All_EveryCuratedOptionRemainsCanonicalizable()
    {
        foreach (var option in LanguageOptionCatalog.All)
        {
            Assert.True(
                MetadataPreferencesService.TryCanonicalizeLanguage(option.Value, out var canonical));
            Assert.Equal(option.Value, canonical, ignoreCase: true);
        }
    }

    [Fact]
    public void LanguageField_CanCombineSourceAndStaticCandidates()
    {
        var session = new ConceptCandidateSessionService();
        var result = session.PresentList(
            ProjectMetadataField.Languages,
            [
                new ListSourceMetadataAlternative(
                    ProjectMetadataField.Languages,
                    ["de-CH"],
                    2,
                    "de-CH")
            ],
            new MetadataValue<IReadOnlyList<string>>([], MetadataOrigin.None),
            LanguageOptionCatalog.All.Select(option => new ConceptMetadataCandidate(
                ProjectMetadataField.Languages,
                option.DisplayText,
                option.Value,
                0,
                MetadataOrigin.StaticList)));

        var candidate = Assert.Single(result, candidate =>
            candidate.ValueToCommit is string value && value == "de-CH");
        var catalogOption = Assert.Single(LanguageOptionCatalog.All, option =>
            string.Equals(option.Value, "de-CH", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(catalogOption.DisplayText, candidate.DisplayValue);
        Assert.Equal(MetadataOrigin.SourceAggregation, candidate.EvidenceOrigin);
        Assert.Equal(2, candidate.SourceCount);
    }

    [Fact]
    public void LanguageField_FreeValidBcp47DraftStillCommits()
    {
        var metadata = new ProjectMetadata();
        var preferences = new MetadataPreferences();
        var field = new ConceptMetadataFieldViewModel(
            () => metadata,
            preferences,
            new MetadataNormalizationService(),
            ProjectMetadataField.Languages,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Languages),
            _ => { },
            _ => { });

        field.Draft = "sr-Latn-RS";
        field.CommitDraft();

        Assert.Contains(
            field.Values,
            value => string.Equals(value.Value, "sr-Latn-RS", StringComparison.OrdinalIgnoreCase));
    }
}
