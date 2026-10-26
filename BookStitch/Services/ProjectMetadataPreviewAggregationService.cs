using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ProjectMetadataPreviewAggregationResult(
    bool HasSourceMetadata,
    ProjectMetadataInitializationResult InitializationResult);

public sealed class ProjectMetadataPreviewAggregationService
{
    private readonly ProjectMetadataInitializationService _initializationService;

    public ProjectMetadataPreviewAggregationService()
        : this(new ProjectMetadataInitializationService())
    {
    }

    public ProjectMetadataPreviewAggregationService(ProjectMetadataInitializationService initializationService)
    {
        _initializationService = initializationService ?? throw new ArgumentNullException(nameof(initializationService));
    }

    public ProjectMetadataPreviewAggregationResult Apply(
        ProjectMetadataState metadataState,
        IReadOnlyList<TrackInfo> tracks,
        ProjectMetadataInitializationOptions options,
        ProjectMetadataInitializationState initializationState = ProjectMetadataInitializationState.InitialSetComplete)
    {
        ArgumentNullException.ThrowIfNull(metadataState);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(options);

        var hasSourceMetadata = tracks.Any(track => track.SourceMetadataFields is not null);
        var snapshots = CreateSourceSnapshots(tracks);

        var metadata = metadataState.EnsureCanonicalMetadata();
        var result = _initializationService.Apply(
            metadata,
            snapshots,
            initializationState,
            options);

        if (result.Changed)
            metadataState.ApplyCanonicalMetadata(metadata);

        return new ProjectMetadataPreviewAggregationResult(hasSourceMetadata, result);
    }

    public IReadOnlyList<SourceMetadataSnapshot> CreateSourceSnapshots(IReadOnlyList<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        return tracks
            .Select(track => new SourceMetadataSnapshot
            {
                SourceId = track.SourceId,
                CanonicalFields = track.SourceMetadataFields ?? BuildLegacyInMemoryFields(track),
                TechnicalInfo = new SourceTechnicalInfo
                {
                    SourcePath = track.FilePath
                }
            })
            .ToArray();
    }

    private static SourceMetadataCanonicalFields BuildLegacyInMemoryFields(TrackInfo track)
    {
        var author = track.Artist?.Trim() ?? string.Empty;
        return new SourceMetadataCanonicalFields
        {
            Title = track.TagTitle?.Trim() ?? string.Empty,
            Authors = author.Length == 0 ? [] : [author]
        };
    }
}
