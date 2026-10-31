using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public class TrackListWarningServiceTests
{
    private readonly TrackListWarningService _service = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reliable_filename_inventory_resolves_ignored_source_tags_without_changing_any_numbers(bool snapshotOnly)
    {
        var rows = CreateFilenamePreferredFamily();
        rows[1].SourceTrackNumber = 99;
        rows[1].SourceMetadataFields = new SourceMetadataCanonicalFields { SourceTrackNumber = 99 };
        if (snapshotOnly) foreach (var row in rows) row.SourceTrackNumber = null;
        var before = rows.Select(row => (row.SourceTrackNumber, row.SourceMetadataFields!.SourceTrackNumber,
            row.TrackNumber, row.Index, row.AutomaticOrderPosition, row.ChapterTitle)).ToArray();
        var order = rows.ToArray();

        _service.Apply(rows);

        Assert.All(rows, row => Assert.False(row.HasChapterWarning));
        Assert.Equal(order, rows);
        Assert.Equal(before, rows.Select(row => (row.SourceTrackNumber, row.SourceMetadataFields!.SourceTrackNumber,
            row.TrackNumber, row.Index, row.AutomaticOrderPosition, row.ChapterTitle)));
        Assert.Equal(0, TrackWarningMetricsService.Create(rows.Select(row =>
            new TrackWarningMetric(row.DisplayFileWarning, row.DisplayChapterWarning))).ChapterWarningCount);
        Assert.False(_service.Apply(rows));
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("duplicate")]
    [InlineData("non-numeric")]
    [InlineData("year")]
    [InlineData("folder")]
    [InlineData("short")]
    [InlineData("working-number")]
    public void Matching_working_number_alone_does_not_hide_an_unresolved_source_conflict(string ambiguity)
    {
        var rows = CreateFilenamePreferredFamily();
        switch (ambiguity)
        {
            case "gap": rows[2].FileName = "0244 Book.mp3"; rows[2].TrackNumber = 244; break;
            case "duplicate": rows[2].FileName = rows[1].FileName; rows[2].TrackNumber = rows[1].TrackNumber; break;
            case "non-numeric": rows[2].FileName = "appendix.mp3"; break;
            case "year": for (var i = 0; i < rows.Count; i++) { rows[i].TrackNumber = 2020 + i; rows[i].FileName = $"{2020 + i} Book.mp3"; } break;
            case "folder": rows[2].RelativeFolder = "Other"; break;
            case "short": rows.RemoveAt(2); break;
            case "working-number": rows[2].TrackNumber = 999; break;
        }

        _service.Apply(rows);

        var first = rows[0].TrackNumber!.Value;
        Assert.Contains(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 1, first), rows[0].ChapterWarningText);
        if (ambiguity == "duplicate")
            Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.DuplicateChapter), rows[1].ChapterWarningText);
        if (ambiguity == "gap")
            Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.MissingChapter), rows[2].ChapterWarningText);
    }

    [Fact]
    public void Resolved_metadata_conflict_does_not_hide_reordering_or_independent_warning_codes()
    {
        var rows = CreateFilenamePreferredFamily();
        rows[0].WarningCodes = TrackWarningCodes.MissingSource | TrackWarningCodes.DuplicateChapterName |
            TrackWarningCodes.DuplicateChapter | TrackWarningCodes.MissingChapter | TrackWarningCodes.UnreadableTags;
        var codes = rows[0].WarningCodes;
        (rows[1], rows[2]) = (rows[2], rows[1]);
        var order = rows.ToArray();

        _service.Apply(rows);

        Assert.Equal(codes, rows[0].WarningCodes);
        Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.MissingSource), rows[0].FileWarningText);
        foreach (var code in new[] { TrackWarningCodes.DuplicateChapterName, TrackWarningCodes.DuplicateChapter,
            TrackWarningCodes.MissingChapter, TrackWarningCodes.UnreadableTags })
            Assert.Contains(TrackWarningPresentation.Format(code), rows[0].ChapterWarningText);
        Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder), rows[2].ChapterWarningText);
        Assert.All(rows, row => Assert.DoesNotContain(
            LocalizationText.Format("WarningTrackTagFileNumberMismatch", 1, row.TrackNumber!.Value), row.ChapterWarningText));
        Assert.Equal(order, rows);
    }

    [Fact]
    public void Excluded_sources_support_the_inventory_without_inventing_a_gap()
    {
        var rows = CreateFilenamePreferredFamily();
        rows[1].IsExcluded = true;
        _service.Apply(rows);
        Assert.All(rows, row => Assert.False(row.HasChapterWarning));
        Assert.True(rows[1].IsExcluded);
    }

    [Fact]
    public void Resolution_is_scoped_per_disc_and_does_not_hide_an_ambiguous_neighbour()
    {
        var resolved = CreateFilenamePreferredFamily();
        var unresolved = CreateFilenamePreferredFamily(2, disc: 3);
        var rows = resolved.Concat(unresolved).ToList();
        _service.Apply(rows);
        Assert.All(resolved, row => Assert.False(row.HasChapterWarning));
        Assert.All(unresolved, row => Assert.Equal(
            LocalizationText.Format("WarningTrackTagFileNumberMismatch", 1, row.TrackNumber!.Value), row.ChapterWarningText));
    }

    [Fact]
    public void Conflict_is_recalculated_when_inventory_evidence_appears_or_disappears()
    {
        var rows = CreateFilenamePreferredFamily();
        rows[0].ChapterWarningText = LocalizationText.Format("WarningTrackTagFileNumberMismatch", 1, 241);
        Assert.True(_service.Apply(rows));
        Assert.All(rows, row => Assert.False(row.HasChapterWarning));
        rows[2].TrackNumber = 244;
        rows[2].FileName = "0244 Book.mp3";
        _service.Apply(rows);
        Assert.Contains(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 1, 241), rows[0].ChapterWarningText);
        rows[2].TrackNumber = 243;
        rows[2].FileName = "0243 Book.mp3";
        _service.Apply(rows);
        Assert.All(rows, row => Assert.False(row.HasChapterWarning));
    }

    private static List<TrackInfo> CreateFilenamePreferredFamily(int count = 3, int disc = 2) => Enumerable.Range(0, count)
        .Select(i => new TrackInfo { FileName = $"{241 + i:0000} Book.mp3", DiscNumber = disc, RelativeFolder = $"CD {disc:00}",
            SourceTrackNumber = 1, SourceMetadataFields = new SourceMetadataCanonicalFields { SourceTrackNumber = 1 },
            TrackNumber = 241 + i, Index = 95 + i, AutomaticOrderPosition = 95 + i, ChapterTitle = $"{95 + i} Book" }).ToList();

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 2000)]
    [InlineData(3, 3000)]
    public void Repeated_source_tags_with_proven_padded_filename_family_report_one_metadata_conflict(int disc, int prefix)
    {
        var rows = CreateRepeatedTagFamily(142);
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].DiscNumber = disc;
            rows[i].FileName = $"{prefix + i + 1:00000} Book.mp3";
        }
        var originalRows = rows.ToArray();
        var originalPresentation = rows.Select(row => (row.TrackNumber, row.SourceTrackNumber,
            row.Index, row.AutomaticOrderPosition, row.ChapterTitle)).ToArray();

        _service.Apply(rows);

        Assert.Equal(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 1, prefix + 1), rows[0].ChapterWarningText);
        Assert.All(rows.Skip(1), row => Assert.False(row.HasChapterWarning));
        Assert.Equal(1, TrackWarningMetricsService.Create(rows.Select(row =>
            new TrackWarningMetric(row.DisplayFileWarning, row.DisplayChapterWarning))).ChapterWarningCount);
        Assert.Equal(originalRows, rows);
        Assert.Equal(originalPresentation, rows.Select(row => (row.TrackNumber, row.SourceTrackNumber,
            row.Index, row.AutomaticOrderPosition, row.ChapterTitle)));
        var warnings = rows.Select(row => row.ChapterWarningText).ToArray();
        _service.Apply(rows);
        Assert.Equal(warnings, rows.Select(row => row.ChapterWarningText));
    }

    [Fact]
    public void Repeated_tag_filename_evidence_preserves_manual_order_and_exclusions()
    {
        var rows = CreateRepeatedTagFamily(4);
        rows[0].IsExcluded = true;
        (rows[1], rows[2]) = (rows[2], rows[1]);
        var originalRows = rows.ToArray();

        _service.Apply(rows);

        Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder), rows[2].ChapterWarningText);
        Assert.Contains(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 1, 1002), rows[2].ChapterWarningText);
        Assert.All(rows, row => Assert.DoesNotContain(TrackWarningPresentation.Format(TrackWarningCodes.DuplicateChapter), row.ChapterWarningText));
        Assert.False(rows[0].HasChapterWarning);
        Assert.True(rows[0].IsExcluded);
        Assert.Equal(originalRows, rows);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("gap")]
    [InlineData("title")]
    [InlineData("width")]
    [InlineData("source")]
    [InlineData("missing-source")]
    [InlineData("working-number")]
    [InlineData("folder")]
    [InlineData("year")]
    [InlineData("overflow")]
    [InlineData("short")]
    [InlineData("unpadded")]
    [InlineData("no-separator")]
    public void Ambiguous_filename_families_do_not_silence_duplicate_warnings(string ambiguity)
    {
        var rows = CreateRepeatedTagFamily(3);
        switch (ambiguity)
        {
            case "duplicate": rows[2].FileName = rows[1].FileName; break;
            case "gap": rows[2].FileName = "01004 Book.mp3"; break;
            case "title": rows[2].FileName = "01003 Other.mp3"; break;
            case "width": rows[2].FileName = "001003 Book.mp3"; break;
            case "source": rows[2].SourceTrackNumber = 2; break;
            case "missing-source": rows[2].SourceTrackNumber = null; break;
            case "working-number": rows[2].TrackNumber = 2; break;
            case "folder": rows[2].RelativeFolder = "Other"; break;
            case "year": for (var i = 0; i < rows.Count; i++) rows[i].FileName = $"{2020 + i:00000} Book.mp3"; break;
            case "overflow": for (var i = 0; i < rows.Count; i++) rows[i].FileName = $"0{3000000000L + i} Book.mp3"; break;
            case "short": rows.RemoveAt(2); break;
            case "unpadded": for (var i = 0; i < rows.Count; i++) rows[i].FileName = $"{1001 + i} Book.mp3"; break;
            case "no-separator": for (var i = 0; i < rows.Count; i++) rows[i].FileName = $"{1001 + i:00000}Book.mp3"; break;
        }

        _service.Apply(rows);

        Assert.Contains(rows, row => row.ChapterWarningText.Contains(
            TrackWarningPresentation.Format(TrackWarningCodes.DuplicateChapter), StringComparison.Ordinal));
    }

    [Fact]
    public void Repeated_tag_filename_families_are_scoped_per_disc_and_keep_independent_warnings()
    {
        var rows = CreateRepeatedTagFamily(3);
        var secondDisc = CreateRepeatedTagFamily(3);
        foreach (var row in secondDisc) row.DiscNumber = 2;
        secondDisc[1].WarningCodes = TrackWarningCodes.InvalidAudio | TrackWarningCodes.MissingChapter |
            TrackWarningCodes.UnreadableTags | TrackWarningCodes.DuplicateChapterName;
        rows.AddRange(secondDisc);

        _service.Apply(rows);

        Assert.Equal(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 1, 1001), rows[0].ChapterWarningText);
        Assert.Equal(rows[0].ChapterWarningText, rows[3].ChapterWarningText);
        Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.InvalidAudio), rows[4].FileWarningText);
        foreach (var code in new[] { TrackWarningCodes.MissingChapter, TrackWarningCodes.UnreadableTags, TrackWarningCodes.DuplicateChapterName })
            Assert.Contains(TrackWarningPresentation.Format(code), rows[4].ChapterWarningText);
    }

    [Fact]
    public void Filename_family_conflict_moves_off_excluded_rows_and_is_removed_when_evidence_changes()
    {
        var rows = CreateRepeatedTagFamily(3);
        foreach (var row in rows)
        {
            row.SourceTrackNumber = null;
            row.SourceMetadataFields = new SourceMetadataCanonicalFields { SourceTrackNumber = 1 };
        }
        _service.Apply(rows);
        rows[0].IsExcluded = true;
        _service.Apply(rows);
        Assert.False(rows[0].HasChapterWarning);
        Assert.Equal(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 1, 1002), rows[1].ChapterWarningText);
        rows[2].FileName = rows[1].FileName;
        _service.Apply(rows);
        Assert.All(rows.Skip(1), row => Assert.Equal(
            TrackWarningPresentation.Format(TrackWarningCodes.DuplicateChapter), row.ChapterWarningText));
    }

    private static List<TrackInfo> CreateRepeatedTagFamily(int count) => Enumerable.Range(1, count)
        .Select(n => new TrackInfo { FileName = $"{1000 + n:00000} Book.mp3", DiscNumber = 1,
            SourceTrackNumber = 1, TrackNumber = 1, Index = n, AutomaticOrderPosition = n,
            ChapterTitle = $"{n} Book" }).ToList();

    [Theory]
    [InlineData("019_book.mp3", 99, true)]
    [InlineData("Track 019.mp3", 99, true)]
    [InlineData("019_book.mp3", 19, false)]
    [InlineData("019_book.mp3", null, false)]
    [InlineData("019_book.mp3", 0, false)]
    [InlineData("019_book.mp3", -1, false)]
    [InlineData("book_019.mp3", 99, false)]
    [InlineData("book.mp3", 99, false)]
    [InlineData("000_book.mp3", 99, false)]
    [InlineData("123456789012345_book.mp3", 99, false)]
    public void Apply_compares_original_track_tag_with_filename_only(string fileName, int? sourceNumber, bool expected)
    {
        var row = new TrackInfo { FileName = fileName, SourceTrackNumber = sourceNumber,
            TrackNumber = 7, Index = 42, TagTitle = "Track 99", IsExcluded = true };
        _service.Apply([row]);
        Assert.Equal(expected, row.HasChapterWarning);
        if (expected)
            Assert.Equal(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 99, 19), row.ChapterWarningText);
        Assert.False(row.HasFileWarning);
        Assert.Equal(sourceNumber, row.SourceTrackNumber);
        Assert.Equal(7, row.TrackNumber);
        Assert.Equal(42, row.Index);
        Assert.Equal(fileName, row.FileName);
    }

    [Fact]
    public void Apply_uses_preserved_metadata_without_inventing_a_source_tag_from_working_number()
    {
        var row = new TrackInfo { FileName = "019_book.mp3", TrackNumber = 19,
            SourceMetadataFields = new SourceMetadataCanonicalFields { SourceTrackNumber = 99 } };
        _service.Apply([row]);
        Assert.Equal(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 99, 19), row.ChapterWarningText);
        row.SourceMetadataFields = null;
        _service.Apply([row]);
        Assert.False(row.HasChapterWarning);
    }

    [Fact]
    public void Apply_combines_metadata_and_order_reasons_without_duplicates_and_clears_resolved_mismatch()
    {
        var first = new TrackInfo { TrackNumber = 20, SourceTrackNumber = 20, FileName = "020_book.mp3" };
        var row = new TrackInfo { TrackNumber = 19, SourceTrackNumber = 99, FileName = "019_book.mp3" };
        var rows = new List<TrackInfo> { first, row };
        _service.Apply(rows);
        Assert.Equal(new[] { LocalizationText.Format("WarningTrackTagFileNumberMismatch", 99, 19),
            TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder) }, TrackInfo.SplitWarningMessages(row.ChapterWarningText));
        var warning = row.ChapterWarningText;
        _service.Apply(rows);
        Assert.Equal(warning, row.ChapterWarningText);
        row.SourceTrackNumber = 19;
        _service.Apply(rows);
        Assert.Equal(TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder), row.ChapterWarningText);
        Assert.Same(first, rows[0]);
        Assert.Same(row, rows[1]);
    }

    [Fact]
    public void Complete_filename_inventory_with_mistyped_source_tag_shows_only_metadata_conflict()
    {
        var rows = Enumerable.Range(170, 169).Select(n => new TrackInfo
        { DiscNumber = 2, TrackNumber = n == 213 ? 123 : n, SourceTrackNumber = n == 213 ? 123 : n,
            FileName = $"{n}_book_CD02.mp3" }).ToList();
        _service.Apply(rows);
        Assert.Single(rows, row => row.HasChapterWarning);
        Assert.Equal(LocalizationText.Format("WarningTrackTagFileNumberMismatch", 123, 213), rows[43].ChapterWarningText);
        Assert.Equal(123, rows[43].TrackNumber);
        Assert.Equal(123, rows[43].SourceTrackNumber);
    }

    [Fact]
    public void Complete_filename_inventory_does_not_invent_gaps_from_a_mistyped_tag()
    {
        var rows = Enumerable.Range(170, 169).Select(n => new TrackInfo
        { DiscNumber = 2, TrackNumber = n == 213 ? 123 : n, FileName = $"{n}_book_CD02.mp3" }).ToList();
        _service.Apply(rows);
        Assert.All(rows, row => Assert.False(row.HasChapterWarning));
        Assert.Equal(123, rows[43].TrackNumber);
        (rows[0], rows[1]) = (rows[1], rows[0]);
        _service.Apply(rows);
        Assert.True(rows[1].HasChapterWarning);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    public void Refresh_retires_missing_title_warnings_and_counts_without_hiding_real_warnings(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            var missing = new TrackInfo { TrackNumber = 1, ChapterWarningText = "obsolete rendered title warning" };
            missing.SetWarning(TrackWarningPresentation.Format(TrackWarningCodes.MissingTagTitle), TrackWarningCodes.MissingTagTitle);
            var real = new TrackInfo { TrackNumber = 2, WarningCodes = TrackWarningCodes.MissingTagTitle |
                TrackWarningCodes.UnreadableTags | TrackWarningCodes.CheckOrder | TrackWarningCodes.InvalidAudio };
            var rows = new List<TrackInfo> { missing, real };

            Assert.True(_service.Apply(rows));
            Assert.Equal(TrackWarningCodes.None, missing.WarningCodes);
            Assert.False(missing.HasChapterWarning);
            Assert.False(missing.HasFileWarning);
            Assert.Equal(TrackWarningCodes.UnreadableTags | TrackWarningCodes.CheckOrder | TrackWarningCodes.InvalidAudio, real.WarningCodes);
            Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder), real.DisplayChapterWarning);
            Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.UnreadableTags), real.DisplayChapterWarning);
            Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.InvalidAudio), real.DisplayFileWarning);
            var metrics = TrackWarningMetricsService.Create(rows.Select(t => new TrackWarningMetric(t.DisplayFileWarning, t.DisplayChapterWarning)));
            Assert.Equal(1, metrics.ChapterWarningCount);
            Assert.Equal(1, metrics.FileWarningCount);
            Assert.Same(missing, rows[0]);
            Assert.Same(real, rows[1]);
            Assert.False(_service.Apply(rows));
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void Apply_ExtremeSourceNumberFollowedByLowerNumberPreservesOrderWarning()
    {
        var tracks = new List<TrackInfo>
        {
            new() { Index = 1, TrackNumber = int.MaxValue - 1 },
            new() { Index = 2, TrackNumber = int.MaxValue },
            new() { Index = 3, TrackNumber = 1 }
        };
        var originalOrder = tracks.ToArray();

        _service.Apply(tracks);

        Assert.Contains(TrackWarningPresentation.Format(TrackWarningCodes.CheckOrder), tracks[2].ChapterWarningText);
        Assert.DoesNotContain(TrackWarningPresentation.Format(TrackWarningCodes.MissingChapter), tracks[2].ChapterWarningText);
        Assert.Equal(originalOrder, tracks);
        Assert.Equal([1, 2, 3], tracks.Select(track => track.Index));
    }

    [Fact]
    public void Apply_MapsInvalidAudioToShortFileWarning()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, Codec = "Ungültig", Warning = "Keine gültige Audiodatei erkannt." }
        };

        var changed = _service.Apply(tracks);

        Assert.True(changed);
        Assert.Equal("Keine gültige Audiodatei", tracks[0].FileWarningText);
        Assert.Equal("Keine gültige Audiodatei", tracks[0].DisplayFileWarning);
    }

    [Fact]
    public void Apply_AllowsFileAndChapterWarningOnSameTrack()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, Warning = "Quelldatei ist leer" },
            new() { TrackNumber = 1 }
        };

        _service.Apply(tracks);

        Assert.Equal("Quelldatei leer", tracks[0].FileWarningText);
        Assert.Equal("Kapitel doppelt", tracks[0].ChapterWarningText);
        Assert.Equal("Kapitel doppelt", tracks[1].ChapterWarningText);
    }

    [Fact]
    public void Apply_ReassignsTagMetadataMessagesToChapterWarnings()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, Warning = "Kein Tag-Titel; Tags konnten nicht gelesen werden" }
        };

        _service.Apply(tracks);

        Assert.Equal(string.Empty, tracks[0].FileWarningText);
        Assert.Equal("Tags konnten nicht gelesen werden", tracks[0].ChapterWarningText);
        Assert.Equal(string.Empty, tracks[0].DisplayHintWarning);
    }

    [Fact]
    public void Apply_PreservesMultipleChapterWarningsOnOneTrack()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1 },
            new() { TrackNumber = 3, Warning = "Doppelter Kapitelname" }
        };

        _service.Apply(tracks);

        Assert.Equal("Doppelter Kapitelname · Kapitel fehlt", tracks[1].ChapterWarningText);
    }

    [Fact]
    public void Apply_MarksFirstTrackAfterMissingTrackNumber()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, ChapterTitle = "001 Anfang" },
            new() { TrackNumber = 2, ChapterTitle = "002 Mitte" },
            new() { TrackNumber = 4, ChapterTitle = "003 Ende" },
            new() { TrackNumber = 5, ChapterTitle = "004 Schluss" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "", "Kapitel fehlt", "" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_MarksBackwardTrackOrderForReview()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 3, ChapterTitle = "001 Ende" },
            new() { TrackNumber = 2, ChapterTitle = "002 Mitte" }
        };

        _service.Apply(tracks);

        Assert.Equal("Sortierung prüfen", tracks[1].ChapterWarningText);
    }

    [Fact]
    public void Apply_IgnoresExcludedTracksForChapterSequence()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, ChapterTitle = "001 Anfang" },
            new() { TrackNumber = 2, ChapterTitle = "002 Ausgeschlossen", IsExcluded = true },
            new() { TrackNumber = 3, ChapterTitle = "003 Ende" }
        };

        _service.Apply(tracks);

        Assert.Equal("", tracks[0].ChapterWarningText);
        Assert.Equal("", tracks[1].ChapterWarningText);
        Assert.Equal("", tracks[2].ChapterWarningText);
    }

    [Fact]
    public void Apply_MarksMissingChapterWhenGapIsNotOnlyExcludedTracks()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, ChapterTitle = "001 Anfang" },
            new() { TrackNumber = 2, ChapterTitle = "002 Ausgeschlossen", IsExcluded = true },
            new() { TrackNumber = 4, ChapterTitle = "003 Ende" }
        };

        _service.Apply(tracks);

        Assert.Equal("Kapitel fehlt", tracks[2].ChapterWarningText);
    }

    [Fact]
    public void Apply_MarksPossibleMissingDiscForInternalDiscGap()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1, ChapterTitle = "CD 1 Track 1" },
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1" },
            new() { DiscNumber = 4, TrackNumber = 1, ChapterTitle = "CD 4 Track 1", AutomaticOrderPosition = 3 }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "", "Disc 3 fehlt möglicherweise" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_DoesNotMarkDiscInventoryGapWhenDiscSequenceIsComplete()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1 },
            new() { DiscNumber = 2, TrackNumber = 1 },
            new() { DiscNumber = 3, TrackNumber = 1 },
            new() { DiscNumber = 4, TrackNumber = 1 }
        };

        _service.Apply(tracks);

        Assert.All(tracks, track => Assert.Equal("", track.ChapterWarningText));
    }

    [Fact]
    public void Apply_DoesNotMarkMissingEarlierDiscsWhenPartialDiscSetStartsAtThree()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 3, TrackNumber = 1 },
            new() { DiscNumber = 4, TrackNumber = 1 },
            new() { DiscNumber = 5, TrackNumber = 1 }
        };

        _service.Apply(tracks);

        Assert.All(tracks, track => Assert.Equal("", track.ChapterWarningText));
    }

    [Fact]
    public void Apply_MarksPossibleMissingDiscRangeForMultipleInternalDiscGaps()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1 },
            new() { DiscNumber = 2, TrackNumber = 1 },
            new() { DiscNumber = 5, TrackNumber = 1 }
        };

        _service.Apply(tracks);

        Assert.Equal("Disc 3-4 fehlen möglicherweise", tracks[2].ChapterWarningText);
    }

    [Fact]
    public void Apply_DoesNotTreatNonConsecutiveSourceNumbersAsMissingChapters()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 10 },
            new() { TrackNumber = 20 },
            new() { TrackNumber = 30 },
            new() { TrackNumber = 40 }
        };

        _service.Apply(tracks);

        Assert.All(tracks, track => Assert.Equal("", track.ChapterWarningText));
    }

    [Fact]
    public void Apply_DoesNotMarkInventoryGapForExcludedExistingTrack()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1 },
            new() { TrackNumber = 2 },
            new() { TrackNumber = 3, IsExcluded = true },
            new() { TrackNumber = 4 },
            new() { TrackNumber = 5 }
        };

        _service.Apply(tracks);

        Assert.All(tracks, track => Assert.Equal("", track.ChapterWarningText));
    }

    [Fact]
    public void Apply_CurrentOrderWarningsRemainWhenAllSourceTracksExistButOrderIsWrong()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TrackNumber = 1, AutomaticOrderPosition = 1 },
            new() { TrackNumber = 5, AutomaticOrderPosition = 5 },
            new() { TrackNumber = 2, AutomaticOrderPosition = 2 },
            new() { TrackNumber = 4, AutomaticOrderPosition = 4 },
            new() { TrackNumber = 3, AutomaticOrderPosition = 3 }
        };

        _service.Apply(tracks);

        Assert.Contains(tracks, track => track.ChapterWarningText == "Sortierung prüfen");
        Assert.DoesNotContain(tracks, track => track.ChapterWarningText.Contains("möglicherweise", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_UsesTagTitleNumberWhenTrackNumberIsMissing()
    {
        var tracks = new List<TrackInfo>
        {
            new() { TagTitle = "001 Anfang", ChapterTitle = "001 Vorschlag" },
            new() { TagTitle = "002 Mitte", ChapterTitle = "002 Vorschlag" },
            new() { TagTitle = "003 Ende", ChapterTitle = "003 Vorschlag" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "", "" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_UsesFileNameNumberWhenTrackNumberAndTagTitleAreMissing()
    {
        var tracks = new List<TrackInfo>
        {
            new() { FileName = "001_Anfang.mp3", ChapterTitle = "001 Vorschlag" },
            new() { FileName = "003_Ende.mp3", ChapterTitle = "002 Vorschlag" }
        };

        _service.Apply(tracks);

        Assert.Equal("Kapitel fehlt", tracks[1].ChapterWarningText);
    }

    [Fact]
    public void Apply_DoesNotWarnWhenNoSequenceNumberCanBeRead()
    {
        var tracks = new List<TrackInfo>
        {
            new() { ChapterTitle = "Prolog" }
        };

        _service.Apply(tracks);

        Assert.Equal("", tracks[0].ChapterWarningText);
        Assert.Equal("", tracks[0].DisplayChapterWarning);
    }

    [Fact]
    public void Apply_ReturnsFalseWhenWarningsAreAlreadyCurrent()
    {
        var tracks = new List<TrackInfo>
        {
            new()
            {
                TrackNumber = 1,
                Warning = "Quelldatei ist leer",
                FileWarningText = "Quelldatei leer"
            }
        };

        var changed = _service.Apply(tracks);

        Assert.False(changed);
    }


    [Fact]
    public void Apply_DoesNotMarkSameTrackNumbersAcrossDifferentDiscsAsDuplicates()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1, ChapterTitle = "CD 1 Track 1" },
            new() { DiscNumber = 1, TrackNumber = 2, ChapterTitle = "CD 1 Track 2" },
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1" },
            new() { DiscNumber = 2, TrackNumber = 2, ChapterTitle = "CD 2 Track 2" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "", "", "" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_MarksDuplicateTrackNumbersWithinSameDisc()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1" },
            new() { DiscNumber = 2, TrackNumber = 2, ChapterTitle = "CD 2 Track 2" },
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1 erneut" },
            new() { DiscNumber = 2, TrackNumber = 2, ChapterTitle = "CD 2 Track 2 erneut" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "Kapitel doppelt", "Kapitel doppelt", "Kapitel doppelt", "Kapitel doppelt" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_RestartsSequenceCheckForEachDisc()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1, ChapterTitle = "CD 1 Track 1" },
            new() { DiscNumber = 1, TrackNumber = 3, ChapterTitle = "CD 1 Track 3" },
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1" },
            new() { DiscNumber = 2, TrackNumber = 2, ChapterTitle = "CD 2 Track 2" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "Kapitel fehlt", "", "" }, tracks.Select(track => track.ChapterWarningText));
    }

    [Fact]
    public void Apply_UsesExcludedTracksOnlyWithinSameDiscForMissingChapterCheck()
    {
        var tracks = new List<TrackInfo>
        {
            new() { DiscNumber = 1, TrackNumber = 1, ChapterTitle = "CD 1 Track 1" },
            new() { DiscNumber = 1, TrackNumber = 2, ChapterTitle = "CD 1 Track 2 ausgeschlossen", IsExcluded = true },
            new() { DiscNumber = 1, TrackNumber = 3, ChapterTitle = "CD 1 Track 3" },
            new() { DiscNumber = 2, TrackNumber = 1, ChapterTitle = "CD 2 Track 1" },
            new() { DiscNumber = 2, TrackNumber = 3, ChapterTitle = "CD 2 Track 3" }
        };

        _service.Apply(tracks);

        Assert.Equal(new[] { "", "", "", "", "Kapitel fehlt" }, tracks.Select(track => track.ChapterWarningText));
    }

}
