using BookStitch.Models;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace BookStitch.Services;

public sealed class TrackStateUpdateQueueService : IDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan ActiveRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _timer;
    private readonly Func<TrackStateUpdateContext> _contextProvider;
    private readonly Action _onTracksChanged;
    private readonly bool _isEnabled;
    private readonly PerformanceTraceService? _trace;
    private readonly TimeProvider _timeProvider;
    private long? _lastActiveRefresh;
    private bool _finalRefreshRequested;
    private int _refreshRequested;
    private bool _isTickRunning;
    private bool _wasWorkflowActive;

    public TrackStateUpdateQueueService(
        Func<TrackStateUpdateContext> contextProvider,
        Action onTracksChanged,
        TimeSpan? interval = null,
        bool isEnabled = true,
        PerformanceTraceService? trace = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(contextProvider);
        ArgumentNullException.ThrowIfNull(onTracksChanged);

        _contextProvider = contextProvider;
        _onTracksChanged = onTracksChanged;
        _isEnabled = isEnabled;
        _trace = trace;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        if (_wasWorkflowActive && !context.IsWorkflowActive)
            _finalRefreshRequested = true;
        if (!_wasWorkflowActive && context.IsWorkflowActive)
            _lastActiveRefresh = null;
        _wasWorkflowActive = context.IsWorkflowActive;
        if (context.IsRefreshSuspended || context.PipelineState == ProjectPipelineState.Merging)
        {
            return;
        }

        var now = _timeProvider.GetTimestamp();
        // Active requests share the safety interval; progress bursts cannot cause full scans every tick.
        var shouldApplyUpdates = _finalRefreshRequested || (context.IsWorkflowActive
            ? _lastActiveRefresh is null || _timeProvider.GetElapsedTime(_lastActiveRefresh.Value, now) >= ActiveRefreshInterval
            : Volatile.Read(ref _refreshRequested) == 1);
        if (!shouldApplyUpdates)
            return;

        Interlocked.Exchange(ref _refreshRequested, 0);
        _finalRefreshRequested = false;
        if (context.IsWorkflowActive)
            _lastActiveRefresh = now;
        _isTickRunning = true;
        try
        {
            if (ApplyUpdates(context, _trace))
                _onTracksChanged();
        }
        finally
        {
            _isTickRunning = false;
        }
    }

    public static bool ApplyUpdates(TrackStateUpdateContext context, PerformanceTraceService? trace = null)
    {
        using var measurement = PerformanceMeasurement.Start(
            context.IsWorkflowActive ? "track-state.refresh.active" : "track-state.refresh.requested-or-final", trace);
        measurement?.Add("TracksChecked", 0);
        if (measurement is not null)
        {
            measurement.Add("SourceFileChecks", 0);
            measurement.Add("ConvertedFileChecks", 0);
            measurement.Add("PreparedFileChecks", 0);
            measurement.Add("ManifestReads", 0);
            measurement.Add("PreparedStateReads", 0);
            measurement.Add("ChangedCycles", 0);
        }
        if (context.Tracks.Count == 0)
            return false;

        var convertedBySourcePath = LoadConvertedEntries(context.ProjectWorkFolder, context.SelectedPreset, measurement, trace);
        var preset = ExportPreset.Parse(context.SelectedPreset);
        var changed = false;

        foreach (var track in context.Tracks)
        {
            measurement?.Add("TracksChecked");
            var sourcePath = NormalizePath(track.FilePath);
            var sourceFile = GetExistingFile(sourcePath, context.IsWorkflowActive, measurement);
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
                    TrackWarningCodes.EmptySource,
                    LocalizationText.Get("WarningEmptySourceSentence"),
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
            var convertedFile = GetConvertedFile(resolvedConvertedPath, context.IsWorkflowActive, measurement);
            var convertedSizeAvailable = convertedFile is not null;
            if (track.ConvertedSizeAvailable != convertedSizeAvailable)
            {
                track.ConvertedSizeAvailable = convertedSizeAvailable;
                changed = true;
            }

            if (SetWarningMarker(
                    track,
                    TrackWarningCodes.EmptyAac,
                    LocalizationText.Get("WarningEmptyAacSentence"),
                    convertedFile is { IsPartFile: false } && convertedFile.File.Length == 0))
            {
                changed = true;
            }

            if (sourceFile is not null &&
                sourceFile.File.Length > 0 &&
                ProcessingActionIds.Normalize(track.ProcessingAction) == ProcessingActionIds.RipFlac)
            {
                track.ProcessingAction = AudioProcessingService.DetermineProcessingAction(track, preset);
                changed = true;
            }
        }

        var previousPrepared = context.Tracks.Select(track =>
            (track.PreparedConvertedPath, track.PreparedConvertedPreset, track.HasReusableConvertedFile, track.ConvertedSizeAvailable)).ToArray();
        new TrackPreparedStateRefreshService().RefreshForCurrentPreset(context.Tracks, context.ProjectWorkFolder, context.SelectedPreset, measurement, trace);
        for (var index = 0; index < context.Tracks.Count; index++)
        {
            var track = context.Tracks[index];
            if (context.IsWorkflowActive && previousPrepared[index].ConvertedSizeAvailable)
                track.ConvertedSizeAvailable = true; // Partial-size observation is separate from preparation evidence.
            changed |= previousPrepared[index] !=
                (track.PreparedConvertedPath, track.PreparedConvertedPreset, track.HasReusableConvertedFile, track.ConvertedSizeAvailable);
        }
        if (changed) measurement?.Add("ChangedCycles");
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

        var codec = string.IsNullOrWhiteSpace(track.Codec)
            ? extension switch
            {
                ".flac" => "FLAC",
                ".mp3" => "MP3",
                ".m4a" or ".m4b" or ".aac" => "AAC",
                ".wav" => "PCM",
                ".opus" => "Opus",
                _ => track.Codec
            }
            : track.Codec;

        if (!string.IsNullOrWhiteSpace(codec) &&
            !string.Equals(track.Codec, codec, StringComparison.OrdinalIgnoreCase))
        {
            track.Codec = codec;
            changed = true;
        }

        // Audio-CD-Arbeitsdateien sind immer zweikanalige FLAC-Dateien.
        if (extension == ".flac" &&
            (ProcessingActionIds.Normalize(track.ProcessingAction) == ProcessingActionIds.RipFlac ||
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

        // Preserve measured source bitrate (including the initial tag read).
        // Container size includes tags/covers and is only a fallback for missing metadata.
        var durationSeconds = GetDurationSeconds(track);
        if (durationSeconds > 0 && track.BitrateKbps is not > 0)
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
            ProcessingActionIds.Normalize(track.ProcessingAction) == ProcessingActionIds.RipFlac;
        if (!isPendingAudioDiscTrack)
        {
            if (track.SampleRateHz.HasValue)
            {
                track.SampleRateHz = null;
                changed = true;
            }

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

    private static Dictionary<string, ConvertedTrackState> LoadConvertedEntries(string projectWorkFolder, string selectedPreset,
        PerformanceMeasurement? refresh, PerformanceTraceService? trace)
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
                using var read = PerformanceMeasurement.Start("manifest.read.live-refresh", trace);
                refresh?.Add("ManifestReads");
                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                read?.FileBytes(manifestPath);
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

    private static ObservedFile? GetExistingFile(string path, bool includePartFiles, PerformanceMeasurement? measurement)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        measurement?.Add("SourceFileChecks");
        if (File.Exists(path))
            return new ObservedFile(new FileInfo(path), IsPartFile: false);

        if (!includePartFiles)
            return null;

        var partPath = path + ".part";
        measurement?.Add("SourceFileChecks");
        return File.Exists(partPath)
            ? new ObservedFile(new FileInfo(partPath), IsPartFile: true)
            : null;
    }

    private static ObservedFile? GetConvertedFile(string? path, bool includePartFiles, PerformanceMeasurement? measurement)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        measurement?.Add("ConvertedFileChecks");
        if (File.Exists(path))
            return new ObservedFile(new FileInfo(path), IsPartFile: false);

        if (!includePartFiles)
            return null;

        var partPath = path + ".part";
        measurement?.Add("ConvertedFileChecks");
        return File.Exists(partPath)
            ? new ObservedFile(new FileInfo(partPath), IsPartFile: true)
            : null;
    }

    private static bool SetWarningMarker(TrackInfo track, TrackWarningCodes code, string marker, bool shouldBePresent)
    {
        var existingCodes = track.WarningCodes;
        var warnings = (track.Warning ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !LegacyTrackWarningReader.Read(item).HasFlag(code) &&
                           !string.Equals(item, marker, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (shouldBePresent)
            warnings.Add(marker);

        var updated = string.Join("; ", warnings);
        var updatedCodes = shouldBePresent ? existingCodes | code : existingCodes & ~code;
        if (string.Equals(track.Warning, updated, StringComparison.Ordinal) && track.WarningCodes == updatedCodes)
            return false;

        track.SetWarning(updated, updatedCodes);
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
