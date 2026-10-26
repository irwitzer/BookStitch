namespace BookStitch.Services;

public enum AppearanceAssetRole
{
    CoverPlaceholder,
    BrandLogo,
    MetadataBanner,
    HeaderBrand,
    FaqSquare,
    InfoSquare
}

public sealed record AppearanceAssetDefinition(
    AppearanceAssetRole Role,
    AppearanceMode Mode,
    string RelativePath,
    string PackUriString);

public sealed record AppearanceAssetVariant(
    string Id, AppearanceAssetRole Role, AppearanceMode? Theme, string RelativePath, string? LabelKey = null, bool IsText = false)
{
    public bool Matches(AppearanceMode mode) => Theme is null || Theme == mode;
}

public static class AppearanceAssetCatalog
{
    private const string BrandLogoRelativePath = "/BookStitch;component/Assets/Icons/icon/BookStitch-Icon-Feather.ico";
    private const string BrandLogoPackUriString =
        "pack://application:,,,/BookStitch;component/Assets/Icons/icon/BookStitch-Icon-Feather.ico";
    private const string DarkCoverRelativePath = "/BookStitch;component/Assets/Icons/Placeholder/CoverPlacHolder Old (1).png";
    private const string DarkCoverPackUriString =
        "pack://application:,,,/BookStitch;component/Assets/Icons/Placeholder/CoverPlacHolder Old (1).png";
    private const string LightCoverRelativePath = "/BookStitch;component/Assets/Icons/Placeholder/CoverPlacHolder Old (2).png";
    private const string LightCoverPackUriString =
        "pack://application:,,,/BookStitch;component/Assets/Icons/Placeholder/CoverPlacHolder Old (2).png";

    private static readonly IReadOnlyDictionary<(AppearanceAssetRole Role, AppearanceMode Mode), AppearanceAssetDefinition> Assets =
        new Dictionary<(AppearanceAssetRole, AppearanceMode), AppearanceAssetDefinition>
        {
            [(AppearanceAssetRole.FaqSquare, AppearanceMode.Dark)] = new(AppearanceAssetRole.FaqSquare, AppearanceMode.Dark,
                "/BookStitch;component/Assets/Icons/BookStitchBrandLogo-Light.png", "pack://application:,,,/BookStitch;component/Assets/Icons/BookStitchBrandLogo-Light.png"),
            [(AppearanceAssetRole.FaqSquare, AppearanceMode.Light)] = new(AppearanceAssetRole.FaqSquare, AppearanceMode.Light,
                "/BookStitch;component/Assets/Icons/BookStitchBrandLogo-Light.png", "pack://application:,,,/BookStitch;component/Assets/Icons/BookStitchBrandLogo-Light.png"),
            [(AppearanceAssetRole.InfoSquare, AppearanceMode.Dark)] = new(AppearanceAssetRole.InfoSquare, AppearanceMode.Dark,
                "/BookStitch;component/Assets/Faq/BookMachin Press.png", "pack://application:,,,/BookStitch;component/Assets/Faq/BookMachin Press.png"),
            [(AppearanceAssetRole.InfoSquare, AppearanceMode.Light)] = new(AppearanceAssetRole.InfoSquare, AppearanceMode.Light,
                "/BookStitch;component/Assets/Faq/BookMachin Press.png", "pack://application:,,,/BookStitch;component/Assets/Faq/BookMachin Press.png"),
            [(AppearanceAssetRole.HeaderBrand, AppearanceMode.Dark)] = new(
                AppearanceAssetRole.HeaderBrand, AppearanceMode.Dark,
                "/BookStitch;component/Assets/Banners/HeaderBrand-Ornates.png", "pack://application:,,,/BookStitch;component/Assets/Banners/HeaderBrand-Ornates.png"),
            [(AppearanceAssetRole.HeaderBrand, AppearanceMode.Light)] = new(
                AppearanceAssetRole.HeaderBrand, AppearanceMode.Light,
                "/BookStitch;component/Assets/Banners/HeaderBrand-Ornates-Ivory.png", "pack://application:,,,/BookStitch;component/Assets/Banners/HeaderBrand-Ornates-Ivory.png"),
            [(AppearanceAssetRole.MetadataBanner, AppearanceMode.Dark)] = new(
                AppearanceAssetRole.MetadataBanner,
                AppearanceMode.Dark,
                "/BookStitch;component/Assets/Banners/MetadataBanner-Celestial-Dark.png",
                "pack://application:,,,/BookStitch;component/Assets/Banners/MetadataBanner-Celestial-Dark.png"),
            [(AppearanceAssetRole.MetadataBanner, AppearanceMode.Light)] = new(
                AppearanceAssetRole.MetadataBanner,
                AppearanceMode.Light,
                "/BookStitch;component/Assets/Banners/MetadataBanner-Celestial-Ivory.png",
                "pack://application:,,,/BookStitch;component/Assets/Banners/MetadataBanner-Celestial-Ivory.png"),
            [(AppearanceAssetRole.CoverPlaceholder, AppearanceMode.Dark)] = new(
                AppearanceAssetRole.CoverPlaceholder,
                AppearanceMode.Dark,
                DarkCoverRelativePath,
                DarkCoverPackUriString),
            [(AppearanceAssetRole.CoverPlaceholder, AppearanceMode.Light)] = new(
                AppearanceAssetRole.CoverPlaceholder,
                AppearanceMode.Light,
                LightCoverRelativePath,
                LightCoverPackUriString),
            [(AppearanceAssetRole.BrandLogo, AppearanceMode.Dark)] = new(
                AppearanceAssetRole.BrandLogo,
                AppearanceMode.Dark,
                BrandLogoRelativePath,
                BrandLogoPackUriString),
            [(AppearanceAssetRole.BrandLogo, AppearanceMode.Light)] = new(
                AppearanceAssetRole.BrandLogo,
                AppearanceMode.Light,
                BrandLogoRelativePath,
                BrandLogoPackUriString)
        };

    // Register new embedded variants here. The generic developer selectors use this one catalog.
    public static IReadOnlyList<AppearanceAssetVariant> Variants { get; } =
        Assets.Values.Where(asset => asset.Role is AppearanceAssetRole.HeaderBrand or AppearanceAssetRole.MetadataBanner or AppearanceAssetRole.CoverPlaceholder)
            .Select(asset => new AppearanceAssetVariant($"{asset.Role}.{asset.Mode}", asset.Role, asset.Mode, asset.RelativePath, asset.Role switch { AppearanceAssetRole.HeaderBrand => "Developer.Images.Ornates", AppearanceAssetRole.MetadataBanner => "Developer.Images.Celestial", AppearanceAssetRole.CoverPlaceholder => "Developer.Images.CoverOld", _ => null }))
            .Concat(new[]
            {
                new AppearanceAssetVariant("HeaderBrand.Text", AppearanceAssetRole.HeaderBrand, null,
                    "", "Developer.Images.Text", IsText: true),
                new AppearanceAssetVariant("FaqSquare.Blueprint", AppearanceAssetRole.FaqSquare, null,
                    "/BookStitch;component/Assets/Faq/BookMachin Bauplan.png", "Developer.Images.Blueprint"),
                new AppearanceAssetVariant("FaqSquare.Brand", AppearanceAssetRole.FaqSquare, null,
                    "/BookStitch;component/Assets/Icons/BookStitchBrandLogo-Light.png", "Developer.Images.Brand"),
                new AppearanceAssetVariant("FaqSquare.Cake", AppearanceAssetRole.FaqSquare, null,
                    "/BookStitch;component/Assets/Faq/audible-cake-optimiert.jpg", "Developer.Images.Cake"),
                new AppearanceAssetVariant("InfoSquare.Brand", AppearanceAssetRole.InfoSquare, null,
                    "/BookStitch;component/Assets/Icons/BookStitchBrandLogo-Light.png", "Developer.Images.Brand"),
                new AppearanceAssetVariant("InfoSquare.Blueprint", AppearanceAssetRole.InfoSquare, null,
                    "/BookStitch;component/Assets/Faq/BookMachin Bauplan.png", "Developer.Images.Blueprint")
            }).ToArray();

    public static IReadOnlyList<AppearanceAssetRole> PreviewRoles { get; } =
        [AppearanceAssetRole.HeaderBrand, AppearanceAssetRole.MetadataBanner, AppearanceAssetRole.CoverPlaceholder,
         AppearanceAssetRole.FaqSquare, AppearanceAssetRole.InfoSquare];

    public static AppearanceAssetVariant? FindVariant(AppearanceAssetRole role, string? id) =>
        Variants.FirstOrDefault(variant => variant.Role == role && string.Equals(variant.Id, id, StringComparison.Ordinal));

    public static AppearanceAssetDefinition Get(AppearanceAssetRole role, AppearanceMode mode) =>
        Assets.TryGetValue((role, mode), out var asset)
            ? asset
            : Assets[(role, AppearanceMode.Dark)];

    public static AppearanceAssetDefinition? GetOptional(AppearanceAssetRole role, AppearanceMode mode) =>
        Assets.GetValueOrDefault((role, mode));

    public static bool IsCoverPlaceholderPath(string? path) =>
        IsRolePath(AppearanceAssetRole.CoverPlaceholder, path);

    private static bool IsRolePath(AppearanceAssetRole role, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return Assets.Values.Where(asset => asset.Role == role)
            .SelectMany(asset => new[] { asset.RelativePath, asset.PackUriString })
            .Concat(Variants.Where(variant => variant.Role == role)
                .SelectMany(variant => new[] { variant.RelativePath, "pack://application:,,," + variant.RelativePath }))
            .Any(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
    }
}
