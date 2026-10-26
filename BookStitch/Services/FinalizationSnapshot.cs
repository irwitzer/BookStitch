using BookStitch.Models;

namespace BookStitch.Services;

public sealed record FinalizationSnapshot(
    long? ProjectMetadataRevision,
    FinalAudioTagData OutputTags,
    IReadOnlyDictionary<ProjectMetadataField, OutputFieldPolicy> OutputFieldPolicies,
    ChapterSettings? ChapterSettingsSnapshot,
    string OutputPresentationCulture,
    bool CompatibilityDescriptionEnabled,
    string CoverPath)
{
    public TimeSpan? ExpectedDuration { get; init; }
    public IReadOnlyList<TrackEmbeddedChapterInfo>? ExpectedChapters { get; init; }
}

public sealed record FinalizedOutputInspection(
    TimeSpan? Duration,
    bool TagReadFailed,
    OriginalTagMetadata Tags,
    AudioProbeInfo? ProbeInfo)
{
    public bool HasEmbeddedCover { get; init; }
    public IReadOnlyList<NeroChapterInfo>? NeroChapters { get; init; }
}

public interface IFinalizedOutputValidationService
{
    Task<FinalizedOutputInspection> InspectAsync(
        string finalPartPath,
        string ffprobePath,
        PerformanceExternalProcessContext? performanceContext,
        CancellationToken token);

    void Validate(FinalizationSnapshot snapshot, FinalizedOutputInspection inspection);
}

public sealed class NullFinalizedOutputValidationService : IFinalizedOutputValidationService
{
    public static NullFinalizedOutputValidationService Instance { get; } = new();

    private NullFinalizedOutputValidationService()
    {
    }

    public Task<FinalizedOutputInspection> InspectAsync(
        string finalPartPath,
        string ffprobePath,
        PerformanceExternalProcessContext? performanceContext,
        CancellationToken token) =>
        Task.FromResult(new FinalizedOutputInspection(null, TagReadFailed: false, new OriginalTagMetadata(), ProbeInfo: null));

    public void Validate(FinalizationSnapshot snapshot, FinalizedOutputInspection inspection)
    {
    }
}

public sealed class FinalizedOutputValidationService : IFinalizedOutputValidationService
{
    private readonly TagMetadataReader _tagMetadataReader;
    private readonly AudioInfoService _audioInfoService;

    public FinalizedOutputValidationService()
        : this(new TagMetadataReader(), new AudioInfoService())
    {
    }

    public FinalizedOutputValidationService(
        TagMetadataReader tagMetadataReader,
        AudioInfoService audioInfoService)
    {
        _tagMetadataReader = tagMetadataReader ?? throw new ArgumentNullException(nameof(tagMetadataReader));
        _audioInfoService = audioInfoService ?? throw new ArgumentNullException(nameof(audioInfoService));
    }

    public async Task<FinalizedOutputInspection> InspectAsync(
        string finalPartPath,
        string ffprobePath,
        PerformanceExternalProcessContext? performanceContext,
        CancellationToken token)
    {
        var tagResult = _tagMetadataReader.Read(finalPartPath);
        var probeInfo = await _audioInfoService
            .ProbeAsync(finalPartPath, ffprobePath, performanceContext, token)
            .ConfigureAwait(false);

        return new FinalizedOutputInspection(
            probeInfo.Duration ?? tagResult.TechnicalDurationFallback,
            tagResult.Failed,
            tagResult.Original,
            probeInfo)
        {
            HasEmbeddedCover = tagResult.HasEmbeddedCover,
            NeroChapters = Mp4ChapterListPreservation.ReadChapters(finalPartPath)
        };
    }

    public void Validate(FinalizationSnapshot snapshot, FinalizedOutputInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(inspection);

        if (inspection.TagReadFailed)
            throw Invalid("unreadable tags");

        if (inspection.ProbeInfo is null || !inspection.ProbeInfo.HasPlausibleAudioProperties)
        {
            throw Invalid(inspection.ProbeInfo?.ErrorMessage ?? "invalid audio properties");
        }

        if (snapshot.OutputTags.IsFieldEnabled(ProjectMetadataField.Authors) &&
            !Values(snapshot.OutputTags.Authors, snapshot.OutputTags.Author).SequenceEqual(inspection.Tags.Authors, StringComparer.Ordinal))
        {
            throw Invalid("authors");
        }

        if (snapshot.OutputTags.IsFieldEnabled(ProjectMetadataField.Narrators) &&
            !Values(snapshot.OutputTags.Narrators, snapshot.OutputTags.Narrator).SequenceEqual(inspection.Tags.Narrators, StringComparer.Ordinal))
        {
            throw Invalid("narrators");
        }
        var tags = snapshot.OutputTags;
        ValidateScalar(ProjectMetadataField.Title, tags.Title, inspection.Tags.Title);
        ValidateScalar(ProjectMetadataField.Album, string.IsNullOrWhiteSpace(tags.Album) ? tags.Title : tags.Album, inspection.Tags.Album);
        ValidateScalar(ProjectMetadataField.Publisher, tags.Publisher, inspection.Tags.Publisher);
        ValidateScalar(ProjectMetadataField.Copyright, tags.Copyright, inspection.Tags.Copyright);
        ValidateScalar(ProjectMetadataField.Comment, tags.Comment, inspection.Tags.Comment);
        if (tags.IsFieldEnabled(ProjectMetadataField.Genres) &&
            !Values(tags.Genres, tags.Genre).SequenceEqual(inspection.Tags.Genres, StringComparer.Ordinal))
            throw Invalid("genres");
        if (tags.IsFieldEnabled(ProjectMetadataField.Year) && tags.Year is > 0 and <= 9999 &&
            inspection.Tags.Year != tags.Year) throw Invalid("year");
        if (!string.IsNullOrWhiteSpace(snapshot.CoverPath) && System.IO.File.Exists(snapshot.CoverPath) &&
            new System.IO.FileInfo(snapshot.CoverPath).Length > 0 && !inspection.HasEmbeddedCover)
            throw Invalid("cover");

        // Allow container/packet rounding without accepting missing or shifted chapter boundaries.
        var tolerance = TimeSpan.FromMilliseconds(100).Ticks;
        if (snapshot.ExpectedDuration is { } expectedDuration &&
            (inspection.Duration is not { } actualDuration ||
             Math.Abs(actualDuration.Ticks - expectedDuration.Ticks) > tolerance)) throw Invalid("duration");
        if (inspection.NeroChapters is not null) throw Invalid("unexpected Nero chapter list");
        if (snapshot.ExpectedChapters is { } expectedChapters)
        {
            var actualChapters = inspection.ProbeInfo.Chapters;
            if (expectedChapters.Count != actualChapters.Count) throw Invalid("chapter count");
            for (var index = 0; index < expectedChapters.Count; index++)
            {
                var expected = expectedChapters[index];
                var actual = actualChapters[index];
                var implicitEnd = index + 1 < expectedChapters.Count
                    ? expectedChapters[index + 1].StartTicks : (inspection.Duration ?? inspection.ProbeInfo.Duration!.Value).Ticks;
                // QuickTime represents the first chapter start as zero, including leading gaps.
                var firstContainerStart = index == 0 && actual.StartTicks == 0;
                if (!string.Equals(ChapterTitle(expected.Title), actual.Title, StringComparison.Ordinal) ||
                    (!firstContainerStart && Math.Abs(expected.StartTicks - actual.StartTicks) > tolerance) ||
                    (Math.Abs(expected.EndTicks - actual.EndTicks) > tolerance && Math.Abs(implicitEnd - actual.EndTicks) > tolerance) ||
                    actual.EndTicks <= actual.StartTicks ||
                    (index > 0 && actual.StartTicks < actualChapters[index - 1].StartTicks))
                    throw Invalid("chapter sequence/bounds");
            }
        }

        void ValidateScalar(ProjectMetadataField field, string expected, string actual)
        {
            if (tags.IsFieldEnabled(field) && !string.Equals(expected.Trim(), actual, StringComparison.Ordinal))
                throw Invalid(field.ToString());
        }
    }

    private static string ChapterTitle(string title) => title.Replace("\r", " ").Replace("\n", " ").Trim();

    private static IReadOnlyList<string> Values(IReadOnlyList<string> values, string fallback) =>
        FinalTagService.CleanList(values) is { Count: > 0 } clean ? clean :
            string.IsNullOrWhiteSpace(fallback) ? [] : [fallback.Trim()];

    private static InvalidOperationException Invalid(string detail) =>
        new(LocalizationText.Get("Export.FinalValidationFailed"), new InvalidOperationException(detail));
}
