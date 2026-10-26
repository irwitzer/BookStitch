using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FinalizedOutputValidationServiceTests
{
    private static FinalAudioTagData Tags => new()
    { Title = "Book", Album = "Album", Author = "Author", Narrator = "Narrator", Genre = "Audiobook",
      Publisher = "Publisher", Copyright = "Copyright", Comment = "Comment", Year = 2026 };

    private static FinalizationSnapshot Snapshot(FinalAudioTagData? tags = null) => new(
        null, tags ?? Tags, new Dictionary<ProjectMetadataField, OutputFieldPolicy>(), null, "en", false, "")
    { ExpectedDuration = TimeSpan.FromSeconds(10), ExpectedChapters = Chapters() };

    private static TrackEmbeddedChapterInfo[] Chapters() =>
    [new() { Title = "First", StartTicks = 0, EndTicks = TimeSpan.FromSeconds(5).Ticks },
     new() { Title = "Second", StartTicks = TimeSpan.FromSeconds(5).Ticks, EndTicks = TimeSpan.FromSeconds(10).Ticks }];

    private static FinalizedOutputInspection Inspection(OriginalTagMetadata? tags = null) => new(
        TimeSpan.FromSeconds(10), false, tags ?? new OriginalTagMetadata
        { Title = "Book", Album = "Album", Authors = ["Author"], Narrators = ["Narrator"], Genres = ["Audiobook"],
          Publisher = "Publisher", Copyright = "Copyright", Comment = "Comment", Year = 2026 },
        new AudioProbeInfo { Success = true, CodecType = "audio", CodecName = "aac", Channels = 2,
            SampleRateHz = 44100, Duration = TimeSpan.FromSeconds(10), Chapters = Chapters().ToList() });

    [Fact]
    public void Complete_output_matches_expected_tags_chapters_and_packet_timeline() =>
        new FinalizedOutputValidationService().Validate(Snapshot(), Inspection());

    [Theory]
    [InlineData("missing")]
    [InlineData("order")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("title")]
    [InlineData("duration")]
    [InlineData("nero present")]
    [InlineData("nero empty")]
    public void Invalid_chapter_or_duration_contract_blocks_final_commit(string defect)
    {
        var inspection = Inspection();
        var chapters = inspection.ProbeInfo!.Chapters;
        switch (defect)
        {
            case "missing": chapters.RemoveAt(1); break;
            case "order": chapters.Reverse(); break;
            case "start": chapters[1].StartTicks += TimeSpan.FromSeconds(1).Ticks; break;
            case "end": chapters[0].EndTicks += TimeSpan.FromSeconds(1).Ticks; break;
            case "title": chapters[0].Title = "Wrong"; break;
            case "nero present": inspection = inspection with { NeroChapters = [new("First", 0)] }; break;
            case "nero empty": inspection = inspection with { NeroChapters = [] }; break;
            case "duration": inspection = inspection with { Duration = TimeSpan.FromSeconds(12) }; break;
        }
        Assert.Throws<InvalidOperationException>(() => new FinalizedOutputValidationService().Validate(Snapshot(), inspection));
    }

    [Fact]
    public void QuickTime_container_gap_projection_is_allowed_without_Nero()
    {
        var expected = new[] { new TrackEmbeddedChapterInfo { Title = "First", StartTicks = 10000000, EndTicks = 20000000 },
            new TrackEmbeddedChapterInfo { Title = "Second", StartTicks = 50000000, EndTicks = 60000000 } };
        var snapshot = Snapshot() with { ExpectedChapters = expected };
        var inspection = Inspection();
        new FinalizedOutputValidationService().Validate(snapshot, inspection);
    }

    [Fact]
    public void Small_container_rounding_is_allowed()
    {
        var inspection = Inspection() with { Duration = TimeSpan.FromSeconds(10.02) };
        inspection.ProbeInfo!.Chapters[0].EndTicks += TimeSpan.FromMilliseconds(1).Ticks;
        new FinalizedOutputValidationService().Validate(Snapshot(), inspection);
    }

    [Theory]
    [InlineData(ProjectMetadataField.Title)]
    [InlineData(ProjectMetadataField.Authors)]
    [InlineData(ProjectMetadataField.Narrators)]
    [InlineData(ProjectMetadataField.Album)]
    [InlineData(ProjectMetadataField.Genres)]
    [InlineData(ProjectMetadataField.Publisher)]
    [InlineData(ProjectMetadataField.Copyright)]
    [InlineData(ProjectMetadataField.Comment)]
    [InlineData(ProjectMetadataField.Year)]
    public void Missing_enabled_core_tag_including_single_person_blocks_commit(ProjectMetadataField field)
    {
        var expected = new FinalAudioTagData
        { Title = field == ProjectMetadataField.Title ? "Book" : "",
          Author = field == ProjectMetadataField.Authors ? "Author" : "",
          Narrator = field == ProjectMetadataField.Narrators ? "Narrator" : "",
          Album = field == ProjectMetadataField.Album ? "Album" : "",
          Genre = field == ProjectMetadataField.Genres ? "Audiobook" : "",
          Publisher = field == ProjectMetadataField.Publisher ? "Publisher" : "",
          Copyright = field == ProjectMetadataField.Copyright ? "Copyright" : "",
          Comment = field == ProjectMetadataField.Comment ? "Comment" : "",
          Year = field == ProjectMetadataField.Year ? 2026 : null };
        var inspection = Inspection(new OriginalTagMetadata());
        Assert.Throws<InvalidOperationException>(() => new FinalizedOutputValidationService().Validate(Snapshot(expected), inspection));
    }

    [Fact]
    public void Disabled_person_fields_are_not_required()
    {
        var tags = new FinalAudioTagData { Author = "Suppressed", Narrator = "Suppressed",
            DisabledFields = new HashSet<ProjectMetadataField> { ProjectMetadataField.Authors, ProjectMetadataField.Narrators } };
        new FinalizedOutputValidationService().Validate(Snapshot(tags), Inspection(new OriginalTagMetadata()));
    }

    [Fact]
    public void Selected_existing_cover_must_be_embedded()
    {
        using var folder = new TemporaryFolder();
        var cover = Path.Combine(folder.Path, "cover.png"); File.WriteAllBytes(cover, [1, 2, 3]);
        var snapshot = Snapshot() with { CoverPath = cover };
        var validator = new FinalizedOutputValidationService();
        Assert.Throws<InvalidOperationException>(() => validator.Validate(snapshot, Inspection()));
        validator.Validate(snapshot, Inspection() with { HasEmbeddedCover = true });
    }
}
