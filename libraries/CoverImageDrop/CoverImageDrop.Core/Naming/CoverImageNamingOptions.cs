namespace CoverImageDrop.Core.Naming;

public sealed record CoverImageNamingOptions
{
    public CoverImageNamingStrategy Strategy { get; init; } = CoverImageNamingStrategy.Timestamp;

    public string? HostProvidedName { get; init; }

    public string? OriginalFileName { get; init; }
}
