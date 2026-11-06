using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using TagLib;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class PreReleaseMetadataContractTests
{
    public static IEnumerable<object[]> ExternalListFields => new[]
    {
        "Authors", "Narrators", "Translators", "Composers", "SortAuthors", "SortNarrators", "SortComposers",
        "Genres", "Categories", "Keywords", "Languages", "OriginalLanguages", "TargetLanguages"
    }.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ExternalListFields))]
    public void External_lists_split_once_without_changing_evidence_or_saved_values(string name)
    {
        var original = new OriginalTagMetadata();
        var values = new[] { "A;B / C", "D/E", "A" };
        typeof(OriginalTagMetadata).GetProperty(name)!.SetValue(original, values);
        var canonical = SourceMetadataCaptureService.BuildCanonicalFields(original);
        Assert.Equal(new[] { "A", "B", "C", "D/E" },
            (IReadOnlyList<string>)typeof(SourceMetadataCanonicalFields).GetProperty(name)!.GetValue(canonical)!);
        Assert.Same(values, typeof(OriginalTagMetadata).GetProperty(name)!.GetValue(original));
    }

    [Theory]
    [InlineData("A / B", "A|B")]
    [InlineData("A /B", "A|B")]
    [InlineData("A/ B", "A|B")]
    [InlineData("A\u2003/\u00a0B", "A|B")]
    [InlineData("A\t/B", "A|B")]
    [InlineData("A/B", "A/B")]
    [InlineData("/ A", "/ A")]
    [InlineData("A /", "A /")]
    [InlineData("A;; B / / C; ;D/ E", "A|B|C|D|E")]
    [InlineData("A,B & C + D\\E", "A,B & C + D\\E")]
    public void External_separator_boundaries_are_explicit(string text, string expected)
    {
        Assert.Equal(expected.Split('|'), ExternalMetadataListParser.Parse([text], allowSlash: true));
        Assert.Equal(new[] { text }, new OriginalTagMetadata { Authors = [text] }.Authors);
    }

    [Fact]
    public void Catalog_numbers_and_manual_disc_values_keep_slashes()
    {
        var original = new OriginalTagMetadata { CatalogNumbers = ["CAT / 1;CAT/2"], Authors = ["A / B"] };
        var canonical = SourceMetadataCaptureService.BuildCanonicalFields(original);
        Assert.Equal(new[] { "CAT / 1", "CAT/2" }, canonical.CatalogNumbers);
        var metadata = new ProjectMetadata();
        // Already canonical values may have SourceAggregation origin, including manual Disc input.
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors, ["A / B"], MetadataOrigin.SourceAggregation);
        using var folder = new TemporaryFolder();
        var store = new ProjectMetadataStore();
        store.Save(folder.Path, metadata);
        Assert.Equal(new[] { "A / B" }, store.LoadOrCreate(folder.Path)
            .GetValue<IReadOnlyList<string>>(ProjectMetadataField.Authors).Value);
    }

    [Theory]
    [InlineData(MetadataTagFormat.Id3v2)]
    [InlineData(MetadataTagFormat.Xiph)]
    [InlineData(MetadataTagFormat.Mp4)]
    [InlineData(MetadataTagFormat.Ape)]
    public void Series_pairs_preserve_positions_and_alphanumeric_parts(MetadataTagFormat format)
    {
        var result = TagMetadataReader.CreateFromNamedTagsForTests(format,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["SERIES"] = ["First; / literal", "", "Third", "Fourth"],
                ["SERIES-PART"] = ["1B", "orphan", "3C", "", "extra"]
            });
        Assert.Equal(new[] { new SeriesEntry("First; / literal", "1B"), new SeriesEntry("Third", "3C"),
            new SeriesEntry("Fourth", "") }, result.Series);
    }

    [Fact]
    public void Explicit_series_data_wins_and_preservation_adds_other_series()
    {
        var result = TagMetadataReader.CreateFromNamedTagsForTests(MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["SERIES"] = ["First"], ["SERIES-PART"] = ["1B"],
                ["BOOKSTITCH_SERIES"] = ["{\"version\":1,\"series\":[{\"name\":\"First\",\"part\":\"wrong\"},{\"name\":\"Second\",\"part\":\"2C\"}]}"]
            });
        Assert.Equal(new[] { new SeriesEntry("First", "1B"), new SeriesEntry("Second", "2C") }, result.Series);
    }

    [Fact]
    public void Release_identifiers_and_barcode_never_become_active_fields()
    {
        var result = TagMetadataReader.CreateFromNamedTagsForTests(MetadataTagFormat.Mp4,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["MUSICBRAINZRELEASEID"] = ["release"], ["BARCODE"] = ["123"],
                ["MusicBrainz Work Id"] = ["work"], ["GOODREADSURL"] = ["https://www.goodreads.com/book/show/123"]
            });
        Assert.Equal("work", result.MusicBrainzWorkId);
        Assert.Equal("https://www.goodreads.com/book/show/123", result.GoodreadsUrl);
        Assert.Contains(result.RawTagEntries, entry => entry.OriginalKey.EndsWith("MUSICBRAINZRELEASEID"));
        Assert.Contains(result.RawTagEntries, entry => entry.OriginalKey.EndsWith("BARCODE"));
        Assert.DoesNotContain(ProjectMetadataFields.OutputFields, field => field is ProjectMetadataField.ReservedYear
            or ProjectMetadataField.ReservedBarcode or ProjectMetadataField.ReservedReleaseId);
        Assert.Equal(51, (int)ProjectMetadataField.SortComposers);
        Assert.Equal(52, (int)ProjectMetadataField.MusicBrainzWorkId);
        Assert.Equal(53, (int)ProjectMetadataField.GoodreadsUrl);
    }

    [Fact]
    public void New_fields_roundtrip_persistence_and_output_policy()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.SortComposers, ["Bach, Johann"], MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.MusicBrainzWorkId, "work", MetadataOrigin.User);
        metadata.SetValue(ProjectMetadataField.GoodreadsUrl, "https://goodreads.com/work/editions/123", MetadataOrigin.User);
        var store = new ProjectMetadataStore();
        store.Save(folder.Path, metadata);
        var loaded = store.LoadOrCreate(folder.Path);
        Assert.Equal(new[] { "Bach, Johann" }, loaded.GetValue<IReadOnlyList<string>>(ProjectMetadataField.SortComposers).Value);
        Assert.Equal("work", loaded.GetValue<string>(ProjectMetadataField.MusicBrainzWorkId).Value);
        Assert.Equal("https://goodreads.com/work/editions/123", loaded.GetValue<string>(ProjectMetadataField.GoodreadsUrl).Value);
        Assert.Equal(ProjectMetadataField.GoodreadsUrl, ConceptMetadataGroupCatalog.CatalogData[^2].Field);
        Assert.Equal(ProjectMetadataField.MusicBrainzWorkId, ConceptMetadataGroupCatalog.CatalogData[^1].Field);
        var projected = new ProjectMetadataOutputProjectionService().Create(loaded,
            new ProjectMetadataOutputProjectionOptions("", false, "en-US"),
            new Dictionary<ProjectMetadataField, OutputFieldPolicy> { [ProjectMetadataField.SortComposers] = OutputFieldPolicy.Disabled },
            new Dictionary<string, bool>());
        Assert.Equal("work", projected.MusicBrainzWorkId);
        Assert.Equal("https://goodreads.com/work/editions/123", projected.GoodreadsUrl);
        Assert.False(projected.IsFieldEnabled(ProjectMetadataField.SortComposers));
    }

    [Fact]
    public void Native_mp4_fields_win_over_freeform_fallbacks_and_foreign_movements_are_not_series()
    {
        using var folder = new TemporaryFolder();
        var path = System.IO.Path.Combine(folder.Path, "native.m4b");
        System.IO.File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "Mp4EmptyFreeformSeriesFixture.m4b"), path);
        using (var file = TagLib.File.Create(path))
        {
            var tag = Assert.IsType<TagLib.Mpeg4.AppleTag>(file.GetTag(TagTypes.Apple));
            var sink = new AppleTagMetadataSink(tag);
            sink.ClearFreeforms();
            tag.TitleSort = "native title";
            tag.AlbumSort = "native album";
            tag.AlbumArtistsSort = ["native author"];
            tag.PerformersSort = ["fallback artist"];
            tag.SetText(ByteVector.FromString("©st3", StringType.Latin1), ["native subtitle"]);
            tag.SetText(ByteVector.FromString("©mvn", StringType.Latin1), ["Symphony movement"]);
            tag.SetText(ByteVector.FromString("©mvi", StringType.Latin1), ["2"]);
            sink.SetFreeform("SORTTITLE", ["freeform title"]);
            sink.SetFreeform("SORTALBUM", ["freeform album"]);
            sink.SetFreeform("SORTAUTHORS", ["freeform author"]);
            sink.SetFreeform("SORTNARRATORS", ["narrator sort"]);
            sink.SetFreeform("SUBTITLE", ["freeform subtitle"]);
            sink.SetFreeform("ORIGINALTITLE", ["original title"]);
            file.Save();
        }
        var result = new TagMetadataReader().Read(path);
        Assert.False(result.Failed);
        Assert.Equal("native title", result.Original.SortTitle);
        Assert.Equal("native album", result.Original.SortAlbum);
        Assert.Equal(new[] { "native author" }, result.Original.SortAuthors);
        Assert.Equal(new[] { "narrator sort" }, result.Original.SortNarrators);
        Assert.Equal("native subtitle", result.Original.Subtitle);
        Assert.Equal("original title", result.Original.OriginalTitle);
        Assert.Empty(result.Original.Series);
        Assert.Contains(result.Original.RawTagEntries, entry => entry.OriginalKey == "©mvn");
    }

    [Theory]
    [InlineData("subt")]
    [InlineData("Subt")]
    public void Legacy_subtitle_atoms_are_still_read(string atom)
    {
        using var folder = new TemporaryFolder();
        var path = System.IO.Path.Combine(folder.Path, "legacy.m4b");
        System.IO.File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "Mp4EmptyFreeformSeriesFixture.m4b"), path);
        using (var file = TagLib.File.Create(path))
        {
            var tag = Assert.IsType<TagLib.Mpeg4.AppleTag>(file.GetTag(TagTypes.Apple));
            tag.SetText(ByteVector.FromString("©st3", StringType.Latin1), []);
            tag.SetText(ByteVector.FromString(atom, StringType.Latin1), ["Legacy"]);
            file.Save();
        }
        Assert.Equal("Legacy", new TagMetadataReader().Read(path).Original.Subtitle);
    }

    [Fact]
    public void Sort_suggestions_are_unconfirmed_refresh_and_only_user_acceptance_sets_canonical_values()
    {
        var state = new ProjectMetadataState();
        var field = new ConceptMetadataFieldViewModel(() => state.CanonicalMetadata, new MetadataPreferences(), new MetadataNormalizationService(),
            ProjectMetadataField.SortComposers, ConceptMetadataFieldCatalog.Get(ProjectMetadataField.SortComposers), _ => { }, _ => { });
        var session = new ConceptCandidateSessionService();
        IReadOnlyList<ConceptMetadataCandidate> Candidates() => ConceptMetadataFieldCandidateComposer.Build(field,
            state, [], [], [], session, new SourceMetadataAlternativeService(), []);
        state.CanonicalMetadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers, ["Bach", "Mozart"], MetadataOrigin.User);
        var candidates = Candidates();
        Assert.Equal(new[] { "Bach", "Mozart" }, candidates.Select(item => item.DisplayValue));
        Assert.All(candidates, item => { Assert.False(item.IsCanonicalWinner); Assert.False(item.IsAutomaticWinner); });
        Assert.Null(state.CanonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.SortComposers).Value);
        state.CanonicalMetadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Composers, ["Beethoven"], MetadataOrigin.User);
        Assert.Equal("Beethoven", Assert.Single(Candidates()).DisplayValue);
        field.Draft = "Beethoven";
        field.CommitDraft();
        Assert.Equal(new[] { "Beethoven" }, state.CanonicalMetadata.GetValue<IReadOnlyList<string>>(ProjectMetadataField.SortComposers).Value);
    }

    [Theory]
    [InlineData(ProjectMetadataField.SortTitle, ProjectMetadataField.Title)]
    [InlineData(ProjectMetadataField.SortAlbum, ProjectMetadataField.Album)]
    [InlineData(ProjectMetadataField.SortAuthors, ProjectMetadataField.Authors)]
    [InlineData(ProjectMetadataField.SortNarrators, ProjectMetadataField.Narrators)]
    [InlineData(ProjectMetadataField.SortSeries, ProjectMetadataField.Series)]
    [InlineData(ProjectMetadataField.SortComposers, ProjectMetadataField.Composers)]
    public void Every_sort_dropdown_offers_current_source_values_without_confirming_them(ProjectMetadataField sort, ProjectMetadataField source)
    {
        var state = new ProjectMetadataState();
        var preferences = new MetadataPreferences();
        MetadataPreferencesService.SetPolicy(preferences, sort, OutputFieldPolicy.Disabled);
        switch (ProjectMetadataFields.GetValueKind(source))
        {
            case ProjectMetadataFieldValueKind.StringList:
                state.CanonicalMetadata.SetValue<IReadOnlyList<string>>(source, ["First", "Second"], MetadataOrigin.User); break;
            case ProjectMetadataFieldValueKind.SeriesList:
                state.CanonicalMetadata.SetValue<IReadOnlyList<SeriesEntry>>(source,
                    [new SeriesEntry("First", "1B"), new SeriesEntry("Second", "2C")], MetadataOrigin.User); break;
            default: state.CanonicalMetadata.SetValue(source, "First", MetadataOrigin.User); break;
        }
        var revision = state.CanonicalMetadata.Revision;
        var field = new ConceptMetadataFieldViewModel(() => state.CanonicalMetadata, preferences, new MetadataNormalizationService(),
            sort, ConceptMetadataFieldCatalog.Get(sort), _ => { }, _ => { });
        var candidates = ConceptMetadataFieldCandidateComposer.Build(field, state, [], [], [],
            new ConceptCandidateSessionService(), new SourceMetadataAlternativeService(), []);
        Assert.Contains(candidates, candidate => candidate.DisplayValue == "First");
        if (ProjectMetadataFields.GetValueKind(source) != ProjectMetadataFieldValueKind.String)
            Assert.Contains(candidates, candidate => candidate.DisplayValue == "Second");
        Assert.All(candidates, candidate => { Assert.False(candidate.IsCanonicalWinner); Assert.False(candidate.IsAutomaticWinner); });
        Assert.Equal(revision, state.CanonicalMetadata.Revision);
        Assert.Equal(OutputFieldPolicy.Disabled, MetadataPreferencesService.GetPolicy(preferences, sort));
    }

    [Theory]
    [InlineData("2024", 2024, 0, 0)]
    [InlineData("2024/02", 2024, 2, 0)]
    [InlineData("20240229", 2024, 2, 29)]
    public void Real_atoms_roundtrip_date_precision_native_subtitle_and_sort_composer(string input, int year, int month, int day)
    {
        using var folder = new TemporaryFolder();
        var path = System.IO.Path.Combine(folder.Path, "metadata.m4b");
        System.IO.File.Copy(System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", "Mp4EmptyFreeformSeriesFixture.m4b"), path);
        Assert.True(MetadataDateParser.TryParse(input, out var date));
        using (var file = TagLib.File.Create(path))
        {
            var tag = Assert.IsType<TagLib.Mpeg4.AppleTag>(file.GetTag(TagTypes.Apple));
            new Mp4MetadataWriter().Write(new AppleTagMetadataSink(tag), new FinalAudioTagData
            {
                Subtitle = "Subtitle Ω", ReleaseDate = date, SortComposers = ["Bach, J. S."],
                MusicBrainzWorkId = "work-id", GoodreadsUrl = "https://goodreads.com/book/show/123",
                Series = [new SeriesEntry("One", "1B"), new SeriesEntry("One", "2C")]
            });
            file.Save();
        }
        using (var file = TagLib.File.Create(path))
        {
            var tag = Assert.IsType<TagLib.Mpeg4.AppleTag>(file.GetTag(TagTypes.Apple));
            Assert.Equal(new[] { "Subtitle Ω" }, tag.GetText(ByteVector.FromString("©st3", StringType.Latin1)));
            Assert.Equal(new[] { "Bach, J. S." }, tag.ComposersSort);
            Assert.Equal(new[] { date.ToString() }, tag.GetText(ByteVector.FromString("©day", StringType.Latin1)));
            Assert.Null(tag.GetDashBoxes("com.apple.iTunes", "RELEASEDATE"));
            Assert.Null(tag.GetDashBoxes("com.apple.iTunes", "SUBTITLE"));
            Assert.Null(tag.GetDashBoxes("com.apple.iTunes", "MUSICBRAINZRELEASEID"));
            var native = tag.DataBoxes(ByteVector.FromString("©st3", StringType.Latin1));
            Assert.All(native, box => Assert.Equal(1u, box.Flags));
        }
        var result = new TagMetadataReader().Read(path);
        Assert.False(result.Failed, string.Join("; ", result.Diagnostics));
        Assert.Equal("Subtitle Ω", result.Original.Subtitle);
        Assert.Equal("work-id", result.Original.MusicBrainzWorkId);
        Assert.Equal(new[] { "Bach, J. S." }, result.Original.SortComposers);
        Assert.Equal(new[] { new SeriesEntry("One", "1B"), new SeriesEntry("One", "2C") }, result.Original.Series);
        Assert.Equal(new MetadataDate(year, month == 0 ? null : month, day == 0 ? null : day),
            SourceMetadataCaptureService.BuildCanonicalFields(result.Original).ReleaseDate);
    }
}
