using System.Globalization;
using System.Text.RegularExpressions;

namespace BookStitch.Services;

public static partial class SeriesPartInputService
{
    public static string NormalizePartText(string? value)
    {
        var source = (value ?? string.Empty).Trim();
        if (source.Length == 0)
            return string.Empty;

        source = DigitComma().Replace(source, ".");
        return TryNormalizeNumericCore(source, out var normalized)
            ? normalized
            : source;
    }

    public static bool TryNormalizeNumeric(string? value, out string normalized) =>
        TryNormalizeNumericCore(NormalizeDigitCommas((value ?? string.Empty).Trim()), out normalized);


    private static bool TryNormalizeNumericCore(string source, out string normalized)
    {
        normalized = string.Empty;
        if (source.Length == 0 ||
            !decimal.TryParse(
                source,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < 0)
        {
            return false;
        }

        normalized = Format(parsed);
        return true;
    }

    private static string NormalizeDigitCommas(string source) =>
        source.Length == 0 ? source : DigitComma().Replace(source, ".");

    private static string Format(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?<=\d),(?=\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DigitComma();
}
