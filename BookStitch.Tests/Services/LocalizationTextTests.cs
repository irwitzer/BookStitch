using System.Globalization;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class LocalizationTextTests
{
    [Fact]
    public void MissingSatelliteFallsBackToEnglishNeutralResource()
    {
        Assert.Equal("Language", LocalizationText.Get("LanguageHeading", CultureInfo.GetCultureInfo("is-IS")));
        Assert.Equal("System (Windows)", LocalizationText.Get("LanguageSystem", CultureInfo.GetCultureInfo("is-IS")));
    }
}
