using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class LocalProjectExtensionStateService
{
    public ProjectPipelineState ResolveInitialState(IEnumerable<string> expectedOriginalPaths)
    {
        ArgumentNullException.ThrowIfNull(expectedOriginalPaths);

        var paths = expectedOriginalPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
            return ProjectPipelineState.AcquiringSources;

        return paths.All(IsCompleteOriginal)
            ? ProjectPipelineState.Converting
            : ProjectPipelineState.AcquiringSources;
    }

    private static bool IsCompleteOriginal(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
