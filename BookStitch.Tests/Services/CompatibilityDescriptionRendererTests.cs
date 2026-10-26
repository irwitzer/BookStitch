using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class CompatibilityDescriptionRendererTests
{
    [Fact]
    public void Render_ProducesCompleteBlockInStableOrder()
    {
        var block = CompatibilityDescriptionRenderer.Render(new FinalAudioTagData
        {
            Title = "Der Name des Windes",
            Album = "Der Name des Windes – Hörbuch",
            Authors = ["Patrick Rothfuss"],
            Series = [new SeriesEntry("Die Königsmörder-Chronik", "1")],
            Narrators = ["Stefan Kaminski", "Detlef Bierstedt"],
            Languages = ["Deutsch"],
            Duration = TimeSpan.FromHours(24) + TimeSpan.FromMinutes(17) + TimeSpan.FromSeconds(32),
            ChapterCount = 42,
            OutputPresentationCulture = "de-DE"
        });

        Assert.Equal(
            "• Titel: Der Name des Windes \n" +
            "• Album: Der Name des Windes – Hörbuch \n" +
            "• Autor: Patrick Rothfuss \n" +
            "• Reihe: Die Königsmörder-Chronik 1 \n" +
            "• Sprecher: Stefan Kaminski, Detlef Bierstedt \n" +
            "• Sprache: Deutsch \n" +
            "• Dauer: 24:17:32 \n" +
            "• Kapitel: 42",
            block);
    }

    [Fact]
    public void Render_OmitsAlbumWhenSemanticallyEqualToTitle()
    {
        var block = CompatibilityDescriptionRenderer.Render(new FinalAudioTagData
        {
            Title = "  Café   Noir ",
            Album = "CAFÉ NOIR",
            OutputPresentationCulture = "de-DE"
        });

        Assert.Contains("• Titel: Café   Noir", block);
        Assert.DoesNotContain("Album:", block);
    }

    [Fact]
    public void Render_OmitsEmptyOptionalLinesAndCombinesSeriesPart()
    {
        var block = CompatibilityDescriptionRenderer.Render(new FinalAudioTagData
        {
            Title = "Book",
            Series = [new SeriesEntry("Series", "3")],
            OutputPresentationCulture = "en-US"
        });

        Assert.Equal("• Title: Book \n• Series: Series 3", block);
        Assert.DoesNotContain("Series part:", block);
        Assert.DoesNotContain("Narrator:", block);
        Assert.DoesNotContain("Language:", block);
    }

    [Fact]
    public void Render_SeriesWithoutPartUsesSingleSeriesLine()
    {
        var block = CompatibilityDescriptionRenderer.Render(new FinalAudioTagData
        {
            Series = [new SeriesEntry("Series", "")],
            OutputPresentationCulture = "en-US"
        });

        Assert.Equal("• Series: Series", block);
    }

    [Fact]
    public void Render_NoSeriesOmitsSeriesLine()
    {
        var block = CompatibilityDescriptionRenderer.Render(new FinalAudioTagData
        {
            Title = "Book",
            OutputPresentationCulture = "en-US"
        });

        Assert.DoesNotContain("Series:", block);
    }

    [Fact]
    public void Render_IgnoresDescriptionEmbeddingToggle()
    {
        var block = CompatibilityDescriptionRenderer.Render(new FinalAudioTagData
        {
            Title = "Book",
            CompatibilityDescriptionEnabled = false,
            OutputPresentationCulture = "en-US"
        });

        Assert.Equal("• Title: Book", block);
    }

    [Fact]
    public void Render_FormatsDurationsLongerThan24HoursAndChapterCount()
    {
        var block = CompatibilityDescriptionRenderer.Render(new FinalAudioTagData
        {
            Duration = TimeSpan.FromHours(25) + TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(3),
            ChapterCount = 12,
            OutputPresentationCulture = "en-US"
        });

        Assert.Equal("• Duration: 25:02:03 \n• Chapters: 12", block);
    }


    [Fact]
    public void Render_SeparatesLinesWithSpaceBeforeLineFeedAndRemainsReadableWhenLineFeedsAreRemoved()
    {
        var block = CompatibilityDescriptionRenderer.Render(new FinalAudioTagData
        {
            Title = "Book",
            Authors = ["Author"],
            OutputPresentationCulture = "en-US"
        });

        Assert.Equal("• Title: Book \n• Author: Author", block);
        Assert.Contains(" \n• ", block, StringComparison.Ordinal);

        var flattened = block.Replace("\n", string.Empty, StringComparison.Ordinal);
        Assert.Equal("• Title: Book • Author: Author", flattened);
        Assert.DoesNotContain("Book•", flattened, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendToDescription_IsIdempotentForAlreadyComposedText()
    {
        var once = CompatibilityDescriptionRenderer.AppendToDescription("Description", "• Title: Book");
        var twice = CompatibilityDescriptionRenderer.AppendToDescription(once, "• Title: Book");

        Assert.Equal(once, twice);
    }

    [Theory]
    [InlineData("Description", "Description\n\n• Title: Book")]
    [InlineData("Description\n", "Description\n\n• Title: Book")]
    [InlineData("Description\n\n\n", "Description\n\n• Title: Book")]
    [InlineData("", "• Title: Book")]
    public void AppendToDescription_NormalizesTrailingWhitespace(string description, string expected)
    {
        Assert.Equal(expected, CompatibilityDescriptionRenderer.AppendToDescription(description, "• Title: Book"));
    }
    [Fact]
    public void Render_MultipleSeriesPreservesCanonicalOrderAndOmitsEmptyNames()
    {
        var block = CompatibilityDescriptionRenderer.Render(new FinalAudioTagData
        {
            Series =
            [
                new SeriesEntry("Archiv der Sterne", "1.7"),
                new SeriesEntry("Zwischenbände", "0.4"),
                new SeriesEntry("  ", "9"),
                new SeriesEntry("Chroniken des Nordens", "")
            ],
            OutputPresentationCulture = "de-DE"
        });

        Assert.Equal(
            "• Reihe: Archiv der Sterne 1.7 \n" +
            "• Reihe: Zwischenbände 0.4 \n" +
            "• Reihe: Chroniken des Nordens",
            block);
    }

    [Fact]
    public void Render_EveryEmittedLineStartsWithBulletAndNoEmptyBulletLineIsCreated()
    {
        var block = CompatibilityDescriptionRenderer.Render(new FinalAudioTagData
        {
            Title = "Book",
            Authors = ["Author"],
            Series = [new SeriesEntry("Series", "1")],
            Duration = TimeSpan.FromSeconds(2),
            ChapterCount = 3,
            OutputPresentationCulture = "en-US"
        });

        var lines = block.Split('\n');
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.StartsWith("• ", line, StringComparison.Ordinal));
        Assert.DoesNotContain("• ", lines);
    }

}
