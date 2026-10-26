using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class CoverFileNamePolicyTests
{
    [Fact]
    public void BuildManagedFileName_UsesStablePrefixHashAndNormalizedTitle()
    {
        Assert.Equal(
            "Cover_2a51c7d91e04_Der_Herr_der_Ringe.jpg",
            CoverFileNamePolicy.BuildManagedFileName("2a51c7d91e04", "Der Herr der Ringe"));
    }

    [Fact]
    public void NormalizeTitleSegment_CollapsesUnicodeWhitespaceAndRemovesPortableForbiddenCharacters()
    {
        var title = "  A\u00A0B\u2007C\tD:E/F\\G?H*I\"J<K>L|M  ";
        var normalized = CoverFileNamePolicy.NormalizeTitleSegment(title);

        Assert.Equal("A_B_C_DEFGHIJKLM", normalized);
        Assert.Equal(-1, normalized.IndexOfAny("<>:\"/\\|?*".ToCharArray()));
    }

    [Fact]
    public void NormalizeTitleSegment_PreservesUsefulUnicodeAndCapsLength()
    {
        var unicode = CoverFileNamePolicy.NormalizeTitleSegment("Ä Ö Ü é ñ");
        Assert.Equal("Ä_Ö_Ü_é_ñ", unicode);

        var longTitle = new string('x', 300);
        var fileName = CoverFileNamePolicy.BuildManagedFileName("abcdef123456", longTitle);
        Assert.StartsWith("Cover_abcdef123456_", fileName, StringComparison.Ordinal);
        Assert.EndsWith(".jpg", fileName, StringComparison.Ordinal);
        Assert.True(CoverFileNamePolicy.NormalizeTitleSegment(longTitle).Length <= CoverFileNamePolicy.MaxTitleSegmentLength);
    }

    [Fact]
    public void BuildManagedFileName_WithoutUsefulTitle_UsesHashOnly()
    {
        Assert.Equal("Cover_abcdef123456.jpg", CoverFileNamePolicy.BuildManagedFileName("abcdef123456", "  :/*  "));
        Assert.True(CoverFileNamePolicy.TryGetManagedHash("Cover_abcdef123456.jpg", out var hash));
        Assert.Equal("abcdef123456", hash);
        Assert.False(CoverFileNamePolicy.TryGetManagedHash("legacy_abcdef123456_cover_2000.jpg", out _));
    }
}
