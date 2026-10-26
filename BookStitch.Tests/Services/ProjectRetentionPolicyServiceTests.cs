using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectRetentionPolicyServiceTests
{
    [Fact]
    public void Never_IsExplicitAndNeverAliasesZeroDays()
    {
        Assert.True(ProjectRetentionPolicyService.IsNever(ProjectRetentionModes.Never));
        Assert.False(ProjectRetentionPolicyService.IsNever(ProjectRetentionModes.Days));
        Assert.Equal(ProjectRetentionModes.Days, ProjectRetentionPolicyService.NormalizeMode(null));
        Assert.Equal("Nie", ProjectRetentionPolicyService.FormatExpiration(DateTime.UtcNow, ProjectRetentionModes.Never));
    }

    [Theory]
    [InlineData(0, "heute")]
    [InlineData(1, "in 1 Tag")]
    [InlineData(4, "in 4 Tagen")]
    [InlineData(-1, "abgelaufen")]
    public void Days_KeepTheExistingExpirationLanguage(int offsetDays, string expected)
    {
        var today = new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, ProjectRetentionPolicyService.FormatExpiration(today.AddDays(offsetDays), ProjectRetentionModes.Days, today));
    }
}
