using BookStitch.Models;

namespace BookStitch.Services;

public static class AudioDiscSettingsService
{
    public const AudioDiscWorkingFormat DefaultWorkingFormat = AudioDiscWorkingFormat.Flac;

    public static AudioDiscWorkingFormat NormalizeWorkingFormat(string? value)
    {
        return Enum.TryParse<AudioDiscWorkingFormat>(value, ignoreCase: true, out var format) &&
               format == DefaultWorkingFormat
            ? format
            : DefaultWorkingFormat;
    }
}
