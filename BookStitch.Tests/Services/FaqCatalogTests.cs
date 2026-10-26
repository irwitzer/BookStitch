using System.Text.RegularExpressions;
using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FaqCatalogTests
{
    [Fact]
    public void Ten_entries_preserve_approved_content_and_follow_the_requested_order()
    {
        using var stream = typeof(FaqCatalogTests).Assembly.GetManifestResourceStream("BookStitch.Tests.ApprovedFaqContent.md")!;
        using var reader = new StreamReader(stream);
        var markdown = reader.ReadToEnd().Replace("\r\n", "\n");
        var approved = Regex.Matches(markdown, @"(?m)^## ([1-7])\. (.+)\n");
        var entries = FaqCatalog.LoadEnglish();
        Assert.Equal(10, entries.Count);
        Assert.Equal(new[] { "ffmpeg-wrapper", "living", "artificial-intelligence", "help", "acronym", "icon-design", "nightly-build", "criticism", "audible-drm", "faq-count" }, entries.Select(entry => entry.Id));
        Assert.Equal(entries.Count, entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
        for (var index = 0; index < 6; index++)
        {
            var start = approved[index].Index + approved[index].Length;
            var end = approved[index + 1].Index;
            var body = markdown[start..end].Split("**Bild unter")[0].Split("\n---")[0].Trim();
            var entry = entries.Single(entry => entry.Question == approved[index].Groups[2].Value);
            Assert.Equal(body.Split("\n\n"), entry.Sections.OfType<FaqParagraph>().Select(section => section.Text));
            Assert.Null(entry.Chat);
        }
        Assert.Equal(3, entries.SelectMany(entry => entry.Sections).OfType<FaqImage>().Count());
        var chat = entries.Single(entry => entry.Id == "icon-design");
        Assert.NotNull(chat.Chat);
        Assert.Empty(chat.Sections);
        var chatText = Regex.Split(markdown[approved[6].Index..], @"(?m)^### (?:Human|ChatGPT) [1-4]\n").Skip(1)
            .Select(text => text.Split("\n**Bild:")[0].Split("\n---")[0].Trim());
        Assert.Equal(chatText, chat.Chat!.Turns.SelectMany(turn => new[] { turn.Human, turn.Assistant }));
    }
    [Fact]
    public void Three_new_answers_keep_the_supplied_wording_and_cake_after_the_last_paragraph()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["criticism"] = ["That's not a question.", "Other programs have larger development teams, professional designers, extensive documentation, and actual budgets.", "BookStitch has one developer, a garden, a construction trailer, and a rather unhealthy relationship with FFmpeg.", "Frankly, I'm surprised it works at all."],
            ["faq-count"] = ["There are twelve.", "The AI has personally verified this.", "We strongly recommend that you do not count them yourself."],
        };
        var entries = FaqCatalog.LoadEnglish();
        foreach (var (id, paragraphs) in expected)
            Assert.Equal(paragraphs, entries.Single(entry => entry.Id == id).Sections.OfType<FaqParagraph>().Select(section => section.Text));
        var audible = entries.Single(entry => entry.Id == "audible-drm");
        var cake = Assert.IsType<FaqImage>(audible.Sections.Last());
        Assert.EndsWith("audible-cake-optimiert.jpg", cake.Source);
        Assert.Equal(cake.Source, audible.SidebarImage!.Source);
        Assert.True(cake.MatchQuestionWidth);
        Assert.Equal(28, cake.CornerRadius);
        Assert.NotNull(audible.Narration);
        Assert.Equal(1.0, audible.SidebarImage!.Zoom);
        var acronym = entries.Single(entry => entry.Id == "acronym");
        Assert.EndsWith("BookMachin Bauplan.png", acronym.SidebarImage!.Source);
        Assert.Equal(acronym.SidebarImage.Source, Assert.Single(acronym.Sections.OfType<FaqImage>()).Source);
        Assert.Equal(1, acronym.SidebarImage.Zoom);
        foreach (var entry in entries.Where(entry => entry.Id is not "acronym" and not "audible-drm"))
            Assert.EndsWith("BookMachin Bauplan.png", entry.SidebarImage!.Source);
    }

}
