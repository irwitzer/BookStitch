using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ChapterGenericClassifierTests
{
    private readonly ChapterGenericClassifier _classifier = new();

    [Theory]
    [InlineData("Kapitel")]
    [InlineData("Chapter")]
    [InlineData("Capítulo")]
    [InlineData("Chapitre")]
    [InlineData("Capitolo")]
    [InlineData("Hoofdstuk")]
    [InlineData("Teil")]
    [InlineData("Part")]
    [InlineData("Parte")]
    [InlineData("Partie")]
    [InlineData("Deel")]
    public void IsGeneric_RecognizesPureMarkersAcrossStartLanguages(string text)
    {
        Assert.True(_classifier.IsGeneric(text));
    }

    [Theory]
    [InlineData("Teil 2")]
    [InlineData("Part 3")]
    [InlineData("Parte 4")]
    [InlineData("Band 7")]
    [InlineData("Chapter House")]
    [InlineData("The Final Chapter")]
    [InlineData("")]
    public void IsGeneric_PreservesMeaningfulCombinations(string text)
    {
        Assert.False(_classifier.IsGeneric(text));
    }
}
