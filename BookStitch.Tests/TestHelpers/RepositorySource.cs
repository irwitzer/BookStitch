using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace BookStitch.Tests.TestHelpers;

internal static class RepositorySource
{
    public static string Read(
        string[] relativePath,
        [CallerFilePath] string testFilePath = "") =>
        File.ReadAllText(Find(relativePath, testFilePath));

    public static XDocument LoadXaml(
        string[] relativePath,
        [CallerFilePath] string testFilePath = "") =>
        XDocument.Load(
            Find(relativePath, testFilePath),
            LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

    public static string Find(
        string[] relativePath,
        [CallerFilePath] string testFilePath = "")
    {
        foreach (var root in SearchRoots(testFilePath))
        {
            for (var directory = new DirectoryInfo(root); directory is { Exists: true }; directory = directory.Parent)
            {
                var candidate = Path.Combine([directory.FullName, .. relativePath]);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        throw new FileNotFoundException("Repository file not found.", Path.Combine(relativePath));
    }

    private static IEnumerable<string> SearchRoots(string testFilePath)
    {
        var sourceRoot = Directory.GetParent(testFilePath)?.Parent?.Parent?.FullName;
        if (!string.IsNullOrWhiteSpace(sourceRoot))
            yield return sourceRoot;

        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();
    }
}
