using BookStitch.Models;
using System.IO;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class TrackListStateService
{
    public const string FileName = ProjectFolderLayout.TrackListStateFileName;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void Save(string projectFolder, IReadOnlyList<TrackInfo> tracks)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            return;

        var state = new TrackListState
        {
            UpdatedUtc = DateTime.UtcNow,
            Tracks = tracks.Select((track, order) => new TrackListStateItem
            {
                Key = CreateKey(track),
                Order = order,
                IsExcluded = track.IsExcluded,
                ChapterTitle = track.ChapterTitle ?? string.Empty,
                ExcludedChapterTitle = track.ExcludedChapterTitle ?? string.Empty
            }).ToList()
        };

        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var path = ProjectFolderLayout.GetTrackListStatePath(projectFolder);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, path, true);
    }

    public List<TrackInfo> Apply(string projectFolder, IEnumerable<TrackInfo> tracks)
    {
        var result = tracks.ToList();
        if (string.IsNullOrWhiteSpace(projectFolder))
            return result;

        new ProjectFolderMigrationService().MigrateIfNeeded(projectFolder);
        var path = ProjectFolderLayout.ResolveTrackListStatePath(projectFolder);
        if (!File.Exists(path))
            return result;

        try
        {
            var state = JsonSerializer.Deserialize<TrackListState>(File.ReadAllText(path), JsonOptions);
            if (state is null)
                return result;

            var items = state.Tracks
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Order).First(), StringComparer.OrdinalIgnoreCase);

            var matchedItems = new Dictionary<TrackInfo, TrackListStateItem>();
            foreach (var track in result)
            {
                var item = CreateCompatibleKeys(projectFolder, track)
                    .Select(key => items.TryGetValue(key, out var candidate) ? candidate : null)
                    .FirstOrDefault(candidate => candidate is not null);
                if (item is null)
                    continue;

                matchedItems[track] = item;
                track.IsExcluded = item.IsExcluded;
                track.ChapterTitle = item.ChapterTitle ?? string.Empty;
                track.ExcludedChapterTitle = item.ExcludedChapterTitle ?? string.Empty;
            }

            return result.OrderBy(track => matchedItems.TryGetValue(track, out var item) ? item.Order : int.MaxValue).ToList();
        }
        catch
        {
            return result;
        }
    }

    public string CreateKey(TrackInfo track)
    {
        if (!string.IsNullOrWhiteSpace(track.FilePath))
        {
            try { return "path:" + Path.GetFullPath(track.FilePath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant(); }
            catch { return "path:" + track.FilePath.Trim().ToUpperInvariant(); }
        }
        return $"disc:{track.DiscNumber}:track:{track.TrackNumber}:file:{track.FileName}".ToUpperInvariant();
    }


    private IEnumerable<string> CreateCompatibleKeys(string projectFolder, TrackInfo track)
    {
        var current = CreateKey(track);
        yield return current;

        if (string.IsNullOrWhiteSpace(track.FilePath) || !current.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
            yield break;

        string fullPath;
        try { fullPath = Path.GetFullPath(track.FilePath); }
        catch { yield break; }

        var originals = ProjectFolderLayout.GetOriginalsFolder(projectFolder);
        if (!fullPath.StartsWith(originals + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            yield break;

        var relative = Path.GetRelativePath(originals, fullPath);
        if (relative.StartsWith("CD ", StringComparison.OrdinalIgnoreCase))
            yield return "path:" + Path.GetFullPath(Path.Combine(projectFolder, relative)).ToUpperInvariant();

        yield return "path:" + Path.GetFullPath(Path.Combine(projectFolder, "ripped", relative)).ToUpperInvariant();
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length > 1 && segments[0].StartsWith("CD ", StringComparison.OrdinalIgnoreCase))
            yield return "path:" + Path.GetFullPath(Path.Combine(projectFolder, "ripped", Path.Combine(segments.Skip(1).ToArray()))).ToUpperInvariant();
    }

    private sealed class TrackListState
    {
        public DateTime UpdatedUtc { get; set; }
        public List<TrackListStateItem> Tracks { get; set; } = [];
    }

    private sealed class TrackListStateItem
    {
        public string Key { get; set; } = string.Empty;
        public int Order { get; set; }
        public bool IsExcluded { get; set; }
        public string ChapterTitle { get; set; } = string.Empty;
        public string ExcludedChapterTitle { get; set; } = string.Empty;
    }
}
