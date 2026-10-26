using BookStitch.Models;

namespace BookStitch.Services;

public sealed record MetadataDebugSnapshot(
    IReadOnlyList<MetadataFieldDecisionDiagnostic> Fields);

/// <summary>
/// Builds a read-only view over canonical metadata and the diagnosis emitted by
/// the real aggregation decision. It never performs metadata aggregation itself.
/// </summary>
public sealed class MetadataDebugPresentationService
{
    private static readonly ProjectMetadataField[] DebugFields =
    [
        ProjectMetadataField.Title,
        ProjectMetadataField.Album,
        ProjectMetadataField.Authors,
        ProjectMetadataField.Narrators
    ];

    public MetadataDebugSnapshot Create(
        ProjectMetadata metadata,
        IReadOnlyList<MetadataFieldDecisionDiagnostic>? latestDecisions)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        latestDecisions ??= [];

        var fields = DebugFields
            .Select(field => CreateField(metadata, field, latestDecisions))
            .ToArray();
        return new MetadataDebugSnapshot(Array.AsReadOnly(fields));
    }

    private static MetadataFieldDecisionDiagnostic CreateField(
        ProjectMetadata metadata,
        ProjectMetadataField field,
        IReadOnlyList<MetadataFieldDecisionDiagnostic> latestDecisions)
    {
        var (value, origin) = ReadCanonical(metadata, field);
        var previous = latestDecisions.FirstOrDefault(item => item.Field == field);
        if (previous is not null &&
            string.Equals(previous.CanonicalValue, value, StringComparison.Ordinal) &&
            previous.Origin == origin)
        {
            return previous;
        }

        if (origin == MetadataOrigin.User)
        {
            var runnerUp = previous?.Winner is null
                ? previous?.RunnerUp
                : previous.Winner with
                {
                    RejectedReason = "Durch kanonischen Benutzerwert geschützt.",
                    RunnerUp = null
                };
            return new MetadataFieldDecisionDiagnostic(
                field,
                value,
                origin,
                "Kanonischer Benutzerwert hat Vorrang vor automatischer Metadatenaggregation.",
                new MetadataDecisionCandidateDiagnostic(
                    value,
                    MetadataDecisionEvidenceType.UserOverride,
                    null,
                    null,
                    null),
                runnerUp);
        }

        return new MetadataFieldDecisionDiagnostic(
            field,
            value,
            origin,
            value.Length == 0
                ? "Kein kanonischer Wert vorhanden."
                : "Aktueller kanonischer Projektwert; für diese Sitzung liegt keine neuere Aggregationsdiagnose vor.",
            value.Length == 0
                ? null
                : new MetadataDecisionCandidateDiagnostic(
                    value,
                    EvidenceTypeForOrigin(origin),
                    null,
                    null,
                    null),
            null);
    }

    private static (string Value, MetadataOrigin Origin) ReadCanonical(
        ProjectMetadata metadata,
        ProjectMetadataField field)
    {
        if (ProjectMetadataFields.GetValueKind(field) == ProjectMetadataFieldValueKind.String)
        {
            var scalar = metadata.GetValue<string>(field);
            return (scalar.Value?.Trim() ?? string.Empty, scalar.Origin);
        }

        var list = metadata.GetValue<IReadOnlyList<string>>(field);
        return (list.Value is null ? string.Empty : string.Join("; ", list.Value), list.Origin);
    }

    private static MetadataDecisionEvidenceType EvidenceTypeForOrigin(MetadataOrigin origin) => origin switch
    {
        MetadataOrigin.User => MetadataDecisionEvidenceType.UserOverride,
        MetadataOrigin.FolderStructure => MetadataDecisionEvidenceType.FolderStructure,
        MetadataOrigin.SingleFileName => MetadataDecisionEvidenceType.SingleFileName,
        MetadataOrigin.Derived => MetadataDecisionEvidenceType.FilenameAggregationFallback,
        _ => MetadataDecisionEvidenceType.ExistingCanonical
    };
}
