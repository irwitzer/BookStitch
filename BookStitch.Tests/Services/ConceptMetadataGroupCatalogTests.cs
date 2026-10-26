using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ConceptMetadataGroupCatalogTests
{
    [Fact]
    public void RemainingCanonicalFields_AreAssignedOnceToTheIntendedConceptGroups()
    {
        var definitions = ConceptMetadataGroupCatalog.PublicationAdditional
            .Concat(ConceptMetadataGroupCatalog.Language)
            .Concat(ConceptMetadataGroupCatalog.CatalogData)
            .Concat(ConceptMetadataGroupCatalog.Sorting)
            .Concat(ConceptMetadataGroupCatalog.DescriptionAdditional)
            .Concat(ConceptMetadataGroupCatalog.Technical)
            .Concat(ConceptMetadataGroupCatalog.PassThrough)
            .Append(ConceptMetadataGroupCatalog.Description)
            .ToArray();

        Assert.Equal(definitions.Length, definitions.Select(item => item.Field).Distinct().Count());
        Assert.Contains(definitions, item => item.Field == ProjectMetadataField.Languages);
        Assert.Contains(definitions, item => item.Field == ProjectMetadataField.Categories);
        Assert.Contains(definitions, item => item.Field == ProjectMetadataField.ISBN);
        Assert.Contains(definitions, item => item.Field == ProjectMetadataField.SortSeries);
        Assert.Contains(definitions, item => item.Field == ProjectMetadataField.Description);
        Assert.Contains(definitions, item => item.Field == ProjectMetadataField.ContentType);
        Assert.Contains(definitions, item => item.Field == ProjectMetadataField.Source && !item.IsReadOnly);
    }

    [Fact]
    public void PassThroughGroup_ContainsCanonicalPassThroughFieldsWithoutDuplicatePlacements()
    {
        Assert.Equal(
            [ProjectMetadataField.ContentType, ProjectMetadataField.TargetLanguages,
             ProjectMetadataField.Script, ProjectMetadataField.PhonogramCopyright, ProjectMetadataField.Comment,
             ProjectMetadataField.CatalogNumbers, ProjectMetadataField.Categories],
            ConceptMetadataGroupCatalog.PassThrough.Select(item => item.Field));

        Assert.DoesNotContain(ConceptMetadataGroupCatalog.Language, item => item.Field == ProjectMetadataField.TargetLanguages);
        Assert.DoesNotContain(ConceptMetadataGroupCatalog.Language, item => item.Field == ProjectMetadataField.Script);
        Assert.DoesNotContain(ConceptMetadataGroupCatalog.PublicationAdditional, item => item.Field == ProjectMetadataField.PhonogramCopyright);
        Assert.DoesNotContain(ConceptMetadataGroupCatalog.DescriptionAdditional, item => item.Field == ProjectMetadataField.Comment);
        Assert.DoesNotContain(ConceptMetadataGroupCatalog.CatalogData, item => item.Field == ProjectMetadataField.CatalogNumbers);
        Assert.DoesNotContain(ConceptMetadataGroupCatalog.CatalogData, item => item.Field == ProjectMetadataField.Categories);
        Assert.DoesNotContain(ConceptMetadataGroupCatalog.PassThrough, item => item.Field == ProjectMetadataField.Composers);
        Assert.DoesNotContain(ConceptMetadataGroupCatalog.Technical, item => item.Field == ProjectMetadataField.ContentType);
        Assert.Contains(ConceptMetadataGroupCatalog.PublicationAdditional, item => item.Field == ProjectMetadataField.Source && !item.IsReadOnly);
        Assert.Empty(ConceptMetadataGroupCatalog.Technical);
    }

    [Fact]
    public void FieldDescriptors_DeclarePassThroughAsInteractionModeWithoutChangingValueKinds()
    {
        Assert.Equal(ConceptFieldInteractionMode.PassThrough,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.ContentType).InteractionMode);
        Assert.Equal(ConceptFieldInteractionMode.PassThrough,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.TargetLanguages).InteractionMode);
        Assert.Equal(ConceptFieldInteractionMode.Standard,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Title).InteractionMode);
        Assert.Equal(ConceptFieldInteractionMode.Standard,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Authors).InteractionMode);
        Assert.Equal(ConceptFieldInteractionMode.Standard,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Composers).InteractionMode);
        Assert.Equal(ConceptFieldInteractionMode.PassThrough,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Script).InteractionMode);
        Assert.Equal(ConceptFieldInteractionMode.PassThrough,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.PhonogramCopyright).InteractionMode);
        Assert.Equal(ConceptFieldInteractionMode.PassThrough,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Comment).InteractionMode);
        Assert.Equal(ConceptFieldInteractionMode.PassThrough,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.CatalogNumbers).InteractionMode);
        Assert.Equal(ConceptFieldInteractionMode.PassThrough,
            ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Categories).InteractionMode);

        foreach (var field in new[]
                 {
                     ProjectMetadataField.Source, ProjectMetadataField.Copyright, ProjectMetadataField.Languages,
                     ProjectMetadataField.OriginalLanguages, ProjectMetadataField.Keywords,
                     ProjectMetadataField.ISBN, ProjectMetadataField.ASIN, ProjectMetadataField.Barcode,
                     ProjectMetadataField.MusicBrainzReleaseId
                 })
        {
            Assert.Equal(ConceptFieldInteractionMode.Standard, ConceptMetadataFieldCatalog.Get(field).InteractionMode);
        }

        Assert.Equal(ProjectMetadataFieldValueKind.String, ProjectMetadataFields.GetValueKind(ProjectMetadataField.ContentType));
        Assert.Equal(ProjectMetadataFieldValueKind.StringList, ProjectMetadataFields.GetValueKind(ProjectMetadataField.TargetLanguages));
        Assert.Equal(ProjectMetadataFieldValueKind.StringList, ProjectMetadataFields.GetValueKind(ProjectMetadataField.Composers));
        Assert.Equal(ProjectMetadataFieldValueKind.StringList, ProjectMetadataFields.GetValueKind(ProjectMetadataField.CatalogNumbers));
        Assert.Equal(ProjectMetadataFieldValueKind.StringList, ProjectMetadataFields.GetValueKind(ProjectMetadataField.Categories));
        Assert.Equal(48, (int)ProjectMetadataField.Composers);
    }

    [Theory]
    [InlineData(ProjectMetadataField.ContentType)]
    [InlineData(ProjectMetadataField.TargetLanguages)]
    [InlineData(ProjectMetadataField.Script)]
    [InlineData(ProjectMetadataField.PhonogramCopyright)]
    [InlineData(ProjectMetadataField.Comment)]
    [InlineData(ProjectMetadataField.CatalogNumbers)]
    [InlineData(ProjectMetadataField.Categories)]
    public void PassThroughFieldDescriptors_UseSharedSemanticInfoResources(ProjectMetadataField field)
    {
        var info = Assert.IsType<ConceptFieldInfoDescriptor>(ConceptMetadataFieldCatalog.Get(field).Info);

        Assert.Equal("InfoPassThroughTitle", info.TitleResourceKey);
        Assert.Equal("InfoPassThroughBody", info.BodyResourceKey);
    }

    [Theory]
    [InlineData(ProjectMetadataField.Languages, ConceptFieldLockKind.LanguagePreference)]
    [InlineData(ProjectMetadataField.Genres, ConceptFieldLockKind.GenrePreference)]
    public void LockableFieldDescriptors_DeclareOwnedLockCapability(
        ProjectMetadataField field,
        ConceptFieldLockKind expectedLockKind)
    {
        Assert.Equal(expectedLockKind, ConceptMetadataFieldCatalog.Get(field).LockKind);
    }

    [Fact]
    public void FieldDescriptors_ExposeLocalizedAndTechnicalNamesForSeriesFields()
    {
        var series = ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Series);
        var seriesPart = ConceptMetadataFieldCatalog.Get(ConceptMetadataFieldCatalog.SeriesPartId);

        Assert.Equal("Metadata.Field.Series", series.NameResourceKey);
        Assert.Equal("Reihe", series.GetDisplayName(false));
        Assert.Equal("Series", series.TechnicalName);
        Assert.Contains("Reihe", series.SearchTerms);
        Assert.Contains("Series", series.SearchTerms);

        Assert.Equal("Metadata.Field.SeriesPart", seriesPart.NameResourceKey);
        Assert.Equal("Reihen Nr.", seriesPart.GetDisplayName(false));
        Assert.Equal("Series Part", seriesPart.TechnicalName);
        Assert.Contains("Series Part", seriesPart.SearchTerms);

        var trackNumber = ConceptMetadataFieldCatalog.Get(ProjectMetadataField.OutputTrackNumber);
        Assert.Equal("Track Nr.", trackNumber.GetDisplayName(false));
        Assert.Contains("Output Track Number", trackNumber.SearchTerms);
    }

    [Fact]
    public void FieldDescriptorsAndSearchTermsFollowTheActiveUiCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
            var series = ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Series);

            Assert.Equal("Serie", series.GetDisplayName(false));
            Assert.Contains("Serie", series.SearchTerms);
            Assert.Contains("Series", series.SearchTerms);
            Assert.DoesNotContain("Reihe", series.SearchTerms);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void FieldDescriptorHasNoLegacySearchAliasApi()
    {
        Assert.Null(typeof(ConceptMetadataFieldDescriptor).GetProperty("SearchAliases"));
        var descriptor = ConceptMetadataFieldCatalog.Get(ProjectMetadataField.Series);
        Assert.Equal(2, descriptor.SearchTerms.Count);
        Assert.Contains(descriptor.GetDisplayName(false), descriptor.SearchTerms);
        Assert.Contains(descriptor.TechnicalName, descriptor.SearchTerms);
    }
}
