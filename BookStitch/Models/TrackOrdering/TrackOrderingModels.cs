namespace BookStitch.Models.TrackOrdering;

public sealed record AutomaticTrackOrderRequest(
    TrackOrderProjectType ProjectType,
    IReadOnlyList<TrackOrderInput> Tracks);

public enum TrackOrderProjectType
{
    LocalFolder,
    Mp3Disc,
    AudioDisc
}

public sealed record TrackOrderInput(
    string TrackKey,
    int DeterministicInputPosition,
    string FullPath,
    string FileName,
    string RelativeFolder,
    OriginalOrderMetadata Metadata,
    ConfirmedOrderContext? ConfirmedContext);

public sealed record OriginalOrderMetadata(
    int? TrackNumber,
    int? TrackTotal,
    int? DiscNumber,
    int? DiscTotal);

public sealed record ConfirmedOrderContext(
    int? DiscNumber,
    int? TrackNumber,
    int? GlobalPosition);

public sealed record TrackOrderCandidate(
    TrackOrderCandidateKind Kind,
    int Value,
    TrackOrderEvidenceSource Source,
    TrackOrderPattern Pattern,
    int? TokenIndex,
    string RawText);

public sealed record TrackOrderCandidateSet(
    string TrackKey,
    IReadOnlyList<TrackOrderCandidate> Candidates);

public enum TrackOrderCandidateKind
{
    Disc,
    Track,
    Sequence
}

public enum TrackOrderEvidenceSource
{
    ConfirmedImportContext,
    Metadata,
    FolderName,
    FileName
}

public enum TrackOrderPattern
{
    ConfirmedDisc,
    ConfirmedTrack,
    ConfirmedGlobalPosition,
    MetadataDisc,
    MetadataTrack,
    ExplicitDiscMarker,
    ExplicitTrackMarker,
    NumericFolderName,
    LeadingNumericToken,
    GenericNumericToken,
    HierarchicalNumericToken,
    AlphaSuffixNumericToken
}

public sealed record AutomaticTrackOrderResult(
    IReadOnlyList<AutomaticTrackOrderItemResult> Tracks,
    TrackOrderConfidence OverallConfidence,
    IReadOnlyList<TrackOrderDiagnostic> Diagnostics);

public sealed record AutomaticTrackOrderItemResult(
    string TrackKey,
    int AutomaticOrderPosition,
    TrackOrderConfidence OrderConfidence);

public enum TrackOrderConfidence
{
    Unresolved,
    Low,
    Medium,
    High,
    VeryHigh
}

public sealed record TrackOrderDiagnostic(
    TrackOrderDiagnosticCode Code,
    TrackOrderDiagnosticSeverity Severity,
    string? TrackKey,
    string Detail);

public enum TrackOrderDiagnosticCode
{
    DiscSequenceGap,
    ConflictingDiscOrder,
    AmbiguousDiscAssignment,
    DuplicateTrackNumber,
    ConflictingTrackOrder,
    AmbiguousTrackOrder,
    InvalidTrackTotal,
    InvalidDiscTotal,
    AmbiguousFilenameNumbers,
    FallbackOrderUsed,
    LowOrderConfidence
}

public enum TrackOrderDiagnosticSeverity
{
    Info,
    Warning,
    Error
}
