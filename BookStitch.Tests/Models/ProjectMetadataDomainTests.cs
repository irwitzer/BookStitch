using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class ProjectMetadataDomainTests
{
    [Fact]
    public void MetadataDate_PreservesPartialPrecision()
    {
        Assert.Equal("2024", new MetadataDate(2024).ToString());
        Assert.Equal("2024-07", new MetadataDate(2024, 7).ToString());
        Assert.Equal("2024-07-31", new MetadataDate(2024, 7, 31).ToString());
    }

    [Fact]
    public void MetadataDate_RejectsInventedOrInvalidDateParts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MetadataDate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MetadataDate(2024, 13));
        Assert.Throws<ArgumentException>(() => new MetadataDate(2024, day: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MetadataDate(2024, 2, 31));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MetadataDate(2024, 2, 0));
    }

    [Fact]
    public void MetadataValue_DistinguishesUnknownFromUserCleared()
    {
        var unknown = new MetadataValue<string>(null, MetadataOrigin.None);
        var userCleared = new MetadataValue<string>(null, MetadataOrigin.User);

        Assert.False(unknown.IsUserDefined);
        Assert.True(userCleared.IsUserDefined);
        Assert.NotEqual(unknown, userCleared);
    }

    [Fact]
    public void MetadataPreferencesPolicy_IsIndependentFromProjectValueOriginAndRevision()
    {
        var metadata = new ProjectMetadata();
        var preferences = new MetadataPreferences();

        metadata.SetValue(ProjectMetadataField.Description, "Klappentext", MetadataOrigin.User);
        var revision = metadata.Revision;
        MetadataPreferencesService.SetPolicy(preferences, ProjectMetadataField.Description, OutputFieldPolicy.Disabled);

        Assert.Equal(OutputFieldPolicy.Disabled, MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.Description));
        var description = metadata.GetValue<string>(ProjectMetadataField.Description);
        Assert.Equal("Klappentext", description.Value);
        Assert.Equal(MetadataOrigin.User, description.Origin);
        Assert.Equal(OutputFieldPolicy.Enabled, MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.Title));
        Assert.Equal(revision, metadata.Revision);
    }

    [Fact]
    public void ProjectMetadata_IncrementsRevisionOncePerSemanticChange()
    {
        var metadata = new ProjectMetadata();
        var changed = new List<string>();
        metadata.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        Assert.True(metadata.SetValue(ProjectMetadataField.Title, " Titel ", MetadataOrigin.User));
        Assert.Equal(1, metadata.Revision);
        Assert.Equal("Titel", metadata.GetValue<string>(ProjectMetadataField.Title).Value);

        Assert.False(metadata.SetValue(ProjectMetadataField.Title, "Titel", MetadataOrigin.User));
        Assert.Equal(1, metadata.Revision);

        Assert.Contains(nameof(ProjectMetadata.Revision), changed);
        Assert.Contains(nameof(ProjectMetadataField.Title), changed);
    }

    [Fact]
    public void ProjectMetadata_ListValuesKeepOrderAndCannotBeMutatedThroughOriginalList()
    {
        var metadata = new ProjectMetadata();
        var authors = new List<string> { "Müller", "Schmidt" };

        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            authors,
            MetadataOrigin.SourceAggregation);
        authors[0] = "Changed";
        authors.Add("Later");

        var value = metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors);

        Assert.Equal(["Müller", "Schmidt"], value.Value);
        Assert.True(metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            ["Schmidt", "Müller"],
            MetadataOrigin.SourceAggregation));
    }

    [Fact]
    public void ProjectMetadata_SeriesValuesKeepOrderAndCannotBeMutatedThroughOriginalList()
    {
        var metadata = new ProjectMetadata();
        var series = new List<SeriesEntry>
        {
            new("Reihe", "1"),
            new("Nebenreihe", "A")
        };

        metadata.SetValue<IReadOnlyList<SeriesEntry>>(
            ProjectMetadataField.Series,
            series,
            MetadataOrigin.User);
        series[0] = new SeriesEntry("Changed", "9");

        var value = metadata.GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series);

        Assert.Equal([new SeriesEntry("Reihe", "1"), new SeriesEntry("Nebenreihe", "A")], value.Value);
    }

    [Fact]
    public void ProjectMetadataField_DateModelContainsOnlyReleaseDateAndYear()
    {
        var names = Enum.GetNames<ProjectMetadataField>();

        Assert.Contains(nameof(ProjectMetadataField.ReleaseDate), names);
        Assert.Contains(nameof(ProjectMetadataField.Year), names);
        Assert.DoesNotContain("OriginalReleaseDate", names);
        Assert.DoesNotContain("PublicationDate", names);
        Assert.Equal(ProjectMetadataFieldValueKind.MetadataDate, ProjectMetadataFields.GetValueKind(ProjectMetadataField.ReleaseDate));
        Assert.Equal(ProjectMetadataFieldValueKind.Integer, ProjectMetadataFields.GetValueKind(ProjectMetadataField.Year));
        Assert.Equal(28, (int)ProjectMetadataField.Year);
        Assert.Equal(47, (int)ProjectMetadataField.OutputTrackTotal);
        Assert.Equal(48, (int)ProjectMetadataField.Composers);
        Assert.Equal(ProjectMetadataFieldValueKind.StringList, ProjectMetadataFields.GetValueKind(ProjectMetadataField.Composers));
    }

    [Fact]
    public void ProjectMetadataField_OutputFieldsExposeStableEnumIds()
    {
        Assert.Contains(ProjectMetadataField.Barcode, ProjectMetadataFields.OutputFields);
        Assert.Contains(ProjectMetadataField.CatalogNumbers, ProjectMetadataFields.OutputFields);
        Assert.DoesNotContain("EAN", ProjectMetadataFields.OutputFields.Select(field => field.ToString()));
        Assert.Equal(ProjectMetadataFields.OutputFields.Count, ProjectMetadataFields.OutputFields.Distinct().Count());
    }

    [Fact]
    public void ProjectMetadata_RejectsAccessTypesThatDoNotMatchTheFieldSchema()
    {
        var metadata = new ProjectMetadata();

        Assert.Throws<ArgumentException>(() =>
            metadata.SetValue(ProjectMetadataField.Authors, "Ada", MetadataOrigin.User));
        Assert.Throws<ArgumentException>(() =>
            metadata.GetValue<string>(ProjectMetadataField.Authors));
        Assert.Throws<ArgumentException>(() =>
            metadata.SetValue<IReadOnlyList<string>>(
                ProjectMetadataField.Title,
                ["Titel"],
                MetadataOrigin.User));
        Assert.Throws<ArgumentException>(() =>
            metadata.SetValue<string[]>(
                ProjectMetadataField.Authors,
                ["Ada"],
                MetadataOrigin.User));
    }

    [Fact]
    public void ProjectMetadata_SuppressedValuesAreFieldScopedAndCanBeRestored()
    {
        var metadata = new ProjectMetadata();

        Assert.True(metadata.SuppressValue(ProjectMetadataField.Genres, " Krimi "));
        Assert.False(metadata.SuppressValue(ProjectMetadataField.Genres, "krimi"));
        Assert.Equal(["Krimi"], metadata.GetSuppressedValues(ProjectMetadataField.Genres));
        Assert.Empty(metadata.GetSuppressedValues(ProjectMetadataField.Authors));

        Assert.True(metadata.UnsuppressValue(ProjectMetadataField.Genres, "KRIMI"));
        Assert.Empty(metadata.GetSuppressedValues(ProjectMetadataField.Genres));
    }

}
