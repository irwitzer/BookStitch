using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class CurrentProjectTypePresentationServiceTests
{
    [Fact]
    public void Create_PrefersLoadedAudioDiscOverOtherActiveProjectTypes()
    {
        var result = CurrentProjectTypePresentationService.Create(
            new LoadedProjectStateSnapshot(
                NeedsDiscImport: false,
                IsMp3Disc: false,
                IsAudioDisc: true,
                IsLocal: false),
            hasActiveAudioDiscProject: false,
            hasActiveMp3DiscProject: true,
            hasCurrentProjectWorkFolder: true);

        Assert.Equal(CurrentProjectSourceKind.AudioDisc, result.SourceKind);
        Assert.Equal(UiSymbolResources.ProjectTypeAudioDisc, result.Glyph);
        Assert.True(result.IsVisible);
    }

    [Fact]
    public void Create_ReturnsMp3DiscWhenNoAudioDiscProjectExists()
    {
        var result = CurrentProjectTypePresentationService.Create(
            new LoadedProjectStateSnapshot(
                NeedsDiscImport: false,
                IsMp3Disc: true,
                IsAudioDisc: false,
                IsLocal: false),
            hasActiveAudioDiscProject: false,
            hasActiveMp3DiscProject: false,
            hasCurrentProjectWorkFolder: true);

        Assert.Equal(CurrentProjectSourceKind.Mp3Disc, result.SourceKind);
        Assert.Equal(UiSymbolResources.ProjectTypeMp3Disc, result.Glyph);
    }

    [Fact]
    public void Create_ReturnsLocalFilesForLoadedOrActiveLocalProject()
    {
        var result = CurrentProjectTypePresentationService.Create(
            new LoadedProjectStateSnapshot(false, false, false, false),
            false,
            false,
            true);

        Assert.Equal(CurrentProjectSourceKind.LocalFiles, result.SourceKind);
        Assert.Equal(UiSymbolResources.ProjectTypeLocalFiles, result.Glyph);
    }

    [Fact]
    public void Create_ReturnsInvisibleNoneWithoutProject()
    {
        var result = CurrentProjectTypePresentationService.Create(
            new LoadedProjectStateSnapshot(false, false, false, false),
            false,
            false,
            false);

        Assert.Equal(CurrentProjectSourceKind.None, result.SourceKind);
        Assert.Equal(string.Empty, result.Glyph);
        Assert.False(result.IsVisible);
    }
}
