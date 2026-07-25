using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class NotificationSoundCatalogTests
{
    [Theory]
    [InlineData(SoundLibrary.Gentle, "Gentle")]
    [InlineData(SoundLibrary.Bass, "Bass")]
    [InlineData(SoundLibrary.HammondOrgan, "HammondOrgan")]
    [InlineData(SoundLibrary.Warm, "Warm")]
    [InlineData(SoundLibrary.Retro, "Retro")]
    public void GetLibraryFolderName_ReturnsExpectedFolder(SoundLibrary library, string expected)
    {
        Assert.Equal(expected, NotificationSoundCatalog.GetLibraryFolderName(library));
    }

    [Theory]
    [InlineData(NotificationEvent.DiscChangeRequired, "disc-change.wav")]
    [InlineData(NotificationEvent.UserActionRequired, "warning.wav")]
    [InlineData(NotificationEvent.ProjectCompleted, "project-completed.wav")]
    [InlineData(NotificationEvent.Warning, "warning.wav")]
    [InlineData(NotificationEvent.Information, "information.wav")]
    [InlineData(NotificationEvent.Error, "error.wav")]
    public void GetFileName_ReturnsExpectedFile(NotificationEvent notificationEvent, string expected)
    {
        Assert.Equal(expected, NotificationSoundCatalog.GetFileName(notificationEvent));
    }

    [Fact]
    public void PreviewSound_UsesProjectCompletedFile()
    {
        Assert.Equal("project-completed.wav", NotificationSoundCatalog.PreviewSoundFileName);
    }
}
