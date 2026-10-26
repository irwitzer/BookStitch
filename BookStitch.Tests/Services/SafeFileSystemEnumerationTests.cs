using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SafeFileSystemEnumerationTests
{
    [Fact]
    public void Lazy_enumeration_failure_retains_previous_entries_and_reports_the_folder()
    {
        static IEnumerable<string> BrokenEntries()
        {
            yield return "readable.mp3";
            throw new UnauthorizedAccessException("Folder disappeared during enumeration");
        }
        var errors = new List<string>();
        var entries = SafeFileSystemEnumeration.EnumerateSafely(BrokenEntries, "blocked",
            CancellationToken.None, (folder, _) => errors.Add(folder)).ToList();
        Assert.Equal(["readable.mp3"], entries);
        Assert.Equal(["blocked"], errors);
    }

    [Fact]
    public void Missing_subfolder_does_not_destroy_sibling_enumeration()
    {
        using var root = new TemporaryFolder();
        var first = Path.Combine(root.Path, "first");
        var vanished = Path.Combine(root.Path, "vanished");
        Directory.CreateDirectory(first);
        File.WriteAllText(Path.Combine(first, "track.mp3"), "audio");
        var errors = new List<string>();
        var files = new[] { vanished, first }.SelectMany(folder =>
            SafeFileSystemEnumeration.EnumerateFiles(folder, onError: (path, _) => errors.Add(path))).ToArray();
        Assert.Single(files);
        Assert.Equal([vanished], errors);
    }

    [Fact]
    public void Cancellation_is_observed_while_enumerating()
    {
        using var root = new TemporaryFolder();
        File.WriteAllText(Path.Combine(root.Path, "track.mp3"), "audio");
        using var cancellation = new CancellationTokenSource();
        using var entries = SafeFileSystemEnumeration.EnumerateFiles(root.Path, cancellation.Token).GetEnumerator();
        Assert.True(entries.MoveNext());
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => entries.MoveNext());
    }
}
