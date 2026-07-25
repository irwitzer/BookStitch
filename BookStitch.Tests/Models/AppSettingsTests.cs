using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Models;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_MatchVersionOneProductDecisions()
    {
        var settings = new AppSettings();

        Assert.Equal(180, settings.ProjectRetentionDays);
        Assert.Equal(550, settings.MetadataPanelAnimationMilliseconds);
        Assert.Equal(".m4a", settings.DefaultOutputExtension);
        Assert.True(settings.OverwriteFinalOutputWithoutAsking);
        Assert.False(settings.MergeAutomaticallyAfterConversion);
        Assert.True(settings.KeepAlbumLinkedToTitle);
        Assert.Equal("Flac", settings.AudioDiscWorkingFormat);
        Assert.Equal(OutputFolderLayoutService.DefaultLayout, settings.OutputFolderLayout);
        Assert.Equal("{Autor} - {Titel}", settings.DefaultFileNameTemplate);
        Assert.Equal("Standard", settings.FocusProfile);
    }
}
