namespace BookStitch.Models;

public static class ProcessingActionIds
{
    public const string Pending = "Pending";
    public const string Reuse = "Reuse";
    public const string Convert = "Convert";
    public const string Inspect = "Inspect";
    public const string Invalid = "Invalid";
    public const string RipFlac = "RipFlac";

    public static string NormalizeOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Normalize(value);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Pending;

        return value.Trim().ToUpperInvariant() switch
        {
            "PENDING" or "OFFEN" => Pending,
            "REUSE" or "ÜBERNEHMEN" or "UEBERNEHMEN" => Reuse,
            "CONVERT" or "KONVERTIEREN" => Convert,
            "INSPECT" or "PRÜFEN" or "PRUEFEN" => Inspect,
            "INVALID" or "UNGÜLTIG" or "UNGUELTIG" => Invalid,
            "RIPFLAC" or "FLAC RIPPEN" => RipFlac,
            _ => value.Trim()
        };
    }
}
