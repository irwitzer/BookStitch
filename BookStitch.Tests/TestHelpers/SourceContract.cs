using Xunit;

namespace BookStitch.Tests.TestHelpers;

internal static class SourceContract
{
    public static string ExtractMember(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Member was not found: {signature}");

        var nextMember = new[] { "private", "public", "internal", "protected" }
            .Select(accessibility => source.IndexOf(
                $"\n    {accessibility} ",
                start + signature.Length,
                StringComparison.Ordinal))
            .Where(index => index >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        return nextMember >= 0 ? source[start..nextMember] : source[start..];
    }

    public static string ExtractBetween(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker was not found: {startMarker}");

        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker was not found after {startMarker}: {endMarker}");
        return source[start..end];
    }

    public static void AssertInOrder(string source, params string[] markers)
    {
        var previousIndex = -1;
        foreach (var marker in markers)
        {
            var index = source.IndexOf(marker, previousIndex + 1, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Expected marker was not found in order: {marker}");
            previousIndex = index;
        }
    }
}
