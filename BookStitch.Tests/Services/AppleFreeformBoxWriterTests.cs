using BookStitch.Services;
using TagLib;
using TagLib.Mpeg4;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AppleFreeformBoxWriterTests
{
    [Fact]
    public void Replace_LanguageWithFourValues_WritesExactlyFourLogicalValues()
    {
        var itemList = new AppleItemListBox();

        AppleFreeformBoxWriter.Replace(itemList, "com.apple.iTunes", "LANGUAGE", ["de-DE", "de", "en-CA", "es-ES"]);

        Assert.Equal(["de-DE", "de", "en-CA", "es-ES"], ReadValues(itemList, "LANGUAGE"));
        Assert.Single(MatchingAnnotations(itemList, "LANGUAGE"));
        Assert.Equal(4, MatchingDataBoxes(itemList, "LANGUAGE").Count);
    }

    [Fact]
    public void Replace_NarratorWithTwoValues_WritesExactlyTwoLogicalValues()
    {
        var itemList = new AppleItemListBox();

        AppleFreeformBoxWriter.Replace(itemList, "com.apple.iTunes", "NARRATOR", ["Mila Brandt", "Tobias Licht"]);

        Assert.Equal(["Mila Brandt", "Tobias Licht"], ReadValues(itemList, "NARRATOR"));
    }

    [Fact]
    public void Replace_SingleValue_WritesExactlyOnce()
    {
        var itemList = new AppleItemListBox();

        AppleFreeformBoxWriter.Replace(itemList, "com.apple.iTunes", "ISBN", ["978123"]);

        Assert.Equal(["978123"], ReadValues(itemList, "ISBN"));
        Assert.Single(MatchingAnnotations(itemList, "ISBN"));
    }

    [Fact]
    public void Replace_RepeatedWrite_IsIdempotent()
    {
        var itemList = new AppleItemListBox();
        var values = new[] { "de", "de-CH" };

        AppleFreeformBoxWriter.Replace(itemList, "com.apple.iTunes", "LANGUAGE", values);
        AppleFreeformBoxWriter.Replace(itemList, "com.apple.iTunes", "LANGUAGE", values);

        Assert.Equal(values, ReadValues(itemList, "LANGUAGE"));
        Assert.Single(MatchingAnnotations(itemList, "LANGUAGE"));
        Assert.Equal(2, MatchingDataBoxes(itemList, "LANGUAGE").Count);
    }

    [Fact]
    public void Replace_ChangedValueCount_RemovesOldValuesCompletely()
    {
        var itemList = new AppleItemListBox();

        AppleFreeformBoxWriter.Replace(itemList, "com.apple.iTunes", "LANGUAGE", ["de", "de-CH"]);
        AppleFreeformBoxWriter.Replace(itemList, "com.apple.iTunes", "LANGUAGE", ["de", "en-CA", "es-ES"]);

        Assert.Equal(["de", "en-CA", "es-ES"], ReadValues(itemList, "LANGUAGE"));
        Assert.Single(MatchingAnnotations(itemList, "LANGUAGE"));
        Assert.Equal(3, MatchingDataBoxes(itemList, "LANGUAGE").Count);
    }

    private static string[] ReadValues(AppleItemListBox itemList, string name) =>
        MatchingDataBoxes(itemList, name)
            .Select(data => data.Text)
            .ToArray();

    private static List<AppleDataBox> MatchingDataBoxes(AppleItemListBox itemList, string name) =>
        MatchingAnnotations(itemList, name)
            .SelectMany(annotation => (annotation.GetChildren(DataType) ?? []).OfType<AppleDataBox>())
            .ToList();

    private static List<AppleAnnotationBox> MatchingAnnotations(AppleItemListBox itemList, string name) =>
        (itemList.GetChildren(DashType) ?? [])
            .OfType<AppleAnnotationBox>()
            .Where(annotation =>
                string.Equals(((AppleAdditionalInfoBox?)annotation.GetChild(MeanType))?.Text, "com.apple.iTunes", StringComparison.Ordinal) &&
                string.Equals(((AppleAdditionalInfoBox?)annotation.GetChild(NameType))?.Text, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static readonly ByteVector DashType = ByteVector.FromString("----", StringType.Latin1);
    private static readonly ByteVector MeanType = ByteVector.FromString("mean", StringType.Latin1);
    private static readonly ByteVector NameType = ByteVector.FromString("name", StringType.Latin1);
    private static readonly ByteVector DataType = ByteVector.FromString("data", StringType.Latin1);
}
