namespace BookStitch.Models;

public static class CodecIds
{
    public const string Invalid = "Invalid";

    public static bool IsInvalid(string? value) =>
        string.Equals(value, Invalid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "Ungültig", StringComparison.OrdinalIgnoreCase);
}
