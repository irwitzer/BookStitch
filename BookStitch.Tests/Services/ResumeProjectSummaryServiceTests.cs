using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ResumeProjectSummaryServiceTests
{
    [Theory]
    [InlineData(0, "Keine Projekte im Projektordner")]
    [InlineData(1, "1 Projekt gefunden")]
    [InlineData(2, "2 Projekte gefunden")]
    [InlineData(17, "17 Projekte gefunden")]
    public void Create_ReturnsExpectedSummary(int projectCount, string expected)
    {
        Assert.Equal(expected, ResumeProjectSummaryService.Create(projectCount));
    }

    [Fact]
    public void Create_RejectsNegativeProjectCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ResumeProjectSummaryService.Create(-1));
    }
}