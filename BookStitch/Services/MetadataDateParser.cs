using System.Globalization;
using System.Text.RegularExpressions;
using BookStitch.Models;

namespace BookStitch.Services;

public static class MetadataDateParser
{
    public const string YearWatermark = "YYYY";
    public const string ReleaseDateWatermark = "YYYY.MM.DD";

    private static readonly Regex YearFirst = new(
        @"^(?<year>\d{4})(?<sep>[-./ ])(?<month>\d{2})(?:\k<sep>(?<day>\d{2}))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DayFirst = new(
        @"^(?<day>\d{2})(?<sep>[-./ ])(?<month>\d{2})\k<sep>(?<year>\d{4})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? value, out MetadataDate date)
    {
        date = default;
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return false;

        if (text.All(char.IsDigit))
        {
            if (text.Length == 4)
                return TryCreate(text[..4], null, null, out date);

            if (text.Length == 8)
            {
                if (TryCreate(text[..4], text.Substring(4, 2), text.Substring(6, 2), out date))
                    return true;

                return TryCreate(text.Substring(4, 4), text.Substring(2, 2), text[..2], out date);
            }

            return false;
        }

        var yearFirst = YearFirst.Match(text);
        if (yearFirst.Success)
        {
            return TryCreate(
                yearFirst.Groups["year"].Value,
                yearFirst.Groups["month"].Value,
                yearFirst.Groups["day"].Success ? yearFirst.Groups["day"].Value : null,
                out date);
        }

        var dayFirst = DayFirst.Match(text);
        return dayFirst.Success && TryCreate(
            dayFirst.Groups["year"].Value,
            dayFirst.Groups["month"].Value,
            dayFirst.Groups["day"].Value,
            out date);
    }

    public static string FormatDisplay(MetadataDate date)
    {
        if (date.Month is null)
            return date.Year.ToString("0000", CultureInfo.InvariantCulture);

        if (date.Day is null)
            return string.Create(CultureInfo.InvariantCulture, $"{date.Year:0000}.{date.Month.Value:00}");

        return string.Create(CultureInfo.InvariantCulture, $"{date.Year:0000}.{date.Month.Value:00}.{date.Day.Value:00}");
    }

    private static bool TryCreate(string yearText, string? monthText, string? dayText, out MetadataDate date)
    {
        date = default;
        if (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            return false;

        int? month = null;
        int? day = null;
        if (monthText is not null)
        {
            if (!int.TryParse(monthText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMonth))
                return false;
            month = parsedMonth;
        }

        if (dayText is not null)
        {
            if (!int.TryParse(dayText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedDay))
                return false;
            day = parsedDay;
        }

        try
        {
            date = new MetadataDate(year, month, day);
            return true;
        }
        catch (ArgumentException)
        {
            date = default;
            return false;
        }
    }
}
