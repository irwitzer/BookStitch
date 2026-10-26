namespace BookStitch.Models;

public static class ConceptMetadataGroupIds
{
    public const string QuickAccess = "quick";
    public const string Work = "work-output";
    public const string People = "people";
    public const string Publication = "publication";
    public const string Language = "language";
    public const string CatalogData = "catalog-data";
    public const string Sorting = "sorting";
    public const string Description = "description-group";
    public const string Compatibility = "compatibility";
    public const string TechnicalData = "technical-data";
    public const string PassThrough = "pass-through";
    public const string Chapter = "chapter";
    public const string Cover = "cover";
    public const string Help = "help";

    public static IReadOnlyList<string> All { get; } =
    [
        QuickAccess,
        Work,
        People,
        Publication,
        Language,
        CatalogData,
        Sorting,
        Description,
        Compatibility,
        Cover,
        Chapter,
        PassThrough,
        Help
    ];
}
