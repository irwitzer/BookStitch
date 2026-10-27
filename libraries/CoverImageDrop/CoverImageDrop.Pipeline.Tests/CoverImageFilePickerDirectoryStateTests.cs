using System.IO;

namespace CoverImageDrop.Pipeline.Tests;

[TestClass]
public sealed class CoverImageFilePickerDirectoryStateTests
{
    [TestMethod]
    public void PreferredDirectory_WinsUntilSelectionThenSelectedDirectoryBecomesLastUsed()
    {
        using var fixture = new DirectoryFixture();
        var restored = fixture.CreateDirectory("restored");
        var preferred = fixture.CreateDirectory("preferred");
        var selected = fixture.CreateDirectory("selected");

        var state = new CoverImageFilePickerDirectoryState(restored);
        state.SetPreferredFilePickerDirectory(preferred);

        Assert.AreEqual(
            preferred,
            state.GetFilePickerDirectoryForNextOpen());

        state.RecordFilePickerSelection(
            Path.Combine(selected, "cover.jpg"));

        Assert.IsNull(state.PreferredDirectory);
        Assert.AreEqual(selected, state.LastUsedDirectory);
        Assert.AreEqual(
            selected,
            state.GetFilePickerDirectoryForNextOpen());
    }

    [TestMethod]
    public void RestoredLastDirectory_IsUsedByNewStateInstance()
    {
        using var fixture = new DirectoryFixture();
        var selected = fixture.CreateDirectory("selected");

        var firstState = new CoverImageFilePickerDirectoryState();
        firstState.RecordFilePickerSelection(
            Path.Combine(selected, "cover.png"));

        var restoredState = new CoverImageFilePickerDirectoryState(
            firstState.LastUsedDirectory);

        Assert.AreEqual(
            selected,
            restoredState.GetFilePickerDirectoryForNextOpen());
    }

    [TestMethod]
    public void MissingPreferredAndLastDirectory_FallBackToDesktop()
    {
        using var fixture = new DirectoryFixture();
        var missingLast = Path.Combine(fixture.Root, "missing-last");
        var missingPreferred = Path.Combine(
            fixture.Root,
            "missing-preferred");

        var state = new CoverImageFilePickerDirectoryState(missingLast);
        state.SetPreferredFilePickerDirectory(missingPreferred);

        var expectedDesktop = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory);

        Assert.AreEqual(
            string.IsNullOrWhiteSpace(expectedDesktop)
                ? null
                : expectedDesktop,
            state.GetFilePickerDirectoryForNextOpen());
    }

    [TestMethod]
    public void ClearingPreferredDirectory_RevealsLastUsedDirectory()
    {
        using var fixture = new DirectoryFixture();
        var lastUsed = fixture.CreateDirectory("last-used");
        var preferred = fixture.CreateDirectory("preferred");

        var state = new CoverImageFilePickerDirectoryState(lastUsed);
        state.SetPreferredFilePickerDirectory(preferred);
        state.SetPreferredFilePickerDirectory(null);

        Assert.IsNull(state.PreferredDirectory);
        Assert.AreEqual(
            lastUsed,
            state.GetFilePickerDirectoryForNextOpen());
    }

    private sealed class DirectoryFixture : IDisposable
    {
        public DirectoryFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "CoverImageDrop.FilePickerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
