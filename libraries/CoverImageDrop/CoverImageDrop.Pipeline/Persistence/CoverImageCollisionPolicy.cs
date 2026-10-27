namespace CoverImageDrop.Pipeline.Persistence;

public enum CoverImageCollisionPolicy
{
    GenerateUniqueName,
    Overwrite,
    FailIfExists
}