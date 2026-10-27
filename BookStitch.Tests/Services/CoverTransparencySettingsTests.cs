using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class CoverTransparencySettingsTests
{
    [Theory]
    [InlineData(null, "Auto")]
    [InlineData("", "Auto")]
    [InlineData("invalid", "Auto")]
    [InlineData("white", "White")]
    [InlineData(" Black ", "Black")]
    public void Normalization_and_persistence_use_automatic_as_fallback(string? input, string expected)
    {
        using var folder = new TemporaryFolder();
        var store = new SettingsService(Path.Combine(folder.Path, "app"), folder.Path);
        var settings = new AppSettings { CoverTransparencyMode = input! };
        AppSettingsNormalizationService.Normalize(settings);
        Assert.Equal(expected, settings.CoverTransparencyMode);
        store.Save(settings);
        Assert.Equal(expected, store.Load().CoverTransparencyMode);
    }
}
