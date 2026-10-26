using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterComparisonKeyBuilderTests
{
    private readonly ChapterComparisonKeyBuilder _builder = new();

    [Theory]
    [InlineData("Der Stein der Weisen")]
    [InlineData("DER STEIN DER WEISEN")]
    [InlineData("Der Stein der Weisen.")]
    [InlineData("Der  Stein der Weisen")]
    [InlineData("„Der Stein der Weisen“")]
    [InlineData("(Der Stein der Weisen)")]
    [InlineData("[Der Stein der Weisen]")]
    [InlineData("{Der Stein der Weisen}")]
    [InlineData("?!„(Der Stein der Weisen)“.;")]
    public void Build_IgnoresOnlyDefinedOuterVariants(string value)
    {
        Assert.Equal("der stein der weisen", _builder.Build(value));
    }

    [Theory]
    [InlineData("Montagmorgen", "montagmorgen")]
    [InlineData("Montag Morgen", "montag morgen")]
    [InlineData("Montag-Morgen", "montag-morgen")]
    [InlineData("Montagvormittag", "montagvormittag")]
    public void Build_KeepsMeaningfullyDifferentFormsSeparate(string value, string expected)
    {
        Assert.Equal(expected, _builder.Build(value));
    }

    [Theory]
    [InlineData("Montag-Morgen")]
    [InlineData("Montag–Morgen")]
    [InlineData("Montag—Morgen")]
    public void Build_NormalizesDashVariantsWithoutRemovingDash(string value)
    {
        Assert.Equal("montag-morgen", _builder.Build(value));
    }

    [Theory]
    [InlineData("Montag (Ungekürzt)", "montag (ungekürzt)")]
    [InlineData("Montag [Teil 2]", "montag [teil 2]")]
    [InlineData("(Montag) Zusatz", "(montag) zusatz")]
    [InlineData("Montag's Ende", "montag's ende")]
    [InlineData("-Montag-", "-montag-")]
    public void Build_PreservesInternalAndNonIgnorablePunctuation(string value, string expected)
    {
        Assert.Equal(expected, _builder.Build(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Build_EmptyInputReturnsEmpty(string? value)
    {
        Assert.Equal(string.Empty, _builder.Build(value));
    }

    [Theory]
    [InlineData("「星。」", "星")]
    [InlineData("『夜』", "夜")]
    [InlineData("《标题》", "标题")]
    [InlineData("！Прибытие？", "прибытие")]
    public void Build_NormalizesUnicodeEdgePunctuationWithoutChangingText(string value, string expected)
    {
        Assert.Equal(expected, _builder.Build(value));
    }
}
