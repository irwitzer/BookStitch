using CoverImageDrop.Core.Naming;
using CoverImageDrop.Pipeline.Persistence;

namespace CoverImageDrop.Pipeline.PassThrough;

public sealed record CoverImagePassThroughOptions
{
    public CoverImageNamingOptions Naming { get; init; } = new();

    public string? TargetFolder { get; init; }

    public string? TargetFilePath { get; init; }

    public CoverImageCollisionPolicy CollisionPolicy { get; init; } = CoverImageCollisionPolicy.GenerateUniqueName;
}
