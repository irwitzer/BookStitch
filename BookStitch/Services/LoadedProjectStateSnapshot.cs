namespace BookStitch.Services;

public enum LoadedProjectKind
{
    None,
    Mp3Disc,
    AudioDisc,
    Local
}

public sealed record LoadedProjectStateSnapshot
{
    public static LoadedProjectStateSnapshot None { get; } =
        new(false, LoadedProjectKind.None);

    public LoadedProjectStateSnapshot(
        bool NeedsDiscImport,
        bool IsMp3Disc,
        bool IsAudioDisc,
        bool IsLocal)
        : this(
            NeedsDiscImport,
            ResolveProjectKind(IsMp3Disc, IsAudioDisc, IsLocal))
    {
    }

    private LoadedProjectStateSnapshot(
        bool needsDiscImport,
        LoadedProjectKind projectKind)
    {
        NeedsDiscImport = needsDiscImport;
        ProjectKind = projectKind;
    }

    public bool NeedsDiscImport { get; }

    public LoadedProjectKind ProjectKind { get; }

    public bool IsMp3Disc =>
        ProjectKind == LoadedProjectKind.Mp3Disc;

    public bool IsAudioDisc =>
        ProjectKind == LoadedProjectKind.AudioDisc;

    public bool IsLocal =>
        ProjectKind == LoadedProjectKind.Local;

    public static LoadedProjectStateSnapshot Create(
        bool needsDiscImport,
        bool isMp3Disc,
        bool isAudioDisc,
        bool isLocal) =>
        new(
            needsDiscImport,
            isMp3Disc,
            isAudioDisc,
            isLocal);

    public bool HasLoadedProject =>
        NeedsDiscImport ||
        ProjectKind != LoadedProjectKind.None;

    public PipelineProjectState ToPipelineProjectState() =>
        new(
            NeedsDiscImport,
            IsMp3Disc,
            IsAudioDisc,
            IsLocal);

    public LoadedProjectStateSnapshot WithDiscImportRequirement(bool needsDiscImport) =>
        new(needsDiscImport, ProjectKind);

    public LoadedProjectStateSnapshot WithMp3DiscState(bool isMp3Disc) =>
        new(
            NeedsDiscImport,
            ResolveUpdatedProjectKind(
                LoadedProjectKind.Mp3Disc,
                isMp3Disc));

    public LoadedProjectStateSnapshot WithAudioDiscState(bool isAudioDisc) =>
        new(
            NeedsDiscImport,
            ResolveUpdatedProjectKind(
                LoadedProjectKind.AudioDisc,
                isAudioDisc));

    public LoadedProjectStateSnapshot WithLocalState(bool isLocal) =>
        new(
            NeedsDiscImport,
            ResolveUpdatedProjectKind(
                LoadedProjectKind.Local,
                isLocal));

    private LoadedProjectKind ResolveUpdatedProjectKind(
        LoadedProjectKind requestedKind,
        bool isActive) =>
        isActive
            ? requestedKind
            : ProjectKind == requestedKind
                ? LoadedProjectKind.None
                : ProjectKind;

    private static LoadedProjectKind ResolveProjectKind(
        bool isMp3Disc,
        bool isAudioDisc,
        bool isLocal)
    {
        int selectedTypeCount =
            (isMp3Disc ? 1 : 0) +
            (isAudioDisc ? 1 : 0) +
            (isLocal ? 1 : 0);

        if (selectedTypeCount > 1)
        {
            throw new ArgumentException(
                "A loaded project can have only one project type.");
        }

        if (isMp3Disc)
        {
            return LoadedProjectKind.Mp3Disc;
        }

        if (isAudioDisc)
        {
            return LoadedProjectKind.AudioDisc;
        }

        return isLocal
            ? LoadedProjectKind.Local
            : LoadedProjectKind.None;
    }
}
