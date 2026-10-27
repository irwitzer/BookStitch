using System.IO;
using CoverImageDrop.Core.Inspection;

namespace BookStitch.Services;

public static class CoverInputSafety
{
    public const int MinUsableSquarePixels = 200;
    public const long MaxEncodedBytes = 32L * 1024 * 1024;
    public const int MaxDimension = 8192;
    public const long MaxPixels = 24_000_000;

    public static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new InvalidDataException(LocalizationText.Get("Cover.InputTooLarge"));
    }

    // Resource validation deliberately accepts legacy covers below the new import minimum.
    public static void Validate(Stream input)
    {
        if (input.Length <= 0 || input.Length > MaxEncodedBytes)
            throw new InvalidDataException(LocalizationText.Get("Cover.InputTooLarge"));
        using var bytes = new MemoryStream();
        input.Position = 0;
        input.CopyTo(bytes);
        var info = new CoverImageInspector().InspectBytesAsync(bytes.ToArray()).GetAwaiter().GetResult();
        if (!info.IsSuccess) throw new InvalidDataException(LocalizationText.Get("Cover.InputInvalid"));
        ValidateDimensions(info.PixelWidth, info.PixelHeight);
        input.Position = 0;
    }

    public static void ValidateFile(string path)
    {
        var file = new FileInfo(path);
        if (file.Length <= 0 || file.Length > MaxEncodedBytes)
            throw new InvalidDataException(LocalizationText.Get("Cover.InputTooLarge"));
        var info = new CoverImageInspector().InspectFileAsync(path).GetAwaiter().GetResult();
        if (!info.IsSuccess) throw new InvalidDataException(LocalizationText.Get("Cover.InputInvalid"));
        ValidateDimensions(info.PixelWidth, info.PixelHeight);
    }
}
