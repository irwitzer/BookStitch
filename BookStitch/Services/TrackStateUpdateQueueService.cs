using BookStitch.Models;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace BookStitch.Services;

public sealed class TrackStateUpdateQueueService : IDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(750);

    private readonly DispatcherTimer _timer;
    private readonly Func<TrackStateUpdateContext> _contextProvider;
    private readonly Action _onTracksChanged;
    private readonly bool _isEnabled;
    private int _refreshRequested;
    private bool _isTickRunning;
    private bool _wasWorkflowActive;

    public TrackStateUpdateQueueService(
        Func<TrackStateUpdateContext> contextProvider,
        Action onTracksChanged,
        TimeSpan? interval = null,
        bool isEnabled = true)
    {
        _contextProvider = contextProvider;
        _onTracksChanged = onTracksChanged;
        _isEnabled = isEnabled;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = interval ?? DefaultInterval
        };
        _timer.Tick += Timer_Tick;
    }

    public void Start()
    {
        if (_isEnabled)
            _timer.Start();
    }

    public void RequestRefresh()
    {
        if (!_isEnabled)
            return;

        Interlocked.Exchange(ref _refreshRequested, 1);
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_isTickRunning)
            return;

        var context = _contextProvider();
        if (context.IsRefreshSuspended)
        {
            _wasWorkflowActive = context.IsWorkflowActive;
            return;
        }

        var shouldApplyUpdates = Volatile.Read(ref _refreshRequested) == 1 || context.IsWorkflowActive || _wasWorkflowActive;
        _wasWorkflowActive = context.IsWorkflowActive;
        if (!shouldApplyUpdates)
            return;

        Interlocked.Exchange(ref _refreshRequested, 0);
        _isTickRunning = true;
        try
        {
            if (ApplyUpdates(context))
                _onTracksChanged();
        }
        finally
        {
            _isTickRunning = false;
        }
    }

    public static bool ApplyUpdates(TrackStateUpdateContext context)
    {
        if (context.Tracks.Count == 0)
            return false;

        var convertedBySourcePath = LoadConvertedEntries(context.ProjectWorkFolder, context.SelectedPreset);
        var preset = ExportPreset.Parse(context.SelectedPreset);
        var changed = false;

        foreach (var track in context.Tracks)
        {
            var sourcePath = NormalizePath(track.FilePath);
            var sourceFile = GetExistingFile(sourcePath, context.IsWorkflowActive);
            var sourceSizeAvailable = sourceFile is not null;
            var sourceSizeMb = sourceFile is null ? 0d : ToMegabytes(sourceFile.File.Length);
            if (track.SourceSizeAvailable != sourceSizeAvailable)
            {
                track.SourceSizeAvailable = sourceSizeAvailable;
                changed = true;
            }

            if (!NearlyEqual(track.SizeMb, sourceSizeMb))
            {
                track.SizeMb = sourceSizeMb;
                changed = true;
            }

            if (SetWarningMarker(
                    track,
                    "Quelldatei ist leer",
                    sourceFile is { IsPartFile: false } && sourceFile.File.Length == 0))
            {
                changed = true;
            }

            if (sourceFile is { IsPartFile: false } && sourceFile.File.Length > 0)
            {
                if (ApplyCompletedSourceMetadata(track, sourceFile.File))
                    changed = true;
            }
            else if (sourceFile is null && ClearMissingSourceMetadata(track))
            {
                changed = true;
            }

            ConvertedTrackState? convertedState = null;
            if (!string.IsNullOrWhiteSpace(sourcePath))
                convertedBySourcePath.TryGetValue(sourcePath, out convertedState);

            var resolvedConvertedPath = ResolveConvertedPath(
                context,
                track,
                sourcePath,
                convertedState);
            var convertedFile = GetConvertedFile(resolvedConvertedPath, context.IsWorkflowActive);
            var convertedSizeAvailable = convertedFile is not null;
            var convertedSizeMb = convertedFile is null ? 0d : ToMegabytes(convertedFile.File.Length);
            if (track.ConvertedSizeAvailable != convertedSizeAvailable)
            {
                track.ConvertedSizeAvailable = convertedSizeAvailable;
                changed = true;
            }

            if (!NearlyEqual(track.ConvertedSizeMb, convertedSizeMb))
            {
                track.ConvertedSizeMb = convertedSizeMb;
                changed = true;
            }

            if (SetWarningMarker(
                    track,
                    "AAC-Datei ist leer",
                    convertedFile is { IsPartFile: false } && convertedFile.File.Length == 0))
            {
                changed = true;
            }

            // Fertige Dateien werden atomar aus .part umbenannt. Eine vorhandene,
            // nicht leere Zieldatei ist deshalb auch dann wiederverwendbar, wenn der
            // Live-Import den Manifeststatus vor einem Abbruch noch nicht aktualisiert hat.
            var hasCompletedConvertedFile = !string.IsNullOrWhiteSpace(resolvedConvertedPath) &&
                File.Exists(resolvedConvertedPath) &&
                new FileInfo(resolvedConvertedPath).Length > 0;

            if (hasCompletedConvertedFile)
            {
                if (!string.Equals(track.PreparedConvertedPath, resolvedConvertedPath, StringComparison.OrdinalIgnoreCase))
                {
                    track.PreparedConvertedPath = resolvedConvertedPath;
                    changed = true;
                }

                if (!string.Equals(track.PreparedConvertedPreset, context.SelectedPreset, StringComparison.Ordinal))
                {
                    track.PreparedConvertedPreset = context.SelectedPreset;
                    changed = true;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(track.PreparedConvertedPath))
                {
                    track.PreparedConvertedPath = string.Empty;
                    changed = true;
                }

                if (!string.IsNullOrEmpty(track.PreparedConvertedPreset))
                {
                    track.PreparedConvertedPreset = string.Empty;
                    changed = true;
                }
            }

            if (track.HasReusableConvertedFile != hasCompletedConvertedFile)
            {
                track.HasReusableConvertedFile = hasCompletedConvertedFile;
                changed = true;
            }

            if (sourceFile is not null &&
                sourceFile.File.Length > 0 &&
                string.Equals(track.ProcessingAction, "FLAC rippen", StringComparison.OrdinalIgnoreCase))
            {
                track.ProcessingAction = AudioProcessingService.DetermineProcessingAction(track, preset);
                changed = true;
            }
        }

        return changed;
    }


    private static bool ApplyCompletedSourceMetadata(TrackInfo track, FileInfo sourceFile)
    {
        var changed = false;
        var extension = sourceFile.Extension.ToLowerInvariant();

        if (!string.Equals(track.Extension, extension, StringComparison.OrdinalIgnoreCase))
        {
            track.Extension = extension;
            changed = true;
        }

        var codec = extension switch
        {
            ".flac" => "FLAC",
            ".mp3" => "MP3",
            ".m4a" or ".m4b" or ".aac" => "AAC",
            ".wav" => "PCM",
            ".ogg" or ".oga" => "Vorbis",
            ".opus" => "Opus",
            _ => track.Codec
        };

        if (!string.IsNullOrWhiteSpace(codec) &&
            !string.Equals(track.Codec, codec, StringComparison.OrdinalIgnoreCase))
        {
            track.Codec = codec;
            changed = true;
        }

        // Audio-CD-Arbeitsdateien sind immer zweikanalige FLAC-Dateien.
        if (extension == ".flac" &&
            (string.Equals(track.ProcessingAction, "FLAC rippen", StringComparison.OrdinalIgnoreCase) ||
             track.DiscNumber.HasValue))
        {
            if (track.Channels != 2)
            {
                track.Channels = 2;
                changed = true;
            }

            if (!string.Equals(track.ChannelLayout, "Stereo", StringComparison.Ordinal))
            {
                track.ChannelLayout = "Stereo";
                changed = true;
            }
        }
        else if (track.Channels.HasValue)
        {
            var channelLayout = AudioProcessingService.FormatChannelLayout(track.Channels);
            if (!string.Equals(track.ChannelLayout, channelLayout, StringComparison.Ordinal))
            {
                track.ChannelLayout = channelLayout;
                changed = true;
            }
        }

        var durationSeconds = GetDurationSeconds(track);
        if (durationSeconds > 0)
        {
            var bitrateKbps = (int)Math.Round(sourceFile.Length * 8d / durationSeconds / 1000d);
            if (track.BitrateKbps != bitrateKbps)
            {
                track.BitrateKbps = bitrateKbps;
                changed = true;
            }
        }

        return changed;
    }


    private static bool ClearMissingSourceMetadata(TrackInfo track)
    {
        var changed = false;

        if (track.BitrateKbps.HasValue)
        {
            track.BitrateKbps = null;
            changed = true;
        }

        // Bei noch nicht gerippten Audio-CD-Tracks sind FLAC und Stereo erwartete
        // Projekteigenschaften. Bei lokalen, fehlenden Quellen sind Kanalwerte
        // dagegen gemessene Dateieigenschaften und dürfen nicht stehen bleiben.
        var isPendingAudioDiscTrack = track.DiscNumber.HasValue ||
            string.Equals(track.ProcessingAction, "FLAC rippen", StringComparison.OrdinalIgnoreCase);
        if (!isPendingAudioDiscTrack)
        {
            if (track.Channels.HasValue)
            {
                track.Channels = null;
                changed = true;
            }

            if (!string.IsNullOrEmpty(track.ChannelLayout))
            {
                track.ChannelLayout = string.Empty;
                changed = true;
            }
        }

        return changed;
    }

    private static double GetDurationSeconds(TrackInfo track)
    {
        if (track.DurationTicks is > 0)
            return TimeSpan.FromTicks(track.DurationTicks.Value).TotalSeconds;

        if (string.IsNullOrWhiteSpace(track.Duration))
            return 0d;

        var parts = track.Duration.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var minutes) &&
            double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return (minutes * 60d) + seconds;
        }

        return TimeSpan.TryParse(track.Duration, System.Globalization.CultureInfo.InvariantCulture, out var duration)
            ? duration.TotalSeconds
            : 0d;
    }


    private static string ResolveConvertedPath(
        TrackStateUpdateContext context,
        TrackInfo track,
        string sourcePath,
        ConvertedTrackState? convertedState)
    {
        // Keep the expected final path even when only its .part file exists.
        // GetConvertedFile decides whether the active workflow may observe that
        // temporary file; idle refreshes still ignore it and clear stale state.
        if (convertedState is not null &&
            !string.IsNullOrWhiteSpace(convertedState.ConvertedPath))
        {
            return convertedState.ConvertedPath;
        }

        if (string.Equals(track.PreparedConvertedPreset, context.SelectedPreset, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(track.PreparedConvertedPath))
        {
            return track.PreparedConvertedPath;
        }

        if (string.IsNullOrWhiteSpace(context.ProjectWorkFolder) ||
            string.IsNullOrWhiteSpace(sourcePath))
        {
            return string.Empty;
        }

        try
        {
            var preset = ExportPreset.Parse(context.SelectedPreset);
            var convertedFolder = ProjectFolderLayout.GetConvertedPresetFolder(
                context.ProjectWorkFolder,
                preset.GetFolderName());
            var expectedPath = ConvertedTrackPathService.GetConvertedTrackPath(
                convertedFolder,
                sourcePath,
                track);

            return expectedPath;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Dictionary<string, ConvertedTrackState> LoadConvertedEntries(string projectWorkFolder, string selectedPreset)
    {
        if (string.IsNullOrWhiteSpace(projectWorkFolder))
            return new Dictionary<string, ConvertedTrackState>(StringComparer.OrdinalIgnoreCase);

        var manifestPaths = new[]
        {
            ProjectFolderLayout.ResolveExportManifestPath(projectWorkFolder),
            ProjectFolderLayout.ResolveWorkManifestPath(projectWorkFolder)
        };

        foreach (var manifestPath in manifestPaths)
        {
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                var manifest = JsonSerializer.Deserialize<ExportWorkManifest>(json);
                if (manifest?.Tracks is not { Count: > 0 })
                    continue;

                var entries = manifest.Tracks
                    .Where(entry => string.Equals(entry.Preset, selectedPreset, StringComparison.Ordinal) &&
                                    !string.IsNullOrWhiteSpace(entry.SourcePath))
                    .GroupBy(entry => NormalizePath(entry.SourcePath), StringComparer.OrdinalIgnoreCase)
                    .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                    .ToDictionary(
                        group => group.Key,
                        group =>
                        {
                            var entry = group.Last();
                            return new ConvertedTrackState(entry.ConvertedPath ?? string.Empty, entry.Status ?? string.Empty);
                        },
                        StringComparer.OrdinalIgnoreCase);

                if (entries.Count > 0)
                    return entries;
            }
            catch
            {
                // Ein unpassendes oder beschädigtes Manifest darf den Live-Refresh nicht blockieren.
            }
        }

        return new Dictionary<string, ConvertedTrackState>(StringComparer.OrdinalIgnoreCase);
    }

    private static ObservedFile? GetExistingFile(string path, bool includePartFiles)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (File.Exists(path))
            return new ObservedFile(new FileInfo(path), IsPartFile: false);

        if (!includePartFiles)
            return null;

        var partPath = path + ".part";
        return File.Exists(partPath)
            ? new ObservedFile(new FileInfo(partPath), IsPartFile: true)
            : null;
    }

    private static ObservedFile? GetConvertedFile(string? path, bool includePartFiles)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (File.Exists(path))
            return new ObservedFile(new FileInfo(path), IsPartFile: false);

        if (!includePartFiles)
            return null;

        var partPath = path + ".part";
        return File.Exists(partPath)
            ? new ObservedFile(new FileInfo(partPath), IsPartFile: true)
            : null;
    }

    private static bool SetWarningMarker(TrackInfo track, string marker, bool shouldBePresent)
    {
        var warnings = (track.Warning ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.Equals(item, marker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (shouldBePresent)
            warnings.Add(marker);

        var updated = string.Join("; ", warnings);
        if (string.Equals(track.Warning, updated, StringComparison.Ordinal))
            return false;

        track.Warning = updated;
        return true;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static double ToMegabytes(long bytes) => Math.Round(bytes / 1024d / 1024d, 2);

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) < 0.005d;

    private sealed record ConvertedTrackState(string ConvertedPath, string Status);
    private sealed record ObservedFile(FileInfo File, bool IsPartFile);
}

public sealed record TrackStateUpdateContext(
    IReadOnlyList<TrackInfo> Tracks,
    string ProjectWorkFolder,
    string SelectedPreset,
    bool IsWorkflowActive,
    bool IsRefreshSuspended = false);
