using CoverImageDrop.Pipeline;

namespace BookStitch.Services;

public static class CoverTransparencySettingsService
{
    public const string DefaultMode = "Auto";

    public static string NormalizeMode(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "WHITE" => "White",
        "BLACK" => "Black",
        _ => DefaultMode
    };

    public static JpegTransparencyMode ResolveMode(string? value) => NormalizeMode(value) switch
    {
        "White" => JpegTransparencyMode.White,
        "Black" => JpegTransparencyMode.Black,
        _ => JpegTransparencyMode.Auto
    };
}
