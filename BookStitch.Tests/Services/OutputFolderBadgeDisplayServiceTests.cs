using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class OutputFolderBadgeDisplayServiceTests
{
    [Fact]
    public void BuildText_PrefersCompleteTrailingPathSegments()
    {
        static double Measure(string value) => value.Length;

        Assert.Equal(
            "Books-Local-Output",
            OutputFolderBadgeDisplayService.BuildText(
                @"C:\TestData\Desktop\Books-Root\Books-Local-Output",
                18,
                Measure));

        Assert.Equal(
            @"Books-Root\Books-Local-Output",
            OutputFolderBadgeDisplayService.BuildText(
                @"C:\TestData\Desktop\Books-Root\Books-Local-Output",
                29,
                Measure));

        Assert.Equal(
            @"Desktop\Books-Root\Books-Local-Output",
            OutputFolderBadgeDisplayService.BuildText(
                @"C:\TestData\Desktop\Books-Root\Books-Local-Output",
                37,
                Measure));
    }

    [Fact]
    public void CalculateWidth_ShrinksToContentWithoutExceedingMaximum()
    {
        static double Measure(string value) => value.Length;

        Assert.Equal(12, OutputFolderBadgeDisplayService.CalculateWidth("Books", 20, 7, Measure));
        Assert.Equal(20, OutputFolderBadgeDisplayService.CalculateWidth("VeryLongFolderName", 20, 7, Measure));
    }

    [Fact]
    public void BuildText_EllipsizesOnlyAnOversizedFinalSegment()
    {
        static double Measure(string value) => value.Length;

        Assert.Equal(
            "VeryLongF…",
            OutputFolderBadgeDisplayService.BuildText(
                @"C:\Parent\VeryLongFolderName",
                10,
                Measure));
    }
}
