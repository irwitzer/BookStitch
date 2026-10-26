namespace BookStitch.Services;

public sealed record PipelineProjectState(
    bool NeedsDiscImport,
    bool IsMp3Disc,
    bool IsAudioDisc,
    bool IsLocal);
