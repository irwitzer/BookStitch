using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectMetadataInitializationServiceTests
{
    private readonly ProjectMetadataInitializationService _service = new();

    [Fact]
    public void Apply_SuppressedYearDoesNotReturnFromLaterSourcesButOtherYearCanFillEmptyField()
    {
        var metadata = new ProjectMetadata();
        metadata.SuppressValue(ProjectMetadataField.Year, "2026");

        _service.Apply(metadata, [Snapshot(year: 2026)], ProjectMetadataInitializationState.SubsequentSource);
        Assert.Null(metadata.GetValue<int?>(ProjectMetadataField.Year).Value);

        _service.Apply(metadata, [Snapshot(year: 2027)], ProjectMetadataInitializationState.SubsequentSource);
        Assert.Equal(2027, metadata.GetValue<int?>(ProjectMetadataField.Year).Value);
    }

    [Theory]
    [InlineData(ProjectMetadataInitializationState.InitialSetOpen)]
    [InlineData(ProjectMetadataInitializationState.InitialSetComplete)]
    [InlineData(ProjectMetadataInitializationState.ProjectMetadataInitialized)]
    [InlineData(ProjectMetadataInitializationState.SubsequentSource)]
    public void Apply_LockedGlobalLanguageAndWorkTypeProtectEveryInitializationPhase(ProjectMetadataInitializationState phase)
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages, ["en"], MetadataOrigin.SourceAggregation);
        metadata.SetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType, AudioWorkType.AudioDrama, MetadataOrigin.SourceAggregation);
        var options = new ProjectMetadataInitializationOptions(
            PreferredLanguages: ["de"], IsLanguagePreferenceFixed: true,
            PreferredAudioWorkType: AudioWorkType.Audiobook, IsAudioWorkTypePreferenceFixed: true);

        _service.Apply(metadata, [], phase, options);
        _service.Apply(metadata, [], phase, options);

        Assert.Equal(new[] { "de" }, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages).Value);
        Assert.Equal(AudioWorkType.Audiobook, metadata.GetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType).Value);
        Assert.Equal(MetadataOrigin.GlobalPreference, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages).Origin);
        Assert.Equal(MetadataOrigin.GlobalPreference, metadata.GetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType).Origin);
    }

    [Fact]
    public void Apply_PreservesDirectSourceYearAsCanonicalYear()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot(year: 1997), Snapshot(year: 1997), Snapshot(year: 2001)],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.Equal(1997, metadata.GetValue<int?>(ProjectMetadataField.Year).Value);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<int?>(ProjectMetadataField.Year).Origin);
    }

    [Fact]
    public void Apply_WhenInitialSetIsOpen_DoesNotInitializeMetadata()
    {
        var metadata = new ProjectMetadata();

        var result = _service.Apply(
            metadata,
            [Snapshot(title: "Disc 2 Title")],
            ProjectMetadataInitializationState.InitialSetOpen);

        Assert.False(result.Changed);
        Assert.Equal(MetadataOrigin.None, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
    }

    [Fact]
    public void Apply_WhenSourceTitlesAreConflictingTrackLevelValues_DoesNotInventAProjectTitle()
    {
        var metadata = new ProjectMetadata();

        var result = _service.Apply(
            metadata,
            [
                Snapshot(title: "001 Alpha"),
                Snapshot(title: "002 Beta")
            ],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.False(result.Changed);
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.None, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.DoesNotContain(result.Diagnostics, item =>
            item.Code == MetadataAggregationDiagnosticCode.AmbiguousMajority &&
            item.Field == ProjectMetadataField.Title);
    }

    [Fact]
    public void Apply_NumberedSourceTitlesUseSharedProjectTitleGroupingBeforeAlbumFallback()
    {
        var metadata = new ProjectMetadata();

        var result = _service.Apply(
            metadata,
            [
                Snapshot(title: "001 Billy Summers", album: "Billy Summers"),
                Snapshot(title: "002 Billy Summers", album: "Billy Summers"),
                Snapshot(title: "003 Billy Summers", album: "Billy Summers")
            ],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.Equal("Billy Summers", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal("Billy Summers", metadata.GetValue<string>(ProjectMetadataField.Album).Value);

        var title = Assert.Single(result.FieldDecisions, item => item.Field == ProjectMetadataField.Title);
        Assert.Equal(MetadataDecisionEvidenceType.DirectTitleAggregation, title.Winner?.EvidenceType);
        Assert.Equal(3, title.Winner?.SupportCount);
        Assert.Equal(1d, title.Winner?.SupportRatio);
    }

    [Fact]
    public void Apply_AlbumFallback_ReportsWinnerSupportAndRejectedDirectTitleEvidence()
    {
        var result = _service.Apply(
            new ProjectMetadata(),
            [
                Snapshot(title: "001 Eckhart Omama", album: "Omama"),
                Snapshot(title: "002 Anderer Track", album: "Omama"),
                Snapshot(title: "003 Dritter Track", album: "Omama")
            ],
            ProjectMetadataInitializationState.InitialSetComplete);

        var title = Assert.Single(result.FieldDecisions, item => item.Field == ProjectMetadataField.Title);
        Assert.Equal("Omama", title.CanonicalValue);
        Assert.Equal(MetadataOrigin.SourceAggregation, title.Origin);
        Assert.Equal(MetadataDecisionEvidenceType.AlbumFallback, title.Winner?.EvidenceType);
        Assert.Equal(3, title.Winner?.SupportCount);
        Assert.Equal(1d, title.Winner?.SupportRatio);
        Assert.Equal(MetadataDecisionEvidenceType.DirectTitleAggregation, title.RunnerUp?.EvidenceType);
        Assert.Contains("Zwei-Drittel", title.RunnerUp?.RejectedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WhenTrackTitlesAndAlbumsBothConflict_LeavesProjectTitleUnset()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [
                Snapshot(title: "001 Alpha", album: "Album A"),
                Snapshot(title: "002 Beta", album: "Album B")
            ],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
    }

    [Fact]
    public void Apply_WhenProjectTitleHasClearTwoThirdsMajority_UsesRepeatedTitleEvidence()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [
                Snapshot(title: "Billy Summers"),
                Snapshot(title: "Billy Summers"),
                Snapshot(title: "001 Billy Summers")
            ],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.Equal("Billy Summers", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
    }

    [Fact]
    public void Apply_NormalizesWhitespaceCaseAndTypographyButKeepsDiacriticsDistinct()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [
                Snapshot(title: " Der  lange - Weg ", authors: ["Müller"]),
                Snapshot(title: "der lange – weg", authors: ["Muller"]),
                Snapshot(title: "DER LANGE - WEG", authors: ["Müller"])
            ],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.Equal("Der lange - Weg", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(["Müller", "Muller"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
    }

    [Fact]
    public void Apply_MergesMultiValuesInSourceOrderAndKeepsFirstOccurrence()
    {
        var metadata = new ProjectMetadata();

        var result = _service.Apply(
            metadata,
            [
                Snapshot(authors: ["A", "B"], narrators: ["N1"], genres: ["G1"]),
                Snapshot(authors: ["B", "C"], narrators: ["N2", "N3"], genres: ["G2", "G3"]),
                Snapshot(authors: ["D"], narrators: ["N1", "N4"], genres: ["G4"])
            ],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.Equal(["A", "B", "C", "D"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(["N1", "N2", "N3", "N4"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
        Assert.Equal(["G1", "G2", "G3", "G4"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.DoesNotContain(result.Diagnostics, item =>
            item.Code == MetadataAggregationDiagnosticCode.AmbiguousMajority &&
            item.Field is ProjectMetadataField.Authors or ProjectMetadataField.Narrators or ProjectMetadataField.Genres);
    }

    [Fact]
    public void Apply_MergesEveryStringListFieldWithoutReorderingOrRepeatingValues()
    {
        var metadata = new ProjectMetadata();
        var first = new SourceMetadataSnapshot
        {
            SourceId = Guid.NewGuid(),
            CanonicalFields = new SourceMetadataCanonicalFields
            {
                Keywords = ["K1", "K2"],
                Authors = ["A1", "A2"],
                Narrators = ["N1"],
                Translators = ["T1"],
                Composers = ["Comp1"],
                Performers = ["P1"],
                Contributors = ["C1"],
                Hosts = ["H1"],
                Guests = ["Gst1"],
                Speakers = ["Sp1"],
                Languages = ["de", "en"],
                OriginalLanguages = ["fr"],
                TargetLanguages = ["de"],
                Genres = ["Genre 1"],
                Categories = ["Cat1"],
                CatalogNumbers = ["CN1"],
                SortAuthors = ["SA1"],
                SortNarrators = ["SN1"]
            }
        };
        var second = new SourceMetadataSnapshot
        {
            SourceId = Guid.NewGuid(),
            CanonicalFields = new SourceMetadataCanonicalFields
            {
                Keywords = ["K2", "K3"],
                Authors = ["A2", "A3"],
                Narrators = ["N2"],
                Translators = ["T2"],
                Composers = ["Comp2"],
                Performers = ["P2"],
                Contributors = ["C2"],
                Hosts = ["H2"],
                Guests = ["Gst2"],
                Speakers = ["Sp2"],
                Languages = ["en", "fr"],
                OriginalLanguages = ["it"],
                TargetLanguages = ["en"],
                Genres = ["Genre 2", "Genre 3"],
                Categories = ["Cat2"],
                CatalogNumbers = ["CN2"],
                SortAuthors = ["SA2"],
                SortNarrators = ["SN2"]
            }
        };

        _service.Apply(
            metadata,
            [first, second],
            ProjectMetadataInitializationState.InitialSetComplete);

        AssertList(metadata, ProjectMetadataField.Keywords, ["K1", "K2", "K3"]);
        AssertList(metadata, ProjectMetadataField.Authors, ["A1", "A2", "A3"]);
        AssertList(metadata, ProjectMetadataField.Narrators, ["N1", "N2"]);
        AssertList(metadata, ProjectMetadataField.Translators, ["T1", "T2"]);
        AssertList(metadata, ProjectMetadataField.Composers, ["Comp1", "Comp2"]);
        Assert.DoesNotContain(ProjectMetadataField.Performers, metadata.GetExplicitValues().Keys);
        Assert.DoesNotContain(ProjectMetadataField.Contributors, metadata.GetExplicitValues().Keys);
        Assert.DoesNotContain(ProjectMetadataField.Hosts, metadata.GetExplicitValues().Keys);
        Assert.DoesNotContain(ProjectMetadataField.Guests, metadata.GetExplicitValues().Keys);
        Assert.DoesNotContain(ProjectMetadataField.Speakers, metadata.GetExplicitValues().Keys);
        AssertList(metadata, ProjectMetadataField.Languages, ["de", "en", "fr"]);
        AssertList(metadata, ProjectMetadataField.OriginalLanguages, ["fr", "it"]);
        AssertList(metadata, ProjectMetadataField.TargetLanguages, ["de", "en"]);
        AssertList(metadata, ProjectMetadataField.Genres, ["Genre 1", "Genre 2", "Genre 3"]);
        AssertList(metadata, ProjectMetadataField.Categories, ["Cat1", "Cat2"]);
        AssertList(metadata, ProjectMetadataField.CatalogNumbers, ["CN1", "CN2"]);
        AssertList(metadata, ProjectMetadataField.SortAuthors, ["SA1", "SA2"]);
        AssertList(metadata, ProjectMetadataField.SortNarrators, ["SN1", "SN2"]);
    }

    [Fact]
    public void Apply_UsesLockedGlobalTrackPreferencesAsCanonicalValues()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                PreferredOutputTrackNumber: 7,
                IsOutputTrackNumberPreferenceFixed: true,
                PreferredOutputTrackTotal: 12,
                IsOutputTrackTotalPreferenceFixed: true));

        var number = metadata.GetValue<int?>(ProjectMetadataField.OutputTrackNumber);
        var total = metadata.GetValue<int?>(ProjectMetadataField.OutputTrackTotal);

        Assert.Equal(7, number.Value);
        Assert.Equal(MetadataOrigin.GlobalPreference, number.Origin);
        Assert.Equal(12, total.Value);
        Assert.Equal(MetadataOrigin.GlobalPreference, total.Origin);
    }

    [Fact]
    public void Apply_PreservesOrderedSeriesAcrossSourcesWithoutDuplicates()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [
                Snapshot(series: [new SeriesEntry("Reihe A", "1"), new SeriesEntry("Reihe B", "2")]),
                Snapshot(series: [new SeriesEntry("Reihe A", "1"), new SeriesEntry("Reihe C", "3")])
            ],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.Equal(
            [
                new SeriesEntry("Reihe A", "1"),
                new SeriesEntry("Reihe B", "2"),
                new SeriesEntry("Reihe C", "3")
            ],
            metadata.GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series).Value);
    }

    [Fact]
    public void Apply_AfterInitialization_FillsOnlyNoneNullGaps()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "User Title", MetadataOrigin.User);

        _service.Apply(
            metadata,
            [Snapshot(title: "Source Title", narrators: ["Sprecherin"])],
            ProjectMetadataInitializationState.ProjectMetadataInitialized);

        Assert.Equal("User Title", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(["Sprecherin"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
    }

    [Theory]
    [InlineData(ProjectMetadataInitializationState.ProjectMetadataInitialized)]
    [InlineData(ProjectMetadataInitializationState.SubsequentSource)]
    public void Apply_AfterInitialSet_PreservesExistingSourceAggregatedScalarListAndSeriesValues(
        ProjectMetadataInitializationState state)
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Initial Title", MetadataOrigin.SourceAggregation);
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            ["A1"],
            MetadataOrigin.SourceAggregation);
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Genres,
            ["G1"],
            MetadataOrigin.SourceAggregation);
        metadata.SetValue<IReadOnlyList<SeriesEntry>>(
            ProjectMetadataField.Series,
            [new SeriesEntry("Reihe A", "1")],
            MetadataOrigin.SourceAggregation);

        var result = _service.Apply(
            metadata,
            [
                Snapshot(
                    title: "Later Title",
                    authors: ["A2"],
                    genres: ["G2"],
                    series: [new SeriesEntry("Reihe B", "2")])
            ],
            state);

        Assert.False(result.Changed);
        Assert.Equal("Initial Title", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(["A1"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(["G1"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.Equal(
            [new SeriesEntry("Reihe A", "1")],
            metadata.GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series).Value);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Origin);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series).Origin);
    }

    [Fact]
    public void Apply_AfterInitialSet_FillsOnlyNoneNullGapsAndPreservesExplicitNullOrigins()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<string>(ProjectMetadataField.Title, null, MetadataOrigin.SourceAggregation);

        var result = _service.Apply(
            metadata,
            [Snapshot(title: "Later Title", narrators: ["Sprecherin"])],
            ProjectMetadataInitializationState.SubsequentSource);

        Assert.True(result.Changed);
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(["Sprecherin"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Origin);
    }

    [Fact]
    public void Apply_AfterInitialization_RespectsUserClearedAndDoesNotConsultOutputPolicy()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<string>(ProjectMetadataField.Title, null, MetadataOrigin.User);

        _service.Apply(
            metadata,
            [Snapshot(title: "Source Title", narrators: ["Sprecherin"])],
            ProjectMetadataInitializationState.SubsequentSource);

        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(["Sprecherin"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
    }

    [Fact]
    public void Apply_CanFillFromExistingSnapshotsWithoutPolicyState()
    {
        var metadata = new ProjectMetadata();
        _service.Apply(
            metadata,
            [Snapshot(title: "Source Title")],
            ProjectMetadataInitializationState.ProjectMetadataInitialized);

        Assert.Equal("Source Title", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
    }

    [Fact]
    public void Apply_DoesNotInventGenreWhenSourcesHaveNoGenre()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot(audioWorkType: AudioWorkType.AudioDrama)],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(LastUsedGenre: "Letztes Genre"));

        Assert.Null(metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.Equal(AudioWorkType.AudioDrama, metadata.GetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType).Value);
    }


    [Fact]
    public void Apply_InitialGenreAggregation_ReplacesOnlyTheAutomaticPreviewSeed()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Genres,
            ["Audiobook"],
            MetadataOrigin.User);

        _service.Apply(
            metadata,
            [Snapshot(genres: ["Krimi", "Thriller"])],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                LastUsedGenre: "Audiobook",
                ReplaceableGenreSeed: "Audiobook"));

        Assert.Equal(["Krimi", "Thriller"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.Equal(MetadataOrigin.SourceAggregation, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Origin);

        metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Genres,
            ["Eigene Wahl"],
            MetadataOrigin.User);

        _service.Apply(
            metadata,
            [Snapshot(genres: ["Krimi"])],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                LastUsedGenre: "Audiobook",
                ReplaceableGenreSeed: "Audiobook"));

        Assert.Equal(["Eigene Wahl"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Origin);

        metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Genres,
            ["Audiobook"],
            MetadataOrigin.User);

        _service.Apply(
            metadata,
            [Snapshot(genres: ["Krimi"])],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                LastUsedGenre: "Audiobook",
                ReplaceableGenreSeed: "Audiobook",
                PreferredGenres: ["Fixiert"],
                IsGenrePreferenceFixed: true));

        Assert.Equal(["Fixiert"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.Equal(MetadataOrigin.GlobalPreference, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Origin);
    }

    [Fact]
    public void Apply_FixedGenrePreferenceWinsOverSourceAndAlsoAppliesWhenSourcesAreEmpty()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot(genres: ["Quelle"]), Snapshot(genres: ["Quelle"]), Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                LastUsedGenre: "Zuletzt",
                PreferredGenre: "Fixiert",
                IsGenrePreferenceFixed: true,
                AudioWorkTypeFallback: AudioWorkType.AudioDrama));

        Assert.Equal(["Fixiert"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);

        metadata = new ProjectMetadata();
        _service.Apply(
            metadata,
            [Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                LastUsedGenre: "Zuletzt",
                PreferredGenre: "Fixiert",
                IsGenrePreferenceFixed: true,
                AudioWorkTypeFallback: AudioWorkType.AudioDrama));

        Assert.Equal(["Fixiert"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
    }

    [Fact]
    public void Apply_FixedGenrePreferencePreservesEntirePreferredListWhenSourcesHaveNoGenre()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                PreferredGenres: ["iBook Klein", "iBook Groß", "iBook Gen3"],
                IsGenrePreferenceFixed: true));

        var genres = metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres);
        Assert.Equal(["iBook Klein", "iBook Groß", "iBook Gen3"], genres.Value);
        Assert.Equal(MetadataOrigin.GlobalPreference, genres.Origin);
    }

    [Fact]
    public void Apply_UsesLocalFolderFallbacksOnlyWhenTagEvidenceIsMissing()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                BookFolderCandidate: "  Mein_Buch ",
                ParentFolderCandidate: " Autorin ",
                SingleFileNameCandidate: "Dateititel",
                IsSingleFile: true));

        Assert.Equal("Mein Buch", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal("Mein Buch", metadata.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Equal(["Autorin"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(MetadataOrigin.FolderStructure, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(MetadataOrigin.FolderStructure, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Origin);

        var title = Assert.Single(
            _service.Apply(
                new ProjectMetadata(),
                [Snapshot()],
                ProjectMetadataInitializationState.InitialSetComplete,
                new ProjectMetadataInitializationOptions(BookFolderCandidate: "Mein Buch")).FieldDecisions,
            item => item.Field == ProjectMetadataField.Title);
        Assert.Equal(MetadataDecisionEvidenceType.FolderStructure, title.Winner?.EvidenceType);
    }

    [Fact]
    public void Apply_GlobalGenreAndLanguageLocksWinWhileSourceEvidenceRemainsUntouched()
    {
        var metadata = new ProjectMetadata();
        var snapshot = Snapshot(genres: ["Source Genre"], languages: ["fr"]);

        _service.Apply(
            metadata,
            [snapshot],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                PreferredGenres: ["Krimi", "Hörbuch"],
                IsGenrePreferenceFixed: true,
                PreferredLanguages: ["de-CH"],
                IsLanguagePreferenceFixed: true));

        Assert.Equal(["Krimi", "Hörbuch"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.Equal(MetadataOrigin.GlobalPreference, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Origin);
        Assert.Equal(["de-CH"], metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages).Value);
        Assert.Equal(MetadataOrigin.GlobalPreference, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages).Origin);
        Assert.Equal(["Source Genre"], snapshot.CanonicalFields.Genres);
        Assert.Equal(["fr"], snapshot.CanonicalFields.Languages);
    }

    [Fact]
    public void Apply_UsesBookFolderFallbackForOnlyTheMissingTitleOrAlbumField()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot(title: "Tag Titel")],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(BookFolderCandidate: "Album Ordner"));

        Assert.Equal("Tag Titel", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal("Album Ordner", metadata.GetValue<string>(ProjectMetadataField.Album).Value);
    }

    [Fact]
    public void Apply_DoesNotUseTechnicalParentFolderAsAuthorFallback()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                BookFolderCandidate: "CD 01",
                ParentFolderCandidate: "Disc 2",
                SingleFileNameCandidate: "Track 01",
                IsSingleFile: true));

        Assert.Null(metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
    }

    [Theory]
    [InlineData("Kapitola 1")]
    [InlineData("Rozdział 1")]
    [InlineData("Глава 1")]
    [InlineData("Κεφάλαιο 1")]
    [InlineData("第1章")]
    [InlineData("第2話")]
    public void Apply_DoesNotUseInternationalTechnicalFoldersAsMetadataFallbacks(string technicalName)
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                BookFolderCandidate: technicalName,
                ParentFolderCandidate: technicalName,
                SingleFileNameCandidate: technicalName,
                IsSingleFile: true));

        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Null(metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
    }

    [Theory]
    [InlineData("Cesta domů")]
    [InlineData("Podróż przez noc")]
    [InlineData("Путешествие домой")]
    [InlineData("Το ταξίδι")]
    [InlineData("第1章标题之谜")]
    public void Apply_KeepsOrdinaryInternationalFolderNamesAsMetadataEvidence(string folderName)
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(BookFolderCandidate: folderName));

        Assert.Equal(folderName, metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(folderName, metadata.GetValue<string>(ProjectMetadataField.Album).Value);
    }

    [Fact]
    public void Apply_Mp3DiscPolicy_DoesNotUseDriveRootOrFolderFallbacks()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                BookFolderCandidate: @"H:\",
                ParentFolderCandidate: @"H:\",
                AllowFolderStructureFallbacks: false,
                AllowSingleFileNameFallback: false));

        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Null(metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
    }

    [Fact]
    public void Apply_Mp3DiscPolicy_UsesStrongSourceFilenameDominanceForDerivedTitleOnly()
    {
        var metadata = new ProjectMetadata();

        var result = _service.Apply(
            metadata,
            [Snapshot(), Snapshot(), Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                SourceFileNameCandidates:
                [
                    "0101John Grisham Bestechung.mp3",
                    "0102John Grisham Bestechung.mp3",
                    "0103John Grisham Bestechung.mp3"
                ],
                AllowFilenameTitleFallback: true,
                AllowFolderStructureFallbacks: false,
                AllowSingleFileNameFallback: false));

        Assert.Equal("John Grisham Bestechung", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.Derived, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal("John Grisham Bestechung", metadata.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Equal(MetadataOrigin.Derived, metadata.GetValue<string>(ProjectMetadataField.Album).Origin);
        Assert.Null(metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);

        var title = Assert.Single(result.FieldDecisions, item => item.Field == ProjectMetadataField.Title);
        Assert.Equal(MetadataDecisionEvidenceType.FilenameAggregationFallback, title.Winner?.EvidenceType);
        Assert.Equal(3, title.Winner?.SupportCount);
        Assert.Equal(1d, title.Winner?.SupportRatio);
    }

    [Fact]
    public void Apply_Mp3DiscPolicy_RejectsIndividualFilenamesAndKeepsTitleEmpty()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot(), Snapshot(), Snapshot(), Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                SourceFileNameCandidates:
                [
                    "Prolog.mp3",
                    "Ankunft.mp3",
                    "Der Prozess.mp3",
                    "Epilog.mp3"
                ],
                AllowFilenameTitleFallback: true,
                AllowFolderStructureFallbacks: false,
                AllowSingleFileNameFallback: false));

        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Album).Value);
    }

    [Fact]
    public void Apply_LocalFolderPolicy_UsesFilenameAggregationBeforeFolderStructure()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot(), Snapshot(), Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                BookFolderCandidate: "Meine Hörbücher 2026",
                SourceFileNameCandidates:
                [
                    "001 John Grisham Bestechung.mp3",
                    "002 John Grisham Bestechung.mp3",
                    "003 John Grisham Bestechung.mp3"
                ],
                AllowFilenameTitleFallback: true));

        Assert.Equal("John Grisham Bestechung", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.Derived, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
    }

    [Fact]
    public void Apply_FilenameFallback_CollapsesZeroPaddedSequenceAndWinsBeforeFolderFallback()
    {
        var metadata = new ProjectMetadata();
        var fileNames = Enumerable.Range(1, 16)
            .Select(index => $"{index:00} S Ánti Gundula.mp3")
            .ToArray();

        var result = _service.Apply(
            metadata,
            Enumerable.Range(1, 16).Select(_ => Snapshot()).ToArray(),
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                BookFolderCandidate: "Falscher Ordnerfallback",
                SourceFileNameCandidates: fileNames,
                AllowFilenameTitleFallback: true,
                AllowFolderStructureFallbacks: true));

        Assert.Equal("S Ánti Gundula", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.Derived, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);

        var title = Assert.Single(result.FieldDecisions, item => item.Field == ProjectMetadataField.Title);
        Assert.Equal(MetadataDecisionEvidenceType.FilenameAggregationFallback, title.Winner?.EvidenceType);
        Assert.Equal(16, title.Winner?.SupportCount);
        Assert.Equal(1d, title.Winner?.SupportRatio);
    }

    [Fact]
    public void Apply_FilenameFallback_CollapsesNonPaddedOneToSixteenSequence()
    {
        var metadata = new ProjectMetadata();
        var fileNames = Enumerable.Range(1, 16)
            .Select(index => $"{index} S Ánti Gundula.mp3")
            .ToArray();

        _service.Apply(
            metadata,
            Enumerable.Range(1, 16).Select(_ => Snapshot()).ToArray(),
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                SourceFileNameCandidates: fileNames,
                AllowFilenameTitleFallback: true,
                AllowFolderStructureFallbacks: false));

        Assert.Equal("S Ánti Gundula", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.Derived, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
    }

    [Fact]
    public void Apply_FilenameFallback_DoesNotAggressivelyStripNonSequentialLeadingNumbers()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot(), Snapshot(), Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                SourceFileNameCandidates:
                [
                    "1 S Ánti Gundula.mp3",
                    "5 S Ánti Gundula.mp3",
                    "12 S Ánti Gundula.mp3"
                ],
                AllowFilenameTitleFallback: true,
                AllowFolderStructureFallbacks: false));

        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
    }

    [Fact]
    public void Apply_AudioDiscPolicy_DoesNotUseFolderOrTechnicalTrackFilenameFallbacks()
    {
        var metadata = new ProjectMetadata();

        _service.Apply(
            metadata,
            [Snapshot(), Snapshot(), Snapshot()],
            ProjectMetadataInitializationState.InitialSetComplete,
            new ProjectMetadataInitializationOptions(
                BookFolderCandidate: @"D:\",
                SourceFileNameCandidates:
                [
                    "Track01.wav",
                    "Track02.wav",
                    "Track03.wav"
                ],
                AllowFilenameTitleFallback: false,
                AllowFolderStructureFallbacks: false,
                AllowSingleFileNameFallback: false));

        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Album).Value);
    }

    [Fact]
    public void Apply_ReportsStrongConflictOnlyForBatchEvidence()
    {
        var result = _service.Apply(
            new ProjectMetadata(),
            [
                Snapshot(album: "Werk A", barcode: "111", authors: ["Autor A"]),
                Snapshot(album: "Werk B", barcode: "222", authors: ["Autor B"])
            ],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.Contains(result.Diagnostics, item => item.Code == MetadataAggregationDiagnosticCode.StrongIdentityConflict);

        result = _service.Apply(
            new ProjectMetadata(),
            [
                Snapshot(album: "Werk A", barcode: "111"),
                Snapshot(album: "Werk A", barcode: "222")
            ],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.DoesNotContain(result.Diagnostics, item => item.Code == MetadataAggregationDiagnosticCode.StrongIdentityConflict);
    }

    [Fact]
    public void Apply_GenresHonorsProjectSuppressionWithoutRemovingOtherCandidates()
    {
        var metadata = new ProjectMetadata();
        metadata.SuppressValue(ProjectMetadataField.Genres, "Krimi");

        _service.Apply(
            metadata,
            [Snapshot(genres: ["Krimi", "Fantasy"]), Snapshot(genres: ["Krimi", "Biografie"])],
            ProjectMetadataInitializationState.InitialSetComplete);

        Assert.Equal(
            ["Fantasy", "Biografie"],
            metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
    }

    private static void AssertList(
        ProjectMetadata metadata,
        ProjectMetadataField field,
        IReadOnlyList<string> expected) =>
        Assert.Equal(expected, metadata.GetValue<IReadOnlyList<string>>(field).Value);

    private static SourceMetadataSnapshot Snapshot(
        string title = "",
        string subtitle = "",
        string album = "",
        IReadOnlyList<string>? authors = null,
        IReadOnlyList<string>? narrators = null,
        IReadOnlyList<string>? genres = null,
        IReadOnlyList<string>? languages = null,
        IReadOnlyList<SeriesEntry>? series = null,
        string isbn = "",
        string barcode = "",
        string asin = "",
        AudioWorkType? audioWorkType = null,
        int? year = null) =>
        new()
        {
            SourceId = Guid.NewGuid(),
            CanonicalFields = new SourceMetadataCanonicalFields
            {
                Title = title,
                Subtitle = subtitle,
                Album = album,
                Authors = authors ?? [],
                Narrators = narrators ?? [],
                Genres = genres ?? [],
                Languages = languages ?? [],
                Series = series ?? [],
                ISBN = isbn,
                Barcode = barcode,
                ASIN = asin,
                AudioWorkType = audioWorkType,
                Year = year
            }
        };
}
