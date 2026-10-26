using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Memory;

namespace BookStitch.Services;

public static class CoverInputSafety
{
    public const long MaxEncodedBytes = 32L * 1024 * 1024;
    public const int MaxDimension = 8192;
    public const long MaxPixels = 24_000_000;

    public static DecoderOptions DecoderOptions { get; } = CreateOptions();

    private static DecoderOptions CreateOptions()
    {
        var configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions { AllocationLimitMegabytes = 192 });
        return new DecoderOptions { Configuration = configuration, MaxFrames = 1 };
    }

    public static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new InvalidDataException(LocalizationText.Get("Cover.InputTooLarge"));
    }

    public static void Validate(Stream input)
    {
        if (input.Length <= 0 || input.Length > MaxEncodedBytes)
            throw new InvalidDataException(LocalizationText.Get("Cover.InputTooLarge"));
        var info = Image.Identify(DecoderOptions, input);
        ValidateDimensions(info.Width, info.Height);
        input.Position = 0;
    }

    public static void ValidateFile(string path)
    {
        using var input = File.OpenRead(path);
        Validate(input);
    }
}
