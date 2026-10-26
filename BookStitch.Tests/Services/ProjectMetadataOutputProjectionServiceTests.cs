using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectMetadataOutputProjectionServiceTests
{
    [Fact]
    public void Create_ProjectsCurrentMetadataValuesAndDisabledPoliciesWithoutMutatingStore()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, " Titel ", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Subtitle, " Untertitel ", MetadataOrigin.User);
        metadata.SetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType, AudioWorkType.AudioDrama, MetadataOrigin.User);
        metadata.SetValue<AbridgedStatus?>(ProjectMetadataField.AbridgedStatus, AbridgedStatus.Unabridged, MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Volume, " Band 2 ", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Edition, " Ausgabe A ", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, [" Autor A ", "Autor B"], MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers, [" Composer A ", "Composer B"], MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<SeriesEntry>>(
            ProjectMetadataField.Series,
            [new SeriesEntry(" Reihe ", " 2 "), new SeriesEntry(" Nebenreihe ", " A ")],
            MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Description, "Klappentext", MetadataOrigin.User);
        var policies = new Dictionary<ProjectMetadataField, OutputFieldPolicy>
        {
            [ProjectMetadataField.Series] = OutputFieldPolicy.Disabled,
            [ProjectMetadataField.AbridgedStatus] = OutputFieldPolicy.Disabled,
            [ProjectMetadataField.Composers] = OutputFieldPolicy.Disabled
        };
        var bindings = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [ProjectMetadataFieldBindingCatalog.NarratorsToComposersId] = false
        };
        var revision = metadata.Revision;

        var result = new ProjectMetadataOutputProjectionService().Create(
            metadata,
            new ProjectMetadataOutputProjectionOptions(
                "cover.jpg",
                CompatibilityDescriptionEnabled: true,
                OutputPresentationCulture: "de-DE"),
            policies,
            bindings);

        Assert.Equal("Titel", result.Title);
        Assert.Equal("Untertitel", result.Subtitle);
        Assert.Equal(AudioWorkType.AudioDrama, result.AudioWorkType);
        Assert.Equal(AbridgedStatus.Unabridged, result.AbridgedStatus);
        Assert.Equal("Band 2", result.Volume);
        Assert.Equal("Ausgabe A", result.Edition);
        Assert.False(result.IsFieldEnabled(ProjectMetadataField.AbridgedStatus));
        Assert.True(result.IsFieldEnabled(ProjectMetadataField.Edition));
        Assert.Equal(["Autor A", "Autor B"], result.Authors);
        Assert.Equal(["Composer A", "Composer B"], result.Composers);
        Assert.False(result.IsFieldEnabled(ProjectMetadataField.Composers));
        Assert.Equal(
            [new SeriesEntry("Reihe", "2"), new SeriesEntry("Nebenreihe", "A")],
            result.Series);
        Assert.False(result.IsFieldEnabled(ProjectMetadataField.Series));
        Assert.Equal("cover.jpg", result.CoverPath);
        Assert.Equal(revision, metadata.Revision);
    }

    [Fact]
    public void Create_UsesGlobalBindingSnapshotAndIgnoresLegacyProjectFlagAndSourceOutputPolicy()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators, [" Sprecher A ", "Sprecher B"], MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers, ["Komponist"], MetadataOrigin.User);
        metadata.SetUseNarratorsAsComposers(false);
        var policies = new Dictionary<ProjectMetadataField, OutputFieldPolicy>
        {
            [ProjectMetadataField.Narrators] = OutputFieldPolicy.Disabled,
            [ProjectMetadataField.Composers] = OutputFieldPolicy.Enabled
        };
        var bindings = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [ProjectMetadataFieldBindingCatalog.NarratorsToComposersId] = true
        };

        var result = new ProjectMetadataOutputProjectionService().Create(
            metadata,
            new ProjectMetadataOutputProjectionOptions(string.Empty, false, "de-DE"),
            policies,
            bindings);

        Assert.Equal(["Sprecher A", "Sprecher B"], result.Narrators);
        Assert.Equal(["Sprecher A", "Sprecher B"], result.Composers);
        Assert.False(result.IsFieldEnabled(ProjectMetadataField.Narrators));
        Assert.True(result.IsFieldEnabled(ProjectMetadataField.Composers));
        Assert.False(metadata.UseNarratorsAsComposers);
        Assert.Equal(["Komponist"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers).Value);
    }

    [Fact]
    public void Create_GenericBindingResolutionSupportsScalarTargetsAndRejectsCompetingActiveBindings()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Quelltitel", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Eigenes Album", MetadataOrigin.User);
        var titleToAlbum = new ProjectMetadataFieldBindingDefinition(
            "test-title-to-album",
            ProjectMetadataField.Title,
            ProjectMetadataField.Album,
            defaultEnabled: false);
        var secondAlbumBinding = new ProjectMetadataFieldBindingDefinition(
            "test-subtitle-to-album",
            ProjectMetadataField.Subtitle,
            ProjectMetadataField.Album,
            defaultEnabled: false);
        var policies = new Dictionary<ProjectMetadataField, OutputFieldPolicy>();

        var result = new ProjectMetadataOutputProjectionService().Create(
            metadata,
            new ProjectMetadataOutputProjectionOptions(string.Empty, false, "de-DE"),
            policies,
            new Dictionary<string, bool>(StringComparer.Ordinal) { [titleToAlbum.BindingId] = true },
            [titleToAlbum]);

        Assert.Equal("Quelltitel", result.Album);
        Assert.Equal("Eigenes Album", metadata.GetValue<string>(ProjectMetadataField.Album).Value);

        var conflicting = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [titleToAlbum.BindingId] = true,
            [secondAlbumBinding.BindingId] = true
        };
        Assert.Throws<InvalidOperationException>(() => new ProjectMetadataOutputProjectionService().Create(
            metadata,
            new ProjectMetadataOutputProjectionOptions(string.Empty, false, "de-DE"),
            policies,
            conflicting,
            [titleToAlbum, secondAlbumBinding]));
    }
    [Fact]
    public void SynchronizeActiveTargets_RetainsTargetOnceAndIsIdempotent()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Composers,
            ["Komponist A"],
            MetadataOrigin.User);
        var snapshot = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [ProjectMetadataFieldBindingCatalog.NarratorsToComposersId] = true
        };
        var service = new ProjectMetadataFieldBindingSynchronizationService();

        Assert.True(service.SynchronizeActiveTargets(metadata, snapshot));
        Assert.Empty(metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers).Value ?? []);
        Assert.Equal(["Komponist A"], metadata.GetSuppressedValues(ProjectMetadataField.Composers));

        Assert.False(service.SynchronizeActiveTargets(metadata, snapshot));
        Assert.Equal(["Komponist A"], metadata.GetSuppressedValues(ProjectMetadataField.Composers));
    }

    [Fact]
    public void ProductiveTitleToAlbumBinding_UsesGenericOutputProjectionAndIndependentOutputPolicies()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Titel als Quelle", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Eigenes Album", MetadataOrigin.User);
        var policies = new Dictionary<ProjectMetadataField, OutputFieldPolicy>
        {
            [ProjectMetadataField.Title] = OutputFieldPolicy.Disabled,
            [ProjectMetadataField.Album] = OutputFieldPolicy.Enabled
        };
        var snapshot = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [ProjectMetadataFieldBindingCatalog.TitleToAlbumId] = true
        };

        var result = new ProjectMetadataOutputProjectionService().Create(
            metadata,
            new ProjectMetadataOutputProjectionOptions(string.Empty, false, "de-DE"),
            policies,
            snapshot);

        Assert.Equal("Titel als Quelle", result.Album);
        Assert.Contains(ProjectMetadataField.Title, result.DisabledFields);
        Assert.DoesNotContain(ProjectMetadataField.Album, result.DisabledFields);
        Assert.Equal("Eigenes Album", metadata.GetValue<string>(ProjectMetadataField.Album).Value);
    }

    [Fact]
    public void SynchronizeActiveTargets_TitleToAlbumRetainsScalarTargetOnceAndIsIdempotent()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Titel A", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Album, "Album A", MetadataOrigin.User);
        var snapshot = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [ProjectMetadataFieldBindingCatalog.TitleToAlbumId] = true
        };
        var service = new ProjectMetadataFieldBindingSynchronizationService();

        Assert.True(service.SynchronizeActiveTargets(metadata, snapshot));
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Equal(["Album A"], metadata.GetSuppressedValues(ProjectMetadataField.Album));

        Assert.False(service.SynchronizeActiveTargets(metadata, snapshot));
        Assert.Equal(["Album A"], metadata.GetSuppressedValues(ProjectMetadataField.Album));
    }

}
