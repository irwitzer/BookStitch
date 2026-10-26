using BookStitch.Models;
using BookStitch.Models.TrackOrdering;
using BookStitch.Services.TrackOrdering;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookStitch.Services;

public sealed class TrackListStateService
{
    public const string FileName = ProjectFolderLayout.TrackListStateFileName;
    public const int CurrentSchemaVersion = 5;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly AtomicJsonStore JsonStore = new();

    public void Save(string projectFolder, IReadOnlyList<TrackInfo> tracks)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
            return;

        var state = new TrackListState
        {
            SchemaVersion = TrackListState.CurrentSchemaVersion,
            UpdatedUtc = DateTime.UtcNow,
            Tracks = tracks.Select((track, order) => new TrackListStateItem
            {
                Key = CreateKey(track),
                SourceId = track.SourceId,
                Order = order,
                IsExcluded = track.IsExcluded,
                AutomaticOrder = track.AutomaticOrderPosition,
                ChapterTitle = track.ChapterTitle ?? string.Empty,
                ExcludedChapterTitle = track.ExcludedChapterTitle ?? string.Empty
            }).ToList()
        };

        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var path = ProjectFolderLayout.GetTrackListStatePath(projectFolder);
        JsonStore.Save(path, state, JsonOptions);
    }

    public List<TrackInfo> Apply(string projectFolder, IEnumerable<TrackInfo> tracks)
    {
        return Apply(
            projectFolder,
            tracks,
            TrackOrderProjectType.LocalFolder,
            confirmedContexts: null);
    }

    public List<TrackInfo> Apply(
        string projectFolder,
        IEnumerable<TrackInfo> tracks,
        TrackOrderProjectType projectType,
        IReadOnlyDictionary<TrackInfo, ConfirmedOrderContext>? confirmedContexts = null)
    {
        var result = tracks.ToList();
        if (string.IsNullOrWhiteSpace(projectFolder))
            return result;

        var path = ProjectFolderLayout.ResolveTrackListStatePath(projectFolder);
        if (!File.Exists(path))
        {
            new TrackAutomaticOrderApplicationService().CalculateAndApply(
                result,
                projectType,
                confirmedContexts);
            Save(projectFolder, result);
            return result;
        }

        try
        {
            var json = File.ReadAllText(path);
            var schema = PersistentJsonSchema.Read(
                json,
                "TrackList-State",
                path,
                TrackListState.CurrentSchemaVersion);
            var state = JsonSerializer.Deserialize<TrackListState>(json, JsonOptions);
            if (state is null)
                return result;

            var needsSchemaMigration = schema.IsLegacy || schema.Version < TrackListState.CurrentSchemaVersion;
            state.SchemaVersion = TrackListState.CurrentSchemaVersion;
            state.Tracks ??= [];
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
                if (item.SourceId != Guid.Empty &&
                    (track.SourceId == Guid.Empty || track.SourceId == item.SourceId))
                {
                    track.SourceId = item.SourceId;
                }
                track.IsExcluded = item.IsExcluded;
                track.AutomaticOrderPosition = item.AutomaticOrder;
                track.ChapterTitle = item.ChapterTitle ?? string.Empty;
                track.ExcludedChapterTitle = item.ExcludedChapterTitle ?? string.Empty;
            }

            var orderedResult = result
                .OrderBy(track => matchedItems.TryGetValue(track, out var item) ? item.Order : int.MaxValue)
                .ToList();
            var needsAutomaticOrderRepair =
                state.AutomaticOrderAlgorithmVersion != AutomaticTrackOrderService.AlgorithmVersion ||
                result.Any(track => !track.AutomaticOrderPosition.HasValue);
            var needsStateRebuild = needsSchemaMigration || matchedItems.Count != result.Count;

            if (needsAutomaticOrderRepair)
            {
                new TrackAutomaticOrderApplicationService().CalculateAndApply(
                    result,
                    projectType,
                    confirmedContexts);
            }

            if (needsAutomaticOrderRepair || needsStateRebuild)
            {
                Save(projectFolder, orderedResult);
                if (needsSchemaMigration)
                    PersistentJsonSchema.TraceLegacyMigration("TrackList-State", path);
            }

            return orderedResult;
        }
        catch (PersistentJsonSchemaException)
        {
            throw;
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
            if (track.SourceId != Guid.Empty)
                return "sourceid:" + track.SourceId.ToString("N").ToUpperInvariant();

            try { return "path:" + Path.GetFullPath(track.FilePath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant(); }
            catch { return "path:" + track.FilePath.Trim().ToUpperInvariant(); }
        }
        if (track.SourceId != Guid.Empty)
            return "sourceid:" + track.SourceId.ToString("N").ToUpperInvariant();

        return $"disc:{track.DiscNumber}:track:{track.TrackNumber}:file:{track.FileName}".ToUpperInvariant();
    }


    private IEnumerable<string> CreateCompatibleKeys(string projectFolder, TrackInfo track)
    {
        if (track.SourceId != Guid.Empty)
            yield return "sourceid:" + track.SourceId.ToString("N").ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(track.FilePath))
        {
            var currentPathKey = CreatePathKey(track);
            yield return currentPathKey;

            if (!currentPathKey.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
                yield break;
        }
        else
        {
            yield return $"disc:{track.DiscNumber}:track:{track.TrackNumber}:file:{track.FileName}".ToUpperInvariant();
            yield break;
        }

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

    private static string CreatePathKey(TrackInfo track)
    {
        try
        {
            return "path:" + Path.GetFullPath(track.FilePath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        }
        catch
        {
            return "path:" + track.FilePath.Trim().ToUpperInvariant();
        }
    }

    private sealed class TrackListState
    {
        internal const int CurrentSchemaVersion = TrackListStateService.CurrentSchemaVersion;

        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public int AutomaticOrderAlgorithmVersion { get; set; } = AutomaticTrackOrderService.AlgorithmVersion;

        public DateTime UpdatedUtc { get; set; }
        public List<TrackListStateItem> Tracks { get; set; } = [];
    }

    private sealed class TrackListStateItem
    {
        public string Key { get; set; } = string.Empty;
        public Guid SourceId { get; set; }
        public int Order { get; set; }
        public int? AutomaticOrder { get; set; }
        public bool IsExcluded { get; set; }
        public string ChapterTitle { get; set; } = string.Empty;
        public string ExcludedChapterTitle { get; set; } = string.Empty;
    }
}
