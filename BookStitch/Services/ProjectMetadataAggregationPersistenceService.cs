using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ProjectMetadataAggregationPersistenceRequest(
    string ProjectFolder,
    ProjectMetadataInitializationState State,
    ProjectMetadataInitializationOptions? Options = null);

public sealed record ProjectMetadataAggregationPersistenceResult(
    bool Changed,
    ProjectMetadata CanonicalMetadata,
    ProjectMetadataProjection Metadata,
    IReadOnlyList<ProjectMetadataField> AppliedFields,
    IReadOnlyList<MetadataAggregationDiagnostic> Diagnostics,
    IReadOnlyList<MetadataFieldDecisionDiagnostic> FieldDecisions);

/// <summary>
/// Aggregates captured source metadata into the caller-owned canonical metadata instance.
/// Persistence is deliberately owned by <see cref="ProjectMetadataSession"/>.
/// </summary>
public sealed class ProjectMetadataAggregationPersistenceService
{
    private readonly SourceMetadataSnapshotStore _snapshotStore;
    private readonly ProjectMetadataInitializationService _initializationService;

    public ProjectMetadataAggregationPersistenceService()
        : this(
            new SourceMetadataSnapshotStore(),
            new ProjectMetadataInitializationService())
    {
    }

    public ProjectMetadataAggregationPersistenceService(
        SourceMetadataSnapshotStore snapshotStore,
        ProjectMetadataInitializationService initializationService)
    {
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
        _initializationService = initializationService ?? throw new ArgumentNullException(nameof(initializationService));
    }

    public ProjectMetadataAggregationPersistenceResult Apply(
        ProjectMetadata metadata,
        ProjectMetadataAggregationPersistenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectFolder);

        var snapshots = OrderSnapshotsForAggregation(_snapshotStore.LoadAll(request.ProjectFolder));
        var result = _initializationService.Apply(
            metadata,
            snapshots,
            request.State,
            request.Options);

        return new ProjectMetadataAggregationPersistenceResult(
            result.Changed,
            metadata,
            ProjectMetadataMigrationService.CreateProjection(metadata),
            result.AppliedFields,
            result.Diagnostics,
            result.FieldDecisions);
    }

    private static IReadOnlyList<SourceMetadataSnapshot> OrderSnapshotsForAggregation(
        IReadOnlyList<SourceMetadataSnapshot> snapshots)
    {
        var naturalComparer = new NaturalStringComparer();
        return snapshots
            .OrderBy(snapshot => string.IsNullOrWhiteSpace(snapshot.TechnicalInfo.SourcePath) ? 1 : 0)
            .ThenBy(snapshot => snapshot.TechnicalInfo.SourcePath ?? string.Empty, naturalComparer)
            .ThenBy(snapshot => snapshot.SourceId)
            .ToArray();
    }
}
