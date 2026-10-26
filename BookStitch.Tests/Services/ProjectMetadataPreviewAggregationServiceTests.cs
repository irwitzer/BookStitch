using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectMetadataPreviewAggregationServiceTests
{
    [Fact]
    public void Apply_MergesAllPreviewListsInTrackOrderAndPreservesInFileOrder()
    {
        var state = new ProjectMetadataState();
        state.Clear("Default Genre");
        var service = new ProjectMetadataPreviewAggregationService();
        var tracks = new[]
        {
            new TrackInfo
            {
                Index = 1,
                FilePath = @"D:\Book\01.m4b",
                SourceMetadataFields = new SourceMetadataCanonicalFields
                {
                    Authors = ["Autor 1"],
                    Narrators = ["Sprecher 1"],
                    Genres = ["Genre 1"],
                    Keywords = ["K1"],
                    Languages = ["de"]
                }
            },
            new TrackInfo
            {
                Index = 2,
                FilePath = @"D:\Book\02.mp3",
                SourceMetadataFields = new SourceMetadataCanonicalFields
                {
                    Authors = ["Autor 2", "Autor 3"],
                    Narrators = ["Sprecher 2", "Sprecher 3"],
                    Genres = ["Genre 2", "Genre 3"],
                    Keywords = ["K2", "K3"],
                    Languages = ["en", "fr"]
                }
            },
            new TrackInfo
            {
                Index = 3,
                FilePath = @"D:\Book\03.m4a",
                SourceMetadataFields = new SourceMetadataCanonicalFields
                {
                    Authors = ["Autor 4"],
                    Narrators = ["Sprecher 4"],
                    Genres = ["Genre 4", "Genre 2"],
                    Keywords = ["K4", "K2"],
                    Languages = ["it", "de"]
                }
            }
        };

        var result = service.Apply(
            state,
            tracks,
            new ProjectMetadataInitializationOptions(ReplaceableGenreSeed: "Default Genre"));

        Assert.True(result.HasSourceMetadata);
        Assert.True(result.InitializationResult.Changed);
        var metadata = Assert.IsType<ProjectMetadata>(state.CanonicalMetadata);
        Assert.Equal(["Autor 1", "Autor 2", "Autor 3", "Autor 4"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(["Sprecher 1", "Sprecher 2", "Sprecher 3", "Sprecher 4"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
        Assert.Equal(["Genre 1", "Genre 2", "Genre 3", "Genre 4"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.Equal(["K1", "K2", "K3", "K4"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Keywords).Value);
        Assert.Equal(["de", "en", "fr", "it"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages).Value);
    }

    [Fact]
    public void Apply_WithoutCanonicalFields_UsesOnlyExistingInMemoryTrackEvidenceAndFolderFallbacks()
    {
        var state = new ProjectMetadataState();
        state.Clear("");
        var service = new ProjectMetadataPreviewAggregationService();
        var tracks = new[]
        {
            new TrackInfo
            {
                Index = 1,
                FilePath = @"D:\Author\Book\01.mp3",
                TagTitle = "Book",
                Artist = "Author"
            },
            new TrackInfo
            {
                Index = 2,
                FilePath = @"D:\Author\Book\02.mp3",
                TagTitle = "Book",
                Artist = "Author"
            }
        };

        var result = service.Apply(
            state,
            tracks,
            new ProjectMetadataInitializationOptions(
                BookFolderCandidate: "Book",
                ParentFolderCandidate: "Author"));

        Assert.False(result.HasSourceMetadata);
        Assert.True(result.InitializationResult.Changed);
        var metadata = Assert.IsType<ProjectMetadata>(state.CanonicalMetadata);
        Assert.Equal("Book", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal("Book", metadata.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Equal(["Author"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
    }

    [Fact]
    public void Apply_WithoutAnyTagEvidence_UsesFolderStructureWithoutReadingFilesAgain()
    {
        var state = new ProjectMetadataState();
        state.Clear("");
        var service = new ProjectMetadataPreviewAggregationService();
        var tracks = new[]
        {
            new TrackInfo
            {
                Index = 1,
                FilePath = @"D:\Stephen King\Billy Summers\001.mp3"
            },
            new TrackInfo
            {
                Index = 2,
                FilePath = @"D:\Stephen King\Billy Summers\002.mp3"
            }
        };

        var result = service.Apply(
            state,
            tracks,
            new ProjectMetadataInitializationOptions(
                BookFolderCandidate: "Billy Summers",
                ParentFolderCandidate: "Stephen King"));

        Assert.False(result.HasSourceMetadata);
        Assert.True(result.InitializationResult.Changed);
        var metadata = Assert.IsType<ProjectMetadata>(state.CanonicalMetadata);
        Assert.Equal("Billy Summers", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal("Billy Summers", metadata.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Equal(["Stephen King"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
    }

    [Fact]
    public void Apply_CanonicalSourceMetadataWinsOverLegacyTrackPresentationFields()
    {
        var state = new ProjectMetadataState();
        state.Clear("");
        var service = new ProjectMetadataPreviewAggregationService();
        var tracks = new[]
        {
            new TrackInfo
            {
                Index = 1,
                FilePath = @"D:\Book\01.mp3",
                TagTitle = "Legacy display title",
                Artist = "Legacy display artist",
                SourceMetadataFields = new SourceMetadataCanonicalFields
                {
                    Title = "Canonical title",
                    Authors = ["Canonical author"]
                }
            }
        };

        var result = service.Apply(
            state,
            tracks,
            new ProjectMetadataInitializationOptions());

        Assert.True(result.HasSourceMetadata);
        var metadata = Assert.IsType<ProjectMetadata>(state.CanonicalMetadata);
        Assert.Equal("Canonical title", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(["Canonical author"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
    }

    [Fact]
    public void Apply_UnlockedSourceWithoutGenreLeavesGenreEmpty()
    {
        var state = new ProjectMetadataState();
        state.Clear("");
        var service = new ProjectMetadataPreviewAggregationService();
        var tracks = new[]
        {
            new TrackInfo
            {
                Index = 1,
                FilePath = @"D:\Book\01.m4b",
                SourceMetadataFields = new SourceMetadataCanonicalFields
                {
                    Title = "Titel",
                    Genres = []
                }
            }
        };

        service.Apply(
            state,
            tracks,
            new ProjectMetadataInitializationOptions(LastUsedGenre: "Altes Genre"));

        var genres = Assert.IsType<ProjectMetadata>(state.CanonicalMetadata)
            .GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres);
        Assert.Null(genres.Value);
        Assert.Equal("", state.Genre);
    }

    [Fact]
    public void Apply_WithLockedGenrePreferenceCarriesEntirePreferredListIntoNewPreviewSession()
    {
        var state = new ProjectMetadataState();
        state.Clear("iBook Klein");
        var service = new ProjectMetadataPreviewAggregationService();
        var tracks = new[]
        {
            new TrackInfo
            {
                Index = 1,
                FilePath = @"D:\Book\01.m4b",
                SourceMetadataFields = new SourceMetadataCanonicalFields
                {
                    Genres = ["Source Genre"]
                }
            }
        };

        service.Apply(
            state,
            tracks,
            new ProjectMetadataInitializationOptions(
                ReplaceableGenreSeed: "iBook Klein",
                IsGenrePreferenceFixed: true,
                PreferredGenres: ["iBook Klein", "iBook Groß", "iBook Gen3"]));

        var genres = Assert.IsType<ProjectMetadata>(state.CanonicalMetadata)
            .GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres);
        Assert.Equal(["iBook Klein", "iBook Groß", "iBook Gen3"], genres.Value);
        Assert.Equal(MetadataOrigin.GlobalPreference, genres.Origin);
    }
    [Fact]
    public void CreateSourceSnapshots_PreservesLiveCanonicalTitleAndAlbumForCandidateDiscovery()
    {
        var service = new ProjectMetadataPreviewAggregationService();
        var tracks = new[]
        {
            new TrackInfo
            {
                SourceId = Guid.NewGuid(),
                FilePath = @"D:\Book\01.m4b",
                SourceMetadataFields = new SourceMetadataCanonicalFields
                {
                    Title = "Die Waechter",
                    Album = "Die Waechter"
                }
            },
            new TrackInfo
            {
                SourceId = Guid.NewGuid(),
                FilePath = @"D:\Book\02.m4b",
                SourceMetadataFields = new SourceMetadataCanonicalFields
                {
                    Title = "Die Waechter",
                    Album = "Die Waechter"
                }
            }
        };

        var snapshots = service.CreateSourceSnapshots(tracks);
        var alternatives = new SourceMetadataAlternativeService();
        var title = Assert.Single(alternatives.BuildScalarTextCandidates(snapshots, ProjectMetadataField.Title));
        var album = Assert.Single(alternatives.BuildScalarTextCandidates(snapshots, ProjectMetadataField.Album));

        Assert.Equal("Die Waechter", title.DisplayValue);
        Assert.Equal(2, title.SourceCount);
        Assert.Equal("Die Waechter", album.DisplayValue);
        Assert.Equal(2, album.SourceCount);
    }

}
