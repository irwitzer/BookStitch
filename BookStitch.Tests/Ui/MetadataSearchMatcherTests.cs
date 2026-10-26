using BookStitch.Dialog;
using Xunit;

namespace BookStitch.Tests.Ui;

// Platform-neutral contract, currently coupled to the matcher in the WPF dialog file (class E).
public sealed class MetadataSearchMatcherTests
{
    [Theory]
    [InlineData("René", "Rene")]
    [InlineData("Tiếng Việt", "Tieng Viet")]
    [InlineData("Niño", "Nino")]
    [InlineData("Märchen", "Marchen")]
    [InlineData("René", "Rene\u0301")]
    [InlineData("বাংলা", "বাংলা")]
    [InlineData("𠀀𠀁", "𠀀𠀁")]
    [InlineData("ガイド", "カ\u3099イト\u3099")]
    public void Equivalent_text_keeps_exact_rank(string value, string query) =>
        Assert.Equal(0, ConceptDialogSearchMatcher.GetRank([value], query));

    [Theory]
    [InlineData("বাংলা", "বেলা")]
    [InlineData("𠀀𠀁", "𠀂𠀃")]
    [InlineData("が", "か")]
    [InlineData("की", "कु")]
    [InlineData("ά", "α")]
    public void Meaningful_non_latin_letters_and_marks_remain_distinct(string first, string second) =>
        Assert.NotEqual(ConceptDialogSearchMatcher.Normalize(first), ConceptDialogSearchMatcher.Normalize(second));

    [Theory]
    [InlineData("日本語の物語", "物語", 2)]
    [InlineData("繁體中文故事", "故事", 2)]
    [InlineData("前𠀀𠀁後", "𠀀𠀁", 2)]
    [InlineData("前ガイド後", "ガイド", 2)]
    [InlineData("物語", "物語", 0)]
    [InlineData("物語の続き", "物語", 1)]
    [InlineData("Title", "ti", 1)]
    [InlineData("Description", "crip", 2)]
    [InlineData("Beschreibung", "bschb", 3)]
    public void Matching_preserves_rank_and_allows_short_cjk_infixes(string value, string query, int rank) =>
        Assert.Equal(rank, ConceptDialogSearchMatcher.GetRank([value], query));

    [Theory]
    [InlineData("Title", "tle")]
    [InlineData("Title", "it")]
    [InlineData("前物語後", "語")]
    [InlineData("前𠀀後", "𠀀")]
    [InlineData("前物a後", "物a")]
    [InlineData("前a物語後", "a物語")]
    [InlineData("前物1後", "物1")]
    [InlineData("বাংলা", "বেলা")]
    [InlineData("𠀀𠀁", "𠀂𠀃")]
    public void Short_non_cjk_infixes_and_unrelated_values_do_not_match(string value, string query) =>
        Assert.Null(ConceptDialogSearchMatcher.GetRank([value], query));

    [Theory]
    [InlineData("!!!")][InlineData("😀")][InlineData("\u0301")]
    public void Nonempty_input_without_searchable_characters_is_not_an_empty_query(string query)
    {
        Assert.Equal(string.Empty, ConceptDialogSearchMatcher.Normalize(query));
        Assert.Null(ConceptDialogSearchMatcher.GetRank(["Title"], query));
    }

    [Theory]
    [InlineData(0xD800, 1)][InlineData(0xDC00, 1)][InlineData(0xD800, 2)]
    public void Isolated_surrogates_are_not_searchable(int codeUnit, int count)
    {
        var query = new string((char)codeUnit, count);
        Assert.Equal(string.Empty, ConceptDialogSearchMatcher.Normalize(query));
        Assert.Null(ConceptDialogSearchMatcher.GetRank(["Title"], query));
    }

    [Theory]
    [InlineData("")][InlineData(" \t ")]
    public void Empty_input_preserves_unfiltered_match(string query) =>
        Assert.Equal(0, ConceptDialogSearchMatcher.GetRank(["Title"], query));

    [Fact]
    public void Invalid_utf16_in_value_or_query_does_not_throw_or_erase_valid_letters()
    {
        Assert.Equal(0, ConceptDialogSearchMatcher.GetRank(["Ti\ud800tle"], "ti\udc00tle"));
        Assert.Equal("TITLE", ConceptDialogSearchMatcher.Normalize("Ti\ud800tle"));
    }

    [Fact]
    public void Long_text_values_keep_the_ordered_subsequence_opt_out()
    {
        Assert.Null(ConceptDialogSearchMatcher.GetRank(["Kommentar"], "kmntr", allowOrderedSubsequence: false));
        Assert.Equal(2, ConceptDialogSearchMatcher.GetRank(["前物語後"], "物語", allowOrderedSubsequence: false));
    }
}
