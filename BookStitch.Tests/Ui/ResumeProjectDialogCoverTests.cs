using BookStitch.Dialog;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class ResumeProjectDialogCoverTests
{
    [Fact]
    public void ResolveProjectCoverImageUri_WhenProjectCoverIsMissing_UsesEmbeddedPlaceholderPackUri()
    {
        var expected = AppearanceAssetCatalog.Get(
            AppearanceAssetRole.CoverPlaceholder,
            AppearanceMode.Dark);

        var uri = ResumeProjectDialog.ResolveProjectCoverImageUri(AppearanceMode.Dark);

        Assert.True(uri.IsAbsoluteUri);
        Assert.False(uri.IsFile);
        Assert.Equal("pack", uri.Scheme);
        Assert.Equal(expected.PackUriString, uri.ToString());

        var light = AppearanceAssetCatalog.Get(AppearanceAssetRole.CoverPlaceholder, AppearanceMode.Light);
        Assert.Equal(light.PackUriString, ResumeProjectDialog.ResolveProjectCoverImageUri(AppearanceMode.Light).ToString());
    }

    [Fact]
    public void ResolveProjectCoverImageUri_WhenProjectCoverExists_UsesFileUri()
    {
        using var folder = new TemporaryFolder();
        var coverPath = folder.CreateFile("cover.jpg");

        var uri = ResumeProjectDialog.ResolveProjectCoverImageUri(AppearanceMode.Light, coverPath);

        Assert.True(uri.IsFile);
        Assert.Equal(Path.GetFullPath(coverPath), uri.LocalPath);
    }
}
