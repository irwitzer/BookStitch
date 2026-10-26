using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class OutputFolderFallbackServiceTests
{
    [Fact]
    public void Resolve_UsesExistingSelectedFolder()
    {
        using var fixture = new TempFolderFixture();
        var selected = Directory.CreateDirectory(Path.Combine(fixture.Root, "selected")).FullName;
        var fallback = Path.Combine(fixture.Root, "fallback");

        Assert.Equal(selected, OutputFolderFallbackService.Resolve(selected, fallback));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_UsesFallbackWhenSelectedFolderIsMissing(string? selected)
    {
        using var fixture = new TempFolderFixture();
        var fallback = Path.Combine(fixture.Root, "fallback");

        Assert.Equal(fallback, OutputFolderFallbackService.Resolve(selected, fallback));
    }

    [Fact]
    public void Resolve_UsesFallbackWhenSelectedFolderNoLongerExists()
    {
        using var fixture = new TempFolderFixture();
        var missing = Path.Combine(fixture.Root, "missing");
        var fallback = Path.Combine(fixture.Root, "fallback");

        Assert.Equal(fallback, OutputFolderFallbackService.Resolve(missing, fallback));
    }

    [Fact]
    public void GetDesktopFallbackFolder_UsesWindowsDesktopWhenAvailable()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var expected = !string.IsNullOrWhiteSpace(desktop)
            ? desktop
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(expected, OutputFolderFallbackService.GetDesktopFallbackFolder());
    }

    private sealed class TempFolderFixture : IDisposable
    {
        public TempFolderFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "BookStitch-OutputFallback-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}