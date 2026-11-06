using System.Collections.Generic;
using System.Linq;

namespace BookStitch.Services;

// Used only when external tag evidence enters the canonical source snapshot.
internal static class ExternalMetadataListParser
{
    public static IReadOnlyList<string> Parse(IEnumerable<string>? values, bool allowSlash)
    {
        var result = new List<string>();
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrEmpty(value)) continue;
            foreach (var segment in value.Split(';'))
            {
                var start = 0;
                for (var index = 0; allowSlash && index < segment.Length; index++)
                {
                    if (segment[index] != '/' ||
                        !(index > 0 && char.IsWhiteSpace(segment[index - 1]) ||
                          index + 1 < segment.Length && char.IsWhiteSpace(segment[index + 1])) ||
                        string.IsNullOrWhiteSpace(segment[..index]) ||
                        string.IsNullOrWhiteSpace(segment[(index + 1)..]))
                        continue;
                    var item = segment[start..index].Trim();
                    if (item.Length > 0) result.Add(item);
                    start = index + 1;
                }
                var last = segment[start..].Trim();
                if (last.Length > 0) result.Add(last);
            }
        }
        return result.Distinct(System.StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
