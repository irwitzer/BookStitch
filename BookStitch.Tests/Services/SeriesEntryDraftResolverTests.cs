using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SeriesEntryDraftResolverTests
{
    private readonly SeriesEntryDraftResolver _resolver = new();

    [Theory]
    [InlineData("Userreihe 7", "7", "Userreihe", "7")]
    [InlineData("Userreihe 4,5", "4.5", "Userreihe", "4.5")]
    [InlineData("Userreihe 6,6,3", "6.6.3", "Userreihe", "6.6.3")]
    [InlineData("Userreihe 6A", "6A", "Userreihe", "6A")]
    [InlineData("Userreihe IV", "IV", "Userreihe", "IV")]
    [InlineData("Userreihe 8-Extra", "8-Extra", "Userreihe", "8-Extra")]
    public void ResolveForCommit_RemovesSemanticallyDuplicateEndToken(
        string name,
        string part,
        string expectedName,
        string expectedPart)
    {
        var result = _resolver.ResolveForCommit(name, part, null, allowAutomaticSplit: false);

        Assert.Equal(expectedName, result.Name);
        Assert.Equal(expectedPart, result.Part);
    }

    [Theory]
    [InlineData("Serie 7", "Serie", "7")]
    [InlineData("Serie 100", "Serie", "100")]
    [InlineData("Serie 4.5", "Serie", "4.5")]
    [InlineData("Serie 4,5", "Serie", "4.5")]
    [InlineData("Serie 6.6.3", "Serie", "6.6.3")]
    [InlineData("Serie 6,6,3", "Serie", "6.6.3")]
    [InlineData("Serie 6A", "Serie", "6A")]
    [InlineData("Serie 4.5B", "Serie", "4.5B")]
    [InlineData("Serie 4,5B", "Serie", "4.5B")]
    public void ResolveUserNameConfirmation_AutoSplitsSafePositionSuffixes(
        string input,
        string expectedName,
        string expectedPart)
    {
        var result = _resolver.ResolveUserNameConfirmation(input, string.Empty);

        Assert.Equal(expectedName, result.Name);
        Assert.Equal(expectedPart, result.Part);
        Assert.NotNull(result.AutoSplitState);
    }

    [Theory]
    [InlineData("Serie A")]
    [InlineData("Serie IV")]
    [InlineData("Serie Special")]
    [InlineData("Serie 8-Extra")]
    [InlineData("Serie 6-7")]
    [InlineData("Serie #7")]
    [InlineData("Serie (7)")]
    public void ResolveUserNameConfirmation_DoesNotGuessAmbiguousSuffixes(string input)
    {
        var result = _resolver.ResolveUserNameConfirmation(input, string.Empty);

        Assert.Equal(input, result.Name);
        Assert.Equal(string.Empty, result.Part);
        Assert.Null(result.AutoSplitState);
    }

    [Fact]
    public void AutomaticSplit_RemainsReversible()
    {
        var auto = _resolver.ResolveUserNameConfirmation("District 9", string.Empty);

        var accepted = _resolver.ResolveForCommit(auto.Name, auto.Part, auto.AutoSplitState, allowAutomaticSplit: true);
        var removed = _resolver.ResolveForCommit(auto.Name, string.Empty, auto.AutoSplitState, allowAutomaticSplit: true);
        var changed = _resolver.ResolveForCommit(auto.Name, "5", auto.AutoSplitState, allowAutomaticSplit: true);

        Assert.Equal(new SeriesEntryDraftResolution("District", "9"), accepted);
        Assert.Equal(new SeriesEntryDraftResolution("District 9", string.Empty), removed);
        Assert.Equal(new SeriesEntryDraftResolution("District", "5"), changed);
    }

    [Fact]
    public void ResolveForCommit_PreservesConflictingNameAndPartWithoutFreshInterpretation()
    {
        var result = _resolver.ResolveForCommit("Serie 7", "8", null, allowAutomaticSplit: false);

        Assert.Equal(new SeriesEntryDraftResolution("Serie 7", "8"), result);
    }

    [Fact]
    public void StructuredLoad_PreservesNamePartStructureWhileEditRepairsLegacyDuplicate()
    {
        var structured = _resolver.LoadStructured("District 9", string.Empty);
        var repaired = _resolver.LoadStructuredForEdit("Serie 7", "7");

        Assert.Equal(new SeriesEntryDraftResolution("District 9", string.Empty), structured);
        Assert.Equal(new SeriesEntryDraftResolution("Serie", "7"), repaired);
    }
}
