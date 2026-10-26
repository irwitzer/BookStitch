using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectMetadataAggregationPersistenceServiceTests
{
    [Fact]
    public void Apply_InitialSetComplete_MutatesCallerOwnedCanonicalInstanceWithoutPersistingIt()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        var service = new ProjectMetadataAggregationPersistenceService();
        SaveSnapshot(folder.Path, title: "Titel", author: "Autor", narrator: "Sprecher", genre: "Quelle");

        var result = service.Apply(
            metadata,
            new ProjectMetadataAggregationPersistenceRequest(
                folder.Path,
                ProjectMetadataInitializationState.InitialSetComplete));

        Assert.True(result.Changed);
        Assert.Same(metadata, result.CanonicalMetadata);
        Assert.Equal("Titel", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(["Autor"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(["Sprecher"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
        Assert.Equal(["Quelle"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Contains(ProjectMetadataField.Title, result.AppliedFields);
        Assert.Contains(ProjectMetadataField.Genres, result.AppliedFields);
        Assert.False(File.Exists(ProjectFolderLayout.GetProjectMetadataPath(folder.Path)));
    }

    [Fact]
    public void Apply_InitialSetComplete_MergesListValuesInNaturalSourcePathOrder()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        SaveSnapshot(
            folder.Path,
            sourcePath: @"D:\Audio\10 Track.m4a",
            genres: ["Genre 10"]);
        SaveSnapshot(
            folder.Path,
            sourcePath: @"D:\Audio\2 Track.m4a",
            genres: ["Genre 2A", "Genre 2B"]);
        SaveSnapshot(
            folder.Path,
            sourcePath: @"D:\Audio\1 Track.m4a",
            genres: ["Genre 1"]);

        new ProjectMetadataAggregationPersistenceService().Apply(
            metadata,
            new ProjectMetadataAggregationPersistenceRequest(
                folder.Path,
                ProjectMetadataInitializationState.InitialSetComplete));

        Assert.Equal(
            ["Genre 1", "Genre 2A", "Genre 2B", "Genre 10"],
            metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
    }

    [Fact]
    public void Apply_SubsequentSource_FillsOnlyRealNoneNullGapsOnTheLiveInstance()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "User Titel", MetadataOrigin.User);
        SaveSnapshot(folder.Path, title: "Source Titel", narrator: "Sprecher");

        var result = new ProjectMetadataAggregationPersistenceService().Apply(
            metadata,
            new ProjectMetadataAggregationPersistenceRequest(
                folder.Path,
                ProjectMetadataInitializationState.SubsequentSource));

        Assert.Same(metadata, result.CanonicalMetadata);
        Assert.Equal("User Titel", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(["Sprecher"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Origin);
        Assert.False(File.Exists(ProjectFolderLayout.GetProjectMetadataPath(folder.Path)));
    }

    [Fact]
    public void Apply_RespectsUserClearedButFillsFieldsRegardlessOfOutputPolicy()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SetValue<string>(ProjectMetadataField.Title, null, MetadataOrigin.User);
        SaveSnapshot(folder.Path, title: "Source Titel", narrator: "Sprecher");

        new ProjectMetadataAggregationPersistenceService().Apply(
            metadata,
            new ProjectMetadataAggregationPersistenceRequest(
                folder.Path,
                ProjectMetadataInitializationState.ProjectMetadataInitialized));

        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(["Sprecher"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
    }

    [Fact]
    public void Apply_DoesNotReplaceTheCallerOwnedCanonicalInstance()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        SaveSnapshot(folder.Path, title: "Snapshot Titel");

        var result = new ProjectMetadataAggregationPersistenceService().Apply(
            metadata,
            new ProjectMetadataAggregationPersistenceRequest(
                folder.Path,
                ProjectMetadataInitializationState.InitialSetComplete));

        Assert.Same(metadata, result.CanonicalMetadata);
        Assert.Equal("Snapshot Titel", result.Metadata.Title);
        Assert.False(File.Exists(ProjectFolderLayout.GetProjectMetadataPath(folder.Path)));
    }

    private static void SaveSnapshot(
        string projectFolder,
        string title = "",
        string author = "",
        string narrator = "",
        string genre = "",
        string sourcePath = "",
        IReadOnlyList<string>? genres = null)
    {
        new SourceMetadataSnapshotStore().Save(
            projectFolder,
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Title = title,
                    Authors = string.IsNullOrWhiteSpace(author) ? [] : [author],
                    Narrators = string.IsNullOrWhiteSpace(narrator) ? [] : [narrator],
                    Genres = genres ?? (string.IsNullOrWhiteSpace(genre) ? [] : [genre])
                },
                TechnicalInfo = new SourceTechnicalInfo
                {
                    SourcePath = sourcePath
                }
            });
    }
}
