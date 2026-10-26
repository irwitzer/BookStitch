using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectMetadataStoreTests
{
    private readonly ProjectMetadataStore _store = new();

    [Fact]
    public void DeletedCoverFingerprint_RoundtripsThroughExistingSuppressionStoreAndAllowsManualRestore()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SuppressValue(ProjectMetadataField.Cover, "ABC123");
        metadata.SetCover(null, null, MetadataOrigin.User);
        _store.Save(folder.Path, metadata);

        var restored = _store.LoadOrCreate(folder.Path);
        Assert.Equal(new[] { "ABC123" }, restored.GetSuppressedValues(ProjectMetadataField.Cover));
        Assert.Empty(restored.GetSuppressedValues(ProjectMetadataField.Title));
        Assert.True(restored.UnsuppressValue(ProjectMetadataField.Cover, "abc123"));
        restored.SetCover("manual.png", "manual.jpg", MetadataOrigin.User);
        _store.Save(folder.Path, restored);
        Assert.Empty(_store.LoadOrCreate(folder.Path).GetSuppressedValues(ProjectMetadataField.Cover));
    }

    [Fact]
    public void LoadOrCreate_WhenFileIsMissing_ReturnsEmptyMetadataWithoutCreatingFile()
    {
        using var folder = new TemporaryFolder();

        var metadata = _store.LoadOrCreate(folder.Path);

        Assert.Equal(0, metadata.Revision);
        Assert.Equal(MetadataOrigin.None, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.False(File.Exists(ProjectFolderLayout.GetProjectMetadataPath(folder.Path)));
    }

    [Fact]
    public void SaveAndLoad_RoundtripsValuesAndRevisionWithoutProjectPolicies()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Titel", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Subtitle, "Untertitel", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Volume, "Band 4", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.Edition, "Jubiläumsausgabe", MetadataOrigin.User);
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            ["Müller", "王"],
            MetadataOrigin.SourceAggregation);
        metadata.SetValue<IReadOnlyList<SeriesEntry>>(
            ProjectMetadataField.Series,
            [new SeriesEntry("Reihe", "1"), new SeriesEntry("Nebenreihe", "A")],
            MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.ReleaseDate, new MetadataDate(2024, 7), MetadataOrigin.SourceAggregation);
        metadata.SetValue(ProjectMetadataField.Year, 2024, MetadataOrigin.Derived);
        metadata.SetValue(ProjectMetadataField.AudioWorkType, AudioWorkType.AudioDrama, MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.AbridgedStatus, AbridgedStatus.Unabridged, MetadataOrigin.User);
        metadata.SetCover(@"D:\Cover.png", @"D:\Cover.jpg", MetadataOrigin.User);
        metadata.SetUseNarratorsAsComposers(true);
        var expectedRevision = metadata.Revision;

        _store.Save(folder.Path, metadata);
        var loaded = _store.LoadOrCreate(folder.Path);

        Assert.Equal(expectedRevision, loaded.Revision);
        Assert.Equal("Titel", loaded.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal("Untertitel", loaded.GetValue<string>(ProjectMetadataField.Subtitle).Value);
        Assert.Equal("Band 4", loaded.GetValue<string>(ProjectMetadataField.Volume).Value);
        Assert.Equal("Jubiläumsausgabe", loaded.GetValue<string>(ProjectMetadataField.Edition).Value);
        Assert.Equal(["Müller", "王"], loaded.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal([new SeriesEntry("Reihe", "1"), new SeriesEntry("Nebenreihe", "A")], loaded.GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series).Value);
        Assert.Equal(new MetadataDate(2024, 7), loaded.GetValue<MetadataDate?>(ProjectMetadataField.ReleaseDate).Value);
        Assert.Equal(2024, loaded.GetValue<int?>(ProjectMetadataField.Year).Value);
        Assert.Equal(AudioWorkType.AudioDrama, loaded.GetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType).Value);
        Assert.Equal(AbridgedStatus.Unabridged, loaded.GetValue<AbridgedStatus?>(ProjectMetadataField.AbridgedStatus).Value);
        Assert.Equal(@"D:\Cover.png", loaded.Cover.SourcePath);
        Assert.Equal(@"D:\Cover.jpg", loaded.Cover.ProcessedPath);
        Assert.Equal(MetadataOrigin.User, loaded.Cover.Origin);
        Assert.True(loaded.UseNarratorsAsComposers);
        Assert.DoesNotContain("OutputFieldPolicies", File.ReadAllText(ProjectFolderLayout.GetProjectMetadataPath(folder.Path)));
    }

    [Fact]
    public void LoadOrCreate_SchemaV1PoliciesAreIgnoredWithoutChangingGlobalPreferences()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var path = ProjectFolderLayout.GetProjectMetadataPath(folder.Path);
        File.WriteAllText(path,
            """
            {
              "schemaVersion": 1,
              "Revision": 7,
              "Values": [],
              "OutputFieldPolicies": { "Description": "Disabled" }
            }
            """);
        var preferences = new MetadataPreferences();

        var loaded = _store.LoadOrCreate(folder.Path);

        Assert.Equal(7, loaded.Revision);
        Assert.Equal(OutputFieldPolicy.Enabled,
            MetadataPreferencesService.GetPolicy(preferences, ProjectMetadataField.Description));
    }

    [Fact]
    public void LoadOrCreate_WhenFileIsCorrupt_ThrowsAndDoesNotOverwrite()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var path = ProjectFolderLayout.GetProjectMetadataPath(folder.Path);
        const string corruptJson = "{ invalid";
        File.WriteAllText(path, corruptJson);

        Assert.ThrowsAny<Exception>(() => _store.LoadOrCreate(folder.Path));
        Assert.Equal(corruptJson, File.ReadAllText(path));
    }

    [Fact]
    public void LoadOrCreate_WhenSchemaVersionIsFuture_ThrowsPersistentSchemaException()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        var path = ProjectFolderLayout.GetProjectMetadataPath(folder.Path);
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 99,
              "Revision": 0,
              "Values": []
            }
            """);

        Assert.Throws<PersistentJsonSchemaException>(() => _store.LoadOrCreate(folder.Path));
    }

    [Fact]
    public void LoadOrCreate_IgnoresRetiredLegacyDateFields()
    {
        using var folder = new TemporaryFolder();
        ProjectFolderLayout.EnsureProjectFolders(folder.Path);
        File.WriteAllText(
            ProjectFolderLayout.GetProjectMetadataPath(folder.Path),
            """
            {
              "schemaVersion": 2,
              "Revision": 3,
              "Cover": null,
              "Values": [
                { "Field": "OriginalReleaseDate", "Origin": "LegacyMigrated", "Value": { "Year": 1999, "Month": 12, "Day": null } },
                { "Field": "PublicationDate", "Origin": "LegacyMigrated", "Value": { "Year": 2024, "Month": 7, "Day": 15 } },
                { "Field": "ReleaseDate", "Origin": "SourceAggregation", "Value": { "Year": 2026, "Month": 1, "Day": 31 } },
                { "Field": "Year", "Origin": "SourceAggregation", "Value": 2026 }
              ]
            }
            """);

        var metadata = _store.LoadOrCreate(folder.Path);

        Assert.Equal(new MetadataDate(2026, 1, 31), metadata.GetValue<MetadataDate?>(ProjectMetadataField.ReleaseDate).Value);
        Assert.Equal(2026, metadata.GetValue<int?>(ProjectMetadataField.Year).Value);
        Assert.DoesNotContain(metadata.GetExplicitValues().Keys, field => field.ToString() is "OriginalReleaseDate" or "PublicationDate");
    }

    [Fact]
    public void CanonicalFieldSurface_RetiresLegacyParticipantRolesButKeepsMigratedConceptFields()
    {
        var canonical = ProjectMetadataFields.OutputFields;

        Assert.DoesNotContain(ProjectMetadataField.Performers, canonical);
        Assert.DoesNotContain(ProjectMetadataField.Contributors, canonical);
        Assert.DoesNotContain(ProjectMetadataField.Hosts, canonical);
        Assert.DoesNotContain(ProjectMetadataField.Guests, canonical);
        Assert.DoesNotContain(ProjectMetadataField.Speakers, canonical);

        Assert.Contains(ProjectMetadataField.Translators, canonical);
        Assert.Contains(ProjectMetadataField.Publisher, canonical);
        Assert.Contains(ProjectMetadataField.ReleaseDate, canonical);
        Assert.Contains(ProjectMetadataField.Year, canonical);
        Assert.Contains(ProjectMetadataField.License, canonical);
        Assert.Contains(ProjectMetadataField.OutputTrackNumber, canonical);
        Assert.Contains(ProjectMetadataField.OutputTrackTotal, canonical);

        var metadata = new ProjectMetadata();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            metadata.SetValue<IReadOnlyList<string>>(
                ProjectMetadataField.Performers,
                ["legacy"],
                MetadataOrigin.User));
    }

    [Fact]
    public void SaveAndLoad_RoundtripsEveryCanonicalFieldWithValueOriginAndRevision()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();

        foreach (var field in ProjectMetadataFields.OutputFields)
            SetRepresentativeValue(metadata, field);

        var expectedRevision = metadata.Revision;
        Assert.Equal(ProjectMetadataFields.OutputFields.Count, metadata.GetExplicitValues().Count);

        _store.Save(folder.Path, metadata);
        var loaded = _store.LoadOrCreate(folder.Path);

        Assert.Equal(expectedRevision, loaded.Revision);
        Assert.Equal(ProjectMetadataFields.OutputFields.Count, loaded.GetExplicitValues().Count);
        foreach (var field in ProjectMetadataFields.OutputFields)
            AssertRepresentativeValue(loaded, field);
    }

    [Fact]
    public void Save_WithExpectedRevision_RejectsAStaleWriterWithoutOverwritingNewerCanonicalMetadata()
    {
        using var folder = new TemporaryFolder();
        var original = new ProjectMetadata();
        original.SetValue(ProjectMetadataField.Title, "Original", MetadataOrigin.User);
        _store.Save(folder.Path, original);

        var stale = _store.LoadOrCreate(folder.Path);
        var expectedRevision = stale.Revision;
        var newer = _store.LoadOrCreate(folder.Path);
        newer.SetValue(ProjectMetadataField.Album, "Neuere Änderung", MetadataOrigin.User);
        _store.Save(folder.Path, newer);

        stale.SetValue(ProjectMetadataField.Description, "Staler Schreibversuch", MetadataOrigin.User);

        Assert.Throws<InvalidOperationException>(() =>
            _store.Save(folder.Path, stale, expectedRevision));

        var persisted = _store.LoadOrCreate(folder.Path);
        Assert.Equal("Neuere Änderung", persisted.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Null(persisted.GetValue<string>(ProjectMetadataField.Description).Value);
    }

    [Fact]
    public void SaveLoad_PreservesSuppressedValues()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SuppressValue(ProjectMetadataField.Genres, "Krimi");
        metadata.SuppressValue(ProjectMetadataField.Genres, "Fantasy");

        _store.Save(folder.Path, metadata);
        var loaded = _store.LoadOrCreate(folder.Path);

        Assert.Equal(["Krimi", "Fantasy"], loaded.GetSuppressedValues(ProjectMetadataField.Genres));
    }

    private static void SetRepresentativeValue(ProjectMetadata metadata, ProjectMetadataField field)
    {
        switch (ProjectMetadataFields.GetValueKind(field))
        {
            case ProjectMetadataFieldValueKind.String:
                metadata.SetValue<string>(field, $"Wert-{field}-ä王", MetadataOrigin.User);
                break;
            case ProjectMetadataFieldValueKind.StringList:
                metadata.SetValue<IReadOnlyList<string>>(
                    field,
                    [$"A-{field}", $"B-{field}"],
                    MetadataOrigin.User);
                break;
            case ProjectMetadataFieldValueKind.SeriesList:
                metadata.SetValue<IReadOnlyList<SeriesEntry>>(
                    field,
                    [new SeriesEntry("Reihe", "2")],
                    MetadataOrigin.User);
                break;
            case ProjectMetadataFieldValueKind.MetadataDate:
                metadata.SetValue<MetadataDate?>(field, new MetadataDate(2024, 7, 31), MetadataOrigin.User);
                break;
            case ProjectMetadataFieldValueKind.Integer:
                metadata.SetValue<int?>(field, 42, MetadataOrigin.User);
                break;
            case ProjectMetadataFieldValueKind.AudioWorkType:
                metadata.SetValue<AudioWorkType?>(field, AudioWorkType.AudioDrama, MetadataOrigin.User);
                break;
            case ProjectMetadataFieldValueKind.AbridgedStatus:
                metadata.SetValue<AbridgedStatus?>(field, AbridgedStatus.Unabridged, MetadataOrigin.User);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    private static void AssertRepresentativeValue(ProjectMetadata metadata, ProjectMetadataField field)
    {
        switch (ProjectMetadataFields.GetValueKind(field))
        {
            case ProjectMetadataFieldValueKind.String:
            {
                var value = metadata.GetValue<string>(field);
                Assert.Equal($"Wert-{field}-ä王", value.Value);
                Assert.Equal(MetadataOrigin.User, value.Origin);
                break;
            }
            case ProjectMetadataFieldValueKind.StringList:
            {
                var value = metadata.GetValue<IReadOnlyList<string>>(field);
                Assert.Equal([$"A-{field}", $"B-{field}"], value.Value);
                Assert.Equal(MetadataOrigin.User, value.Origin);
                break;
            }
            case ProjectMetadataFieldValueKind.SeriesList:
            {
                var value = metadata.GetValue<IReadOnlyList<SeriesEntry>>(field);
                Assert.Equal([new SeriesEntry("Reihe", "2")], value.Value);
                Assert.Equal(MetadataOrigin.User, value.Origin);
                break;
            }
            case ProjectMetadataFieldValueKind.MetadataDate:
            {
                var value = metadata.GetValue<MetadataDate?>(field);
                Assert.Equal(new MetadataDate(2024, 7, 31), value.Value);
                Assert.Equal(MetadataOrigin.User, value.Origin);
                break;
            }
            case ProjectMetadataFieldValueKind.Integer:
            {
                var value = metadata.GetValue<int?>(field);
                Assert.Equal(42, value.Value);
                Assert.Equal(MetadataOrigin.User, value.Origin);
                break;
            }
            case ProjectMetadataFieldValueKind.AudioWorkType:
            {
                var value = metadata.GetValue<AudioWorkType?>(field);
                Assert.Equal(AudioWorkType.AudioDrama, value.Value);
                Assert.Equal(MetadataOrigin.User, value.Origin);
                break;
            }
            case ProjectMetadataFieldValueKind.AbridgedStatus:
            {
                var value = metadata.GetValue<AbridgedStatus?>(field);
                Assert.Equal(AbridgedStatus.Unabridged, value.Value);
                Assert.Equal(MetadataOrigin.User, value.Origin);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

}
