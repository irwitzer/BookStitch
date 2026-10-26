using System.Reflection;
using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Models;

public sealed class ProjectMetadataStateTests
{
    [Fact]
    public void SharedState_RaisesChangesForCommonProjectMetadata()
    {
        var state = new ProjectMetadataState();
        var changed = new List<string>();
        state.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

        state.BookTitle = "Titel";
        state.Album = "Album";
        state.Author = "Autor";
        state.Narrator = "Sprecher";
        state.Genre = "Hoerbuch";
        state.CoverSourcePath = @"D:\Cover.png";
        state.ProcessedCoverPath = @"D:\Cover.jpg";
        state.CoverPreviewSource = @"D:\Cover.jpg";

        Assert.Contains(nameof(ProjectMetadataState.BookTitle), changed);
        Assert.Contains(nameof(ProjectMetadataState.Album), changed);
        Assert.Contains(nameof(ProjectMetadataState.Author), changed);
        Assert.Contains(nameof(ProjectMetadataState.Narrator), changed);
        Assert.Contains(nameof(ProjectMetadataState.Genre), changed);
        Assert.Contains(nameof(ProjectMetadataState.CoverSourcePath), changed);
        Assert.Contains(nameof(ProjectMetadataState.ProcessedCoverPath), changed);
        Assert.Contains(nameof(ProjectMetadataState.CoverPreviewSource), changed);
    }

    [Fact]
    public void Clear_ResetsSharedMetadataWithoutReplacingStateObject()
    {
        var state = new ProjectMetadataState
        {
            BookTitle = "Titel",
            Album = "Album",
            Author = "Autor",
            Narrator = "Sprecher",
            Genre = "Genre",
            CoverSourcePath = @"D:\Cover.png",
            ProcessedCoverPath = @"D:\Cover.jpg",
            CoverPreviewSource = @"D:\Cover.jpg"
        };
        var previous = state.CanonicalMetadata;

        state.Clear("Audiobook");

        Assert.NotSame(previous, state.CanonicalMetadata);
        Assert.Equal("", state.BookTitle);
        Assert.Equal("", state.Album);
        Assert.Equal("", state.Author);
        Assert.Equal("", state.Narrator);
        Assert.Equal("Audiobook", state.Genre);
        Assert.Equal("", state.CoverSourcePath);
        Assert.Equal("", state.ProcessedCoverPath);
        Assert.Equal("", state.CoverPreviewSource);
    }

    [Fact]
    public void Clear_DiscardsPreviousCanonicalSessionIncludingFieldsOutsideCompactUi()
    {
        var state = new ProjectMetadataState
        {
            BookTitle = "Altes Buch",
            Author = "Alte Autorin",
            Narrator = "Alte Sprecherin",
            Genre = "Hörbuch"
        };
        var previous = state.CanonicalMetadata;
        previous.SetValue(ProjectMetadataField.Description, "Alter Beschreibungstext", MetadataOrigin.User);
        previous.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages, ["de"], MetadataOrigin.User);

        state.Clear("Hörbuch");

        var next = state.CanonicalMetadata;
        Assert.NotSame(previous, next);
        Assert.Equal("", state.BookTitle);
        Assert.Equal("", state.Author);
        Assert.Equal("", state.Narrator);
        Assert.Equal("Hörbuch", state.Genre);
        Assert.Null(next.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Null(next.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Null(next.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
        Assert.Null(next.GetValue<string>(ProjectMetadataField.Description).Value);
        Assert.Null(next.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Languages).Value);
        Assert.Equal(new[] { "Hörbuch" }, next.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
    }

    [Fact]
    public void EmptyCanonicalSession_StartsAtRevisionZero_AndCompactEditsBecomeUserValues()
    {
        var state = new ProjectMetadataState();
        state.Clear("");

        var metadata = state.EnsureCanonicalMetadata(MetadataOrigin.None);

        Assert.Same(state.CanonicalMetadata, metadata);
        Assert.Equal(0, metadata.Revision);
        Assert.Empty(metadata.GetExplicitValues());
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.None, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);

        state.BookTitle = "Neues Hörbuch";
        state.Author = "Autorin";
        state.Narrator = "Sprecher";
        state.Genre = "Lesung";

        Assert.Equal("Neues Hörbuch", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
        Assert.Equal(new[] { "Autorin" }, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Origin);
        Assert.Equal(new[] { "Sprecher" }, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Origin);
        Assert.Equal(new[] { "Lesung" }, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Origin);
    }

    [Fact]
    public void SeriesProjection_PreservesMultipleEntryPairsOrderAndRefreshNotification()
    {
        var metadata = new ProjectMetadata();
        var state = new ProjectMetadataState();
        state.ApplyCanonicalMetadata(metadata);
        var changed = new List<string>();
        state.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        var expected = new[]
        {
            new SeriesEntry("Hauptreihe", "2"),
            new SeriesEntry("Nebenreihe", "A")
        };

        metadata.SetValue<IReadOnlyList<SeriesEntry>>(
            ProjectMetadataField.Series,
            expected,
            MetadataOrigin.SourceAggregation);

        Assert.Equal(expected, state.Series);
        Assert.Contains(nameof(ProjectMetadataState.Series), changed);

        state.Series =
        [
            new SeriesEntry("Benutzerreihe", "7"),
            new SeriesEntry("Zweite Reihe", "3")
        ];

        Assert.Equal(
            [new SeriesEntry("Benutzerreihe", "7"), new SeriesEntry("Zweite Reihe", "3")],
            metadata.GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series).Value);
        Assert.Equal(
            MetadataOrigin.User,
            metadata.GetValue<IReadOnlyList<SeriesEntry>>(ProjectMetadataField.Series).Origin);
    }

    [Fact]
    public void CoverProperties_UpdateCanonicalCoverTruth()
    {
        var state = new ProjectMetadataState();
        state.Clear("");

        state.CoverSourcePath = @"D:\Cover.png";
        state.ProcessedCoverPath = @"D:\Cover.jpg";

        Assert.Equal(@"D:\Cover.png", state.CanonicalMetadata.Cover.SourcePath);
        Assert.Equal(@"D:\Cover.jpg", state.CanonicalMetadata.Cover.ProcessedPath);
        Assert.Equal(MetadataOrigin.User, state.CanonicalMetadata.Cover.Origin);
    }

    [Fact]
    public void ApplyingCanonicalMetadata_ProjectsCanonicalCoverTruth()
    {
        var metadata = new ProjectMetadata();
        metadata.SetCover(@"D:\Source.png", @"D:\Processed.jpg", MetadataOrigin.LegacyMigrated);

        var state = new ProjectMetadataState();
        state.ApplyCanonicalMetadata(metadata);

        Assert.Equal(@"D:\Source.png", state.CoverSourcePath);
        Assert.Equal(@"D:\Processed.jpg", state.ProcessedCoverPath);
    }

    [Fact]
    public void CompactPrimaryEdits_PreserveCanonicalListTails()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            ["Autor 1", "Autor 2", "Autor 3"],
            MetadataOrigin.SourceAggregation);
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Narrators,
            ["Sprecher 1", "Sprecher 2"],
            MetadataOrigin.SourceAggregation);
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Genres,
            ["Genre 1", "Genre 2", "Genre 3"],
            MetadataOrigin.SourceAggregation);

        var state = new ProjectMetadataState();
        state.ApplyCanonicalMetadata(metadata);

        state.Author = "Neuer Autor";
        state.Narrator = "Neue Sprecherin";
        state.Genre = "Neues Genre";

        Assert.Equal(
            ["Neuer Autor", "Autor 2", "Autor 3"],
            metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
        Assert.Equal(
            ["Neue Sprecherin", "Sprecher 2"],
            metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators).Value);
        Assert.Equal(
            ["Neues Genre", "Genre 2", "Genre 3"],
            metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
    }

    [Fact]
    public void ClearingCompactPrimary_RemovesOnlyPrimaryAndKeepsRemainingListOrder()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Genres,
            ["Genre 1", "Genre 2", "Genre 3"],
            MetadataOrigin.User);

        var state = new ProjectMetadataState();
        state.ApplyCanonicalMetadata(metadata);

        state.Genre = "";

        Assert.Equal(
            ["Genre 2", "Genre 3"],
            metadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.Genres).Value);
    }

    [Fact]
    public void ApplyingCanonicalMetadata_PreservesOriginsAndOnlyLaterCompactEditsBecomeUserInput()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Quelle", MetadataOrigin.SourceAggregation);
        var state = new ProjectMetadataState();

        state.ApplyCanonicalMetadata(metadata);

        Assert.Equal("Quelle", state.BookTitle);
        Assert.Equal(MetadataOrigin.SourceAggregation,
            metadata.GetValue<string>(ProjectMetadataField.Title).Origin);

        state.BookTitle = "Bearbeitet";

        Assert.Equal("Bearbeitet", metadata.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(MetadataOrigin.User, metadata.GetValue<string>(ProjectMetadataField.Title).Origin);
    }

    [Fact]
    public void DirectCanonicalChanges_AreProjectedAndRaiseSharedStateNotifications()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Quelle", MetadataOrigin.SourceAggregation);
        var state = new ProjectMetadataState();
        state.ApplyCanonicalMetadata(metadata);
        var changed = new List<string>();
        state.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

        metadata.SetValue(ProjectMetadataField.Title, "Neue Quelle", MetadataOrigin.SourceAggregation);
        metadata.SetValue<IReadOnlyList<string>>(
            ProjectMetadataField.Authors,
            ["Ada", "Berta"],
            MetadataOrigin.SourceAggregation);

        Assert.Equal("Neue Quelle", state.BookTitle);
        Assert.Equal("Ada", state.Author);
        Assert.Contains(nameof(ProjectMetadataState.BookTitle), changed);
        Assert.Contains(nameof(ProjectMetadataState.Author), changed);
    }


    [Fact]
    public void WorkFields_ProjectCanonicalValuesAndRaiseSharedStateNotifications()
    {
        var metadata = new ProjectMetadata();
        var state = new ProjectMetadataState();
        state.ApplyCanonicalMetadata(metadata);
        var changed = new List<string>();
        state.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        metadata.SetValue(ProjectMetadataField.Subtitle, "Untertitel", MetadataOrigin.SourceAggregation);
        metadata.SetValue<AudioWorkType?>(ProjectMetadataField.AudioWorkType, AudioWorkType.AudioDrama, MetadataOrigin.SourceAggregation);
        metadata.SetValue<AbridgedStatus?>(ProjectMetadataField.AbridgedStatus, AbridgedStatus.Unabridged, MetadataOrigin.SourceAggregation);
        metadata.SetValue(ProjectMetadataField.Volume, "Band 7", MetadataOrigin.SourceAggregation);
        metadata.SetValue(ProjectMetadataField.Edition, "Sonderausgabe", MetadataOrigin.SourceAggregation);

        Assert.Equal("Untertitel", state.Subtitle);
        Assert.Equal(AudioWorkType.AudioDrama, state.WorkType);
        Assert.Equal(AbridgedStatus.Unabridged, state.AbridgedStatus);
        Assert.Equal("Band 7", state.Volume);
        Assert.Equal("Sonderausgabe", state.Edition);
        Assert.Contains(nameof(ProjectMetadataState.Subtitle), changed);
        Assert.Contains(nameof(ProjectMetadataState.WorkType), changed);
        Assert.Contains(nameof(ProjectMetadataState.AbridgedStatus), changed);
        Assert.Contains(nameof(ProjectMetadataState.Volume), changed);
        Assert.Contains(nameof(ProjectMetadataState.Edition), changed);
    }

    [Fact]
    public void CompactMetadataProperties_DoNotHaveMutableScalarShadowFields()
    {
        var fields = typeof(ProjectMetadataState)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.Name)
            .ToArray();

        Assert.DoesNotContain("_bookTitle", fields);
        Assert.DoesNotContain("_album", fields);
        Assert.DoesNotContain("_author", fields);
        Assert.DoesNotContain("_narrator", fields);
        Assert.DoesNotContain("_genre", fields);
        Assert.Contains("_canonicalMetadata", fields);
    }
    [Fact]
    public void ApplyCoverMetadata_SynchronizesPreviewImmediately()
    {
        var state = new ProjectMetadataState();

        state.ApplyCoverMetadata(@"D:\Source.png", @"D:\Processed.jpg", MetadataOrigin.User);
        Assert.Equal(@"D:\Processed.jpg", state.CoverPreviewSource);

        state.ApplyCoverMetadata(@"D:\Source2.png", "", MetadataOrigin.User);
        Assert.Equal(@"D:\Source2.png", state.CoverPreviewSource);

        state.ApplyCoverMetadata("", "", MetadataOrigin.User);
        Assert.Equal(string.Empty, state.CoverPreviewSource);
    }

    [Fact]
    public void ApplyCanonicalMetadata_SynchronizesCoverPreviewProjection()
    {
        var metadata = new ProjectMetadata();
        metadata.SetCover(@"D:\Source.png", @"D:\Processed.jpg", MetadataOrigin.SourceAggregation);
        var state = new ProjectMetadataState();

        state.ApplyCanonicalMetadata(metadata);

        Assert.Equal(@"D:\Processed.jpg", state.CoverPreviewSource);
    }

}
