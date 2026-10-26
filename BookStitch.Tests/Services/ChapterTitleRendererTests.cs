using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterTitleRendererTests
{
    private const string FigureSpace = "\u2007";

    private readonly ChapterTitleRenderer _renderer = new();

    [Fact]
    public void Render_Plain_ReplacesExistingLeadingNumber()
    {
        var result = _renderer.Render(12, 436, "001 Titel", ChapterNumberingStyle.Plain);

        Assert.Equal("12 Titel", result);
    }

    [Theory]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(99, 2)]
    [InlineData(100, 3)]
    [InlineData(999, 3)]
    [InlineData(1000, 4)]
    public void CalculateNumberWidth_UsesOnlyRequiredDecimalWidth(int activeChapterCount, int expectedWidth)
    {
        var result = ChapterTitleRenderer.CalculateNumberWidth(activeChapterCount);

        Assert.Equal(expectedWidth, result);
    }

    [Theory]
    [InlineData(1, 9, "1 Titel")]
    [InlineData(1, 10, "01 Titel")]
    [InlineData(1, 100, "001 Titel")]
    [InlineData(1, 1000, "0001 Titel")]
    public void Render_ZeroPadded_UsesDynamicActiveChapterCountWidth(
        int position,
        int activeChapterCount,
        string expected)
    {
        var result = _renderer.RenderContent(
            position,
            activeChapterCount,
            "Titel",
            ChapterNumberingStyle.ZeroPadded);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Render_SpacePadded_UsesActiveChapterCountWidth()
    {
        var first = _renderer.Render(1, 436, "001 Titel", ChapterNumberingStyle.SpacePadded);
        var second = _renderer.Render(12, 436, "012 Titel", ChapterNumberingStyle.SpacePadded);
        var third = _renderer.Render(436, 436, "436 Titel", ChapterNumberingStyle.SpacePadded);

        Assert.Equal($"1{FigureSpace}{FigureSpace} Titel", first);
        Assert.Equal($"12{FigureSpace} Titel", second);
        Assert.Equal("436 Titel", third);
    }

    [Theory]
    [InlineData(1, 9, "1 Titel")]
    [InlineData(1, 10, "1\u2007 Titel")]
    [InlineData(9, 10, "9\u2007 Titel")]
    [InlineData(10, 10, "10 Titel")]
    [InlineData(1, 99, "1\u2007 Titel")]
    [InlineData(12, 99, "12 Titel")]
    [InlineData(99, 99, "99 Titel")]
    [InlineData(1, 100, "1\u2007\u2007 Titel")]
    [InlineData(12, 100, "12\u2007 Titel")]
    [InlineData(100, 100, "100 Titel")]
    [InlineData(123, 999, "123 Titel")]
    [InlineData(1, 1000, "1\u2007\u2007\u2007 Titel")]
    [InlineData(12, 1000, "12\u2007\u2007 Titel")]
    [InlineData(123, 1000, "123\u2007 Titel")]
    [InlineData(984, 1000, "984\u2007 Titel")]
    [InlineData(999, 1000, "999\u2007 Titel")]
    [InlineData(1000, 1000, "1000 Titel")]
    public void Render_SpacePadded_AlignsTextStartWithinProject(
        int position,
        int activeChapterCount,
        string expected)
    {
        var result = _renderer.RenderContent(
            position,
            activeChapterCount,
            "Titel",
            ChapterNumberingStyle.SpacePadded);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Render_SpacePadded_UsesFigureSpacesOnlyForPaddingAndKeepsSeparatorSpace()
    {
        var result = _renderer.RenderContent(
            1,
            100,
            "Titel",
            ChapterNumberingStyle.SpacePadded);

        Assert.Equal($"1{FigureSpace}{FigureSpace} Titel", result);
        Assert.Equal('\u2007', result[1]);
        Assert.Equal('\u2007', result[2]);
        Assert.Equal(' ', result[3]);
    }

    [Fact]
    public void Render_None_RemovesExistingLeadingNumber()
    {
        var result = _renderer.Render(1, 10, "001 Titel", ChapterNumberingStyle.None);

        Assert.Equal("Titel", result);
    }

    [Fact]
    public void Render_KeepsUnnumberedTitleForNumberingStyles()
    {
        var result = _renderer.Render(1, 10, "Prolog", ChapterNumberingStyle.ZeroPadded);

        Assert.Equal("Prolog", result);
    }

    [Theory]
    [InlineData("001 Titel", true, "Titel")]
    [InlineData("1 Titel", true, "Titel")]
    [InlineData("0001 Titel", true, "Titel")]
    [InlineData("001 - Titel", true, "- Titel")]
    [InlineData("001. Titel", false, "001. Titel")]
    [InlineData("001- Titel", false, "001- Titel")]
    [InlineData("1984", false, "1984")]
    [InlineData("1984 Roman", true, "Roman")]
    [InlineData("2001: Odyssee im Weltraum", false, "2001: Odyssee im Weltraum")]
    [InlineData("12 Angry Men", true, "Angry Men")]
    public void TryGetTitleWithoutNumber_CharacterizesLeadingNumberDetection(
        string title,
        bool expectedSuccess,
        string expectedTitle)
    {
        var success = _renderer.TryGetTitleWithoutNumber(title, out var result);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedTitle, result);
    }
}
