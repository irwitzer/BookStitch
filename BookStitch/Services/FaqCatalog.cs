using System.Text.Json;
using BookStitch.Models;

namespace BookStitch.Services;

public static class FaqCatalog
{
    // Deliberately English, independent of the application language. A later FAQ
    // language selector can choose a different resource here.
    public static IReadOnlyList<FaqEntry> LoadEnglish()
    {
        using var stream = typeof(FaqCatalog).Assembly.GetManifestResourceStream("BookStitch.Resources.Faq.en.json")
            ?? throw new InvalidOperationException("The embedded English FAQ resource is missing.");
        return JsonSerializer.Deserialize<FaqEntry[]>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("The embedded English FAQ resource is empty.");
    }
}
