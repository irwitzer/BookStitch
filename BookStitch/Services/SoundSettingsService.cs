using BookStitch.Models;

namespace BookStitch.Services;

public static class SoundSettingsService
{
    public const int DefaultVolumePercent = 65;
    public const SoundLibrary DefaultLibrary = SoundLibrary.Bass;

    public static SoundLibrary NormalizeLibrary(string? value)
    {
        return Enum.TryParse<SoundLibrary>(value, ignoreCase: true, out var library) && Enum.IsDefined(library)
            ? library
            : DefaultLibrary;
    }

    public static int NormalizeVolumePercent(int value)
    {
        return Math.Clamp(value, 0, 100);
    }
}
