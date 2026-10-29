using System.Text.RegularExpressions;

namespace BookStitch.Services;

public sealed record TitleNarratorRecognition(string Title, IReadOnlyList<string> Narrators);

/// <summary>Conservative terminal credits; never modifies source metadata.</summary>
public sealed class TitleNarratorRecognitionService
{
    private static readonly Regex Credit = new(
        @"^(?<title>.+?)(?:\s*\((?<credit>(?:gelesen von|gesprochen von|read by|narrated by)\s+[^()]+)\)|(?:\s+|\s*[-–—:]\s*)(?<credit>(?:gelesen von|gesprochen von|read by|narrated by)\s+[^()]+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Marker = new(@"^(?:gelesen von|gesprochen von|read by|narrated by)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Separator = new(@"\s+(?:und|and|&)\s+|\s*;\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NameWord = new(@"^(?:\p{Lu}[\p{L}\p{M}'’\-]*\.?|[\p{Lo}\p{M}]+|von|van|de|del|der|den|da|dos|du|la|le|di|della|al|bin)$", RegexOptions.CultureInvariant);

    public TitleNarratorRecognition? Recognize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096) return null;
        var match = Credit.Match(value.Trim());
        if (!match.Success) return null;
        var title = match.Groups["title"].Value.TrimEnd();
        title = Regex.Replace(title, @"\s*[-–—:]\s*$", "").TrimEnd();
        if (title.Length == 0 || title.Contains('(') != title.Contains(')')) return null;
        var names = Separator.Split(Marker.Replace(match.Groups["credit"].Value, ""))
            .Select(name => Regex.Replace(name.Trim(), @"\s+", " ")).ToArray();
        if (names.Length == 0 || names.Any(name =>
        {
            var words = name.Split(' ');
            return words.Length is < 2 or > 6 || !words.All(word => NameWord.IsMatch(word)) ||
                words.Count(word => char.IsUpper(word[0]) || char.GetUnicodeCategory(word[0]) == System.Globalization.UnicodeCategory.OtherLetter) < 2;
        })) return null;
        return new(title, names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
