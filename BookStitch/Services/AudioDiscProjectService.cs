using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Models.TrackOrdering;
using BookStitch.Services.TrackOrdering;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BookStitch.Services;

public sealed class AudioDiscProjectService
{
    public const string ManifestFileName = ProjectFolderLayout.AudioDiscManifestFileName;
    public const string OriginalsFolderName = ProjectFolderLayout.OriginalsFolderName;
    public const string RippedFolderName = OriginalsFolderName; // Legacy API name; new projects use originals.

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly AtomicJsonStore JsonStore = new();

    public string CreateProjectFolder(string audioDiscProjectsFolder, string bookTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioDiscProjectsFolder);

        return new ProjectFolderNameService().CreateUniqueProjectFolder(
            audioDiscProjectsFolder,
            bookTitle,
            "Audio-CD");
    }

    public AudioDiscProjectManifest CreateInitialManifest(
        string projectFolder,
        AudioDiscInfo disc,
        int discNumber,
        DiscProjectSetupResult setup,
        AudioDiscWorkingFormat workingFormat,
        DiscDriveInfo? sourceDriveInfo = null,
        int targetSampleRateHz = 44100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(disc);
        ArgumentNullException.ThrowIfNull(setup);

        var now = DateTime.UtcNow;
        var manifest = new AudioDiscProjectManifest
        {
            ProjectFolder = Path.GetFullPath(projectFolder),
            ProjectFormatVersion = ProjectFormat.CurrentProjectFormatVersion,
            ProjectId = Guid.NewGuid().ToString("N"),
            SourceDriveRoot = disc.DriveRoot,
            SourceDriveName = sourceDriveInfo?.DiagnosticDriveName ?? string.Empty,
            SourceDriveDevicePath = sourceDriveInfo?.DevicePath ?? string.Empty,
            SourceVolumeLabel = sourceDriveInfo?.VolumeLabel ?? string.Empty,
            DiscIdentity = disc.DiscIdentity,
            TotalDiscs = setup.TotalDiscs,
            WorkingFormat = workingFormat.ToString(),
            ExportPreset = setup.SelectedExportPreset,
            TargetSampleRateHz = SampleRateSettingsService.Normalize(targetSampleRateHz),
            ParallelJobs = setup.ParallelJobs,
            OutputExtension = setup.OutputExtension,
            OutputFolder = setup.OutputFolder,
            FileNameTemplate = setup.FileNameTemplate,
            CoverSourcePath = setup.CoverSourcePath,
            ProcessedCoverPath = setup.ProcessedCoverPath,
            CreatedUtc = now,
            UpdatedUtc = now,
            RawReadAddressingVersion = AudioDiscRawReadAddressingVersions.Current
        };

        manifest.Discs.Add(BuildDiscEntryCore(
            manifest,
            disc,
            discNumber,
            startingGlobalIndex: 1,
            projectTitle: setup.BookTitle,
            sourceDriveInfo: sourceDriveInfo));
        return manifest;
    }

    public AudioDiscProjectManifestDisc BuildDiscEntry(
        AudioDiscProjectManifest manifest,
        AudioDiscInfo disc,
        int discNumber,
        int startingGlobalIndex,
        DiscDriveInfo? sourceDriveInfo = null,
        int targetSampleRateHz = 44100)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var metadata = ResolveProjectMetadata(manifest);
        return BuildDiscEntryCore(
            manifest,
            disc,
            discNumber,
            startingGlobalIndex,
            metadata.Title,
            sourceDriveInfo);
    }

    private static AudioDiscProjectManifestDisc BuildDiscEntryCore(
        AudioDiscProjectManifest manifest,
        AudioDiscInfo disc,
        int discNumber,
        int startingGlobalIndex,
        string? projectTitle,
        DiscDriveInfo? sourceDriveInfo = null,
        int targetSampleRateHz = 44100)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(disc);
        if (discNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(discNumber));
        if (startingGlobalIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(startingGlobalIndex));

        var normalizedTitle = projectTitle?.Trim() ?? string.Empty;
        var extension = GetWorkingExtension(manifest.WorkingFormat);
        var titleStem = MakeSafeFileName(normalizedTitle);
        var orderedTracks = disc.Tracks
            .OrderBy(track => track.TrackNumber)
            .ToList();

        var tracks = orderedTracks
            .Select((track, offset) =>
            {
                var globalIndex = startingGlobalIndex + offset;
                var nextSectorOffset = offset + 1 < orderedTracks.Count
                    ? orderedTracks[offset + 1].SectorOffset
                    : disc.Toc?.LeadOutSectorOffset;
                var sectorCount = track.SectorOffset.HasValue && nextSectorOffset.HasValue
                    ? Math.Max(0, nextSectorOffset.Value - track.SectorOffset.Value)
                    : 0;
                var fileStem = string.IsNullOrWhiteSpace(titleStem)
                    ? $"{globalIndex:000}_Track_{track.TrackNumber:00}"
                    : $"{globalIndex:000}_{titleStem}";

                return new AudioDiscProjectManifestTrack
                {
                    SourceId = Guid.NewGuid(),
                    GlobalIndex = globalIndex,
                    DiscNumber = discNumber,
                    TrackNumber = track.TrackNumber,
                    TrackIdentity = track.TrackIdentity,
                    StartPosition = track.StartPosition,
                    Duration = track.Duration,
                    SectorOffset = track.SectorOffset,
                    SectorCount = sectorCount,
                    FileName = fileStem + extension,
                    RelativePath = Path.Combine(ProjectFolderLayout.OriginalsFolderName, $"CD {discNumber:00}", fileStem + extension),
                    Status = AudioDiscTrackStatus.Pending
                };
            })
            .ToList();

        return new AudioDiscProjectManifestDisc
        {
            DiscNumber = discNumber,
            DiscIdentity = disc.DiscIdentity,
            SourceDriveRoot = disc.DriveRoot,
            SourceDriveName = sourceDriveInfo?.DiagnosticDriveName ?? string.Empty,
            SourceDriveDevicePath = sourceDriveInfo?.DevicePath ?? string.Empty,
            SourceVolumeLabel = sourceDriveInfo?.VolumeLabel ?? string.Empty,
            TrackCount = tracks.Count,
            TotalDuration = disc.TotalDuration,
            Tracks = tracks
        };
    }

    public AudioDiscProjectManifestDisc AddDisc(
        AudioDiscProjectManifest manifest,
        AudioDiscInfo disc,
        int discNumber,
        DiscDriveInfo? sourceDriveInfo = null,
        bool acceptDuplicate = false,
        AudioDiscProjectManifestDisc? preparedDisc = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(disc);

        if (discNumber < 1 || discNumber > manifest.TotalDiscs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(discNumber),
                discNumber,
                $"Die Disc-Nummer muss zwischen 1 und {manifest.TotalDiscs} liegen.");
        }

        var existingNumber = manifest.Discs.FirstOrDefault(item => item.DiscNumber == discNumber);
        if (existingNumber is not null)
        {
            if (SameIdentity(existingNumber.DiscIdentity, disc.DiscIdentity))
                return existingNumber;

            throw new InvalidOperationException(
                $"Disc {discNumber} ist bereits mit einer anderen Audio-CD belegt.");
        }

        var existingIdentity = manifest.Discs.FirstOrDefault(item => SameIdentity(item.DiscIdentity, disc.DiscIdentity));
        if (existingIdentity is not null && !acceptDuplicate)
        {
            throw new InvalidOperationException(
                $"Diese Audio-CD ist bereits als Disc {existingIdentity.DiscNumber} im Projekt enthalten.");
        }

        if (preparedDisc is not null &&
            (preparedDisc.DiscNumber != discNumber || !SameIdentity(preparedDisc.DiscIdentity, disc.DiscIdentity) ||
             preparedDisc.Tracks.Any(track => track.SourceId == Guid.Empty) ||
             !preparedDisc.Tracks.Select(track => track.TrackIdentity).SequenceEqual(
                 disc.Tracks.OrderBy(track => track.TrackNumber).Select(track => track.TrackIdentity))))
            throw new InvalidOperationException("The prepared disc does not match the validated medium.");

        var entry = preparedDisc ?? BuildDiscEntry(
            manifest,
            disc,
            discNumber,
            GetNextGlobalTrackIndex(manifest),
            sourceDriveInfo);
        manifest.Discs.Add(entry);
        if (preparedDisc is not null)
            UpdateDiscSourceDrive(manifest, discNumber, disc, sourceDriveInfo);
        manifest.CompletedUtc = null;
        manifest.ErrorMessage = string.Empty;
        RefreshProgressState(manifest);
        return entry;
    }


    public bool UpdateSnapshot(
        AudioDiscProjectManifest manifest,
        ProjectSnapshotUiState snapshot)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(snapshot);

        var changed = false;
        if (!manifest.IsStoppedForResume)
            changed |= UpdateValue(manifest.ExportPreset, snapshot.SelectedExportPreset, value => manifest.ExportPreset = value);
        changed |= UpdateValue(manifest.ParallelJobs, snapshot.ParallelJobs, value => manifest.ParallelJobs = value);
        changed |= UpdateValue(manifest.OutputExtension, snapshot.OutputExtension, value => manifest.OutputExtension = value);
        changed |= UpdateValue(manifest.OutputFolder, snapshot.OutputFolder, value => manifest.OutputFolder = value);
        changed |= UpdateValue(manifest.FileNameTemplate, snapshot.FileNameTemplate, value => manifest.FileNameTemplate = value);
        changed |= UpdateValue(manifest.CoverSourcePath, snapshot.CoverSourcePath, value => manifest.CoverSourcePath = value);
        changed |= UpdateValue(manifest.ProcessedCoverPath, snapshot.ProcessedCoverPath, value => manifest.ProcessedCoverPath = value);

        return changed;
    }

    public int GetNextGlobalTrackIndex(AudioDiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.Discs
            .SelectMany(disc => disc.Tracks)
            .Select(track => track.GlobalIndex)
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    public int? GetNextRequiredDiscNumber(AudioDiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        for (var discNumber = 1; discNumber <= manifest.TotalDiscs; discNumber++)
        {
            var disc = manifest.Discs.FirstOrDefault(item => item.DiscNumber == discNumber);
            if (disc is null || !IsDiscCompleted(disc))
                return discNumber;
        }

        return null;
    }

    public int CountCompletedDiscs(AudioDiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Discs.Count(IsDiscCompleted);
    }

    public bool IsProjectRipCompleted(AudioDiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.TotalDiscs > 0 &&
               GetNextRequiredDiscNumber(manifest) is null;
    }

    public void CompleteDiscImportEarly(AudioDiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var completedDiscNumbers = manifest.Discs
            .Where(IsDiscCompleted)
            .Select(disc => disc.DiscNumber)
            .OrderBy(number => number)
            .ToArray();
        if (completedDiscNumbers.Length == 0)
            throw new InvalidOperationException("At least one completed disc is required.");

        manifest.TotalDiscs = completedDiscNumbers.Max();
        RefreshProgressState(manifest);
    }

    public bool IsDiscCompleted(AudioDiscProjectManifestDisc disc)
    {
        ArgumentNullException.ThrowIfNull(disc);

        return string.Equals(disc.Status, AudioDiscStatus.Completed, StringComparison.OrdinalIgnoreCase) ||
               (disc.Tracks.Count > 0 && disc.Tracks.All(track =>
                   string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase)));
    }

    public void UpdateDiscSourceDrive(
        AudioDiscProjectManifest manifest,
        int discNumber,
        AudioDiscInfo disc,
        DiscDriveInfo? driveInfo)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(disc);

        var manifestDisc = manifest.Discs.FirstOrDefault(item => item.DiscNumber == discNumber)
            ?? throw new InvalidOperationException($"Disc {discNumber} ist im Audio-CD-Projekt nicht vorhanden.");

        if (!string.Equals(manifestDisc.DiscIdentity, disc.DiscIdentity, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Die eingelegte Audio-CD entspricht nicht Disc {discNumber}.");

        manifestDisc.SourceDriveRoot = disc.DriveRoot;
        manifestDisc.SourceDriveName = driveInfo?.DiagnosticDriveName ?? string.Empty;
        manifestDisc.SourceDriveDevicePath = driveInfo?.DevicePath ?? string.Empty;
        manifestDisc.SourceVolumeLabel = driveInfo?.VolumeLabel ?? string.Empty;

        manifest.SourceDriveRoot = disc.DriveRoot;
        manifest.SourceDriveName = manifestDisc.SourceDriveName;
        manifest.SourceDriveDevicePath = manifestDisc.SourceDriveDevicePath;
        manifest.SourceVolumeLabel = manifestDisc.SourceVolumeLabel;
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkDiscRipping(AudioDiscProjectManifest manifest, int discNumber)
    {
        var disc = GetDisc(manifest, discNumber);
        disc.Status = AudioDiscStatus.Ripping;
        disc.CompletedUtc = null;
        disc.ErrorMessage = string.Empty;
        manifest.Status = AudioDiscProjectStatus.Ripping;
        manifest.PipelineState = ProjectPipelineStateNames.AcquiringSources;
        manifest.CompletedUtc = null;
        manifest.ErrorMessage = string.Empty;
    }

    public void MarkDiscCompleted(
        AudioDiscProjectManifest manifest,
        int discNumber,
        TimeSpan? ripDuration = null)
    {
        var disc = GetDisc(manifest, discNumber);
        if (disc.Tracks.Count == 0 || disc.Tracks.Any(track =>
                !string.Equals(track.Status, AudioDiscTrackStatus.Ripped, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Disc {discNumber} kann erst abgeschlossen werden, wenn alle Tracks vollständig gerippt sind.");
        }

        var now = DateTime.UtcNow;
        disc.Status = AudioDiscStatus.Completed;
        disc.RipDuration = ripDuration ?? disc.RipDuration;
        disc.CompletedUtc = now;
        disc.ErrorMessage = string.Empty;
        manifest.RipDuration = manifest.Discs
            .Where(item => item.RipDuration.HasValue)
            .Aggregate(TimeSpan.Zero, (total, item) => total + item.RipDuration!.Value);
        RefreshProgressState(manifest, now);
    }

    public void MarkDiscFailed(AudioDiscProjectManifest manifest, int discNumber, string? errorMessage)
    {
        var disc = GetDisc(manifest, discNumber);
        disc.Status = AudioDiscStatus.Failed;
        disc.CompletedUtc = null;
        disc.ErrorMessage = errorMessage?.Trim() ?? string.Empty;
        manifest.Status = AudioDiscProjectStatus.Failed;
        manifest.PipelineState = ProjectPipelineStateNames.AcquiringSources;
        manifest.CompletedUtc = null;
        manifest.ErrorMessage = disc.ErrorMessage;
    }

    public void MarkProjectCanceled(AudioDiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        foreach (var disc in manifest.Discs.Where(item =>
                     string.Equals(item.Status, AudioDiscStatus.Ripping, StringComparison.OrdinalIgnoreCase)))
        {
            disc.Status = AudioDiscStatus.Pending;
            disc.CompletedUtc = null;
            disc.ErrorMessage = string.Empty;
        }

        manifest.Status = AudioDiscProjectStatus.Canceled;
        manifest.PipelineState = IsProjectRipCompleted(manifest)
            ? ProjectPipelineStateNames.Converting
            : ProjectPipelineStateNames.Preparing;
        manifest.CompletedUtc = null;
        manifest.ErrorMessage = string.Empty;
    }

    public void MarkExportStarted(AudioDiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        manifest.ExportStatus = AudioDiscExportStatus.Exporting;
        manifest.PipelineState = ProjectPipelineStateNames.Converting;
        manifest.ExportStartedUtc ??= DateTime.UtcNow;
        manifest.ExportCompletedUtc = null;
        manifest.FinalOutputPath = string.Empty;
        manifest.ExportErrorMessage = string.Empty;
    }

    public void MarkExportPausedBeforeMerge(AudioDiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        manifest.ExportStatus = AudioDiscExportStatus.PausedBeforeMerge;
        manifest.PipelineState = ProjectPipelineStateNames.ReviewBeforeMerge;
        manifest.ExportCompletedUtc = null;
        manifest.ExportErrorMessage = string.Empty;
    }

    public void MarkExportCompleted(AudioDiscProjectManifest manifest, string? outputPath)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.IsStoppedForResume = false;

        var completedUtc = DateTime.UtcNow;
        var normalizedOutputPath = outputPath?.Trim() ?? string.Empty;
        manifest.ExportStatus = AudioDiscExportStatus.Completed;
        manifest.PipelineState = ProjectPipelineStateNames.Completed;
        manifest.ExportStartedUtc ??= completedUtc;
        manifest.ExportCompletedUtc = completedUtc;
        manifest.HasSuccessfulExport = true;
        manifest.LastSuccessfulExportUtc = completedUtc;
        manifest.LastSuccessfulOutputPath = normalizedOutputPath;
        manifest.FinalOutputPath = normalizedOutputPath;
        manifest.ExportErrorMessage = string.Empty;
    }

    public void IncreaseTotalDiscsForAdditionalRip(AudioDiscProjectManifest manifest, int newTotalDiscs)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var completedDiscs = CountCompletedDiscs(manifest);
        var minimum = Math.Max(manifest.TotalDiscs + 1, completedDiscs + 1);
        if (newTotalDiscs < minimum || newTotalDiscs > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newTotalDiscs),
                newTotalDiscs,
                $"Die Gesamtzahl muss zwischen {minimum} und 99 liegen.");
        }

        manifest.TotalDiscs = newTotalDiscs;
        manifest.Status = AudioDiscProjectStatus.WaitingForDisc;
        manifest.PipelineState = ProjectPipelineStateNames.AcquiringSources;
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkExportCanceled(AudioDiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        manifest.ExportStatus = AudioDiscExportStatus.Canceled;
        manifest.PipelineState = IsProjectRipCompleted(manifest)
            ? ProjectPipelineStateNames.ReviewBeforeMerge
            : ProjectPipelineStateNames.AcquiringSources;
        manifest.ExportCompletedUtc = null;
        manifest.ExportErrorMessage = string.Empty;
    }

    public void MarkExportFailed(AudioDiscProjectManifest manifest, string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        manifest.ExportStatus = AudioDiscExportStatus.Failed;
        manifest.PipelineState = IsProjectRipCompleted(manifest)
            ? ProjectPipelineStateNames.ReviewBeforeMerge
            : ProjectPipelineStateNames.AcquiringSources;
        manifest.ExportCompletedUtc = null;
        manifest.ExportErrorMessage = errorMessage?.Trim() ?? string.Empty;
    }

    public void RefreshProgressState(AudioDiscProjectManifest manifest) =>
        RefreshProgressState(manifest, DateTime.UtcNow);

    public IReadOnlyList<TrackInfo> CreateTrackPreview(
        AudioDiscInfo disc,
        string title,
        string author,
        AudioDiscWorkingFormat workingFormat)
    {
        ArgumentNullException.ThrowIfNull(disc);

        var normalizedTitle = title?.Trim() ?? string.Empty;
        var normalizedAuthor = author?.Trim() ?? string.Empty;
        var extension = GetWorkingExtension(workingFormat.ToString());

        var tracks = disc.Tracks
            .OrderBy(track => track.TrackNumber)
            .Select((track, offset) =>
            {
                var globalIndex = offset + 1;
                var fileStem = string.IsNullOrWhiteSpace(normalizedTitle)
                    ? $"{globalIndex:000}_Track_{track.TrackNumber:00}"
                    : $"{globalIndex:000}_{MakeSafeFileName(normalizedTitle)}";
                var chapterTitle = BuildDefaultChapterTitle(globalIndex, normalizedTitle);

                return new TrackInfo
                {
                    Index = globalIndex,
                    DiscNumber = 1,
                    TrackNumber = track.TrackNumber,
                    FilePath = Path.Combine(disc.DriveRoot, fileStem + extension),
                    FileName = fileStem + extension,
                    RelativeFolder = $"CD-Laufwerk {disc.DriveRoot}",
                    HideFolderDisplay = true,
                    Extension = "Audio-CD",
                    Artist = normalizedAuthor,
                    ChapterTitle = chapterTitle,
                    Duration = FormatDuration(track.Duration),
                    DurationTicks = track.Duration.Ticks,
                    BitrateKbps = 1411,
                SampleRateHz = 44100,
                    Channels = 2,
                    ChannelLayout = "Stereo",
                    SizeMb = CalculatePcmSourceSizeMb(track.Duration),
                    Codec = "PCM",
                    ProcessingAction = ProcessingActionIds.RipFlac,
                    AudioValidationPassed = null
                };
            })
            .ToList();
        new TrackAutomaticOrderApplicationService().CalculateAndApply(
            tracks,
            TrackOrderProjectType.AudioDisc,
            TrackOrderConfirmedContextFactory.CreateAudioDiscContexts(tracks));
        return tracks;
    }

    public IReadOnlyList<TrackInfo> CreateTrackPreview(
        AudioDiscProjectManifest manifest,
        AudioDiscProjectManifestDisc? preparedDisc = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var metadata = ResolveProjectMetadata(manifest);
        var author = metadata.Author;
        var title = metadata.Title;
        var tracks = manifest.Discs.Concat(preparedDisc is null ? [] : new[] { preparedDisc })
            .OrderBy(disc => disc.DiscNumber)
            .SelectMany(disc => disc.Tracks
                .OrderBy(track => track.GlobalIndex)
                .Select(track => (Disc: disc, Track: track)))
            .Select(item =>
            {
                var disc = item.Disc;
                var track = item.Track;
                return new TrackInfo
                {
                    SourceId = track.SourceId,
                    Index = track.GlobalIndex,
                DiscNumber = track.DiscNumber,
                TrackNumber = track.TrackNumber,
                FilePath = Path.Combine(manifest.ProjectFolder, track.RelativePath),
                FileName = track.FileName,
                RelativeFolder = $"CD-Laufwerk {disc.SourceDriveRoot}",
                HideFolderDisplay = true,
                Extension = "Audio-CD",
                TagTitle = string.Empty,
                Artist = author,
                ChapterTitle = BuildDefaultChapterTitle(track.GlobalIndex, title),
                Duration = FormatDuration(track.Duration),
                DurationTicks = track.Duration.Ticks,
                BitrateKbps = 1411,
                SampleRateHz = 44100,
                Channels = 2,
                ChannelLayout = "Stereo",
                SizeMb = CalculatePcmSourceSizeMb(track.Duration),
                Codec = "PCM",
                ProcessingAction = ProcessingActionIds.RipFlac,
                AudioValidationPassed = null
                };
            })
            .ToList();
        new TrackAutomaticOrderApplicationService().CalculateAndApply(
            tracks,
            TrackOrderProjectType.AudioDisc,
            TrackOrderConfirmedContextFactory.CreateAudioDiscContexts(tracks));
        return tracks;
    }


    public void ApplyManifestOrderAndMetadataToRippedTracks(
        AudioDiscProjectManifest manifest,
        IList<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tracks);

        if (tracks.Count == 0)
            return;

        var manifestTracksByFileName = manifest.Discs
            .SelectMany(disc => disc.Tracks)
            .GroupBy(track => track.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var unmatched = tracks
            .Where(track => !manifestTracksByFileName.ContainsKey(track.FileName))
            .Select(track => track.FileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Take(3)
            .ToList();
        if (tracks.Any(track => !manifestTracksByFileName.ContainsKey(track.FileName)))
        {
            var suffix = unmatched.Count == 0
                ? string.Empty
                : $" Beispiele: {string.Join(" | ", unmatched)}";
            throw new InvalidOperationException(
                $"Die Audio-CD-Trackliste konnte nicht vollständig gegen das Projektmanifest abgeglichen werden.{suffix}");
        }

        ApplyManifestMetadataToRippedTracks(manifest, tracks);

        var ordered = tracks
            .OrderBy(track => manifestTracksByFileName[track.FileName].GlobalIndex)
            .ToList();
        tracks.Clear();
        foreach (var track in ordered)
            tracks.Add(track);
    }

    public void ApplyManifestMetadataToRippedTracks(
        AudioDiscProjectManifest manifest,
        IEnumerable<TrackInfo> tracks)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(tracks);

        var metadata = ResolveProjectMetadata(manifest);
        var author = metadata.Author;
        var title = metadata.Title;
        var manifestTracks = manifest.Discs
            .SelectMany(disc => disc.Tracks)
            .ToDictionary(track => track.FileName, StringComparer.OrdinalIgnoreCase);

        foreach (var track in tracks)
        {
            if (!manifestTracks.TryGetValue(track.FileName, out var manifestTrack))
                continue;

            track.Index = manifestTrack.GlobalIndex;
            track.SourceId = manifestTrack.SourceId;
            track.DiscNumber = manifestTrack.DiscNumber;
            track.TrackNumber = manifestTrack.TrackNumber;
            track.AutomaticOrderPosition = manifestTrack.GlobalIndex;
            track.HideFolderDisplay = true;
            track.TagTitle = string.Empty;
            track.Artist = author;
            track.ChapterTitle = BuildDefaultChapterTitle(manifestTrack.GlobalIndex, title);

            if (track.AudioValidationPassed == false ||
                track.WarningCodes.HasFlag(TrackWarningCodes.InvalidAudio) ||
                CodecIds.IsInvalid(track.Codec) ||
                ProcessingActionIds.Normalize(track.ProcessingAction) == ProcessingActionIds.Invalid)
            {
                continue;
            }

            track.Warning = string.Empty;
        }
    }

    public AudioDiscProjectManifest? TryLoad(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            return null;

        var path = ProjectFolderLayout.ResolveAudioDiscManifestPath(projectFolder);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var schema = PersistentJsonSchema.Read(
                json,
                "Audio-CD-Projektmanifest",
                path,
                AudioDiscProjectManifestVersions.CurrentSchemaVersion);
            PersistentJsonSchema.RequireState(json, path, "Discs");
            var manifest = JsonSerializer.Deserialize<AudioDiscProjectManifest>(json, JsonOptions);
            if (manifest is null)
                throw new PersistentJsonSchemaException(LocalizationText.Get("Persistence.Manifest.Invalid"));

            var before = JsonSerializer.Serialize(manifest, JsonOptions);
            manifest.ProjectFolder = Path.GetFullPath(projectFolder);
            var normalized = NormalizeManifest(manifest);
            ProjectSourceIntegrityService.Reconcile(manifest);
            RefreshProgressState(manifest);

            return manifest;
        }
        catch (PersistentJsonSchemaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(LocalizationText.Get("Persistence.Manifest.Unreadable"), ex);
        }
    }

    internal AudioDiscProjectManifest? LoadForResume(string projectFolder)
    {
        var path = ProjectFolderLayout.GetAudioDiscManifestPath(projectFolder);
        lock (AtomicJsonStore.ForPath(path))
        {
            var manifest = TryLoad(projectFolder);
            if (manifest is not null && !System.Text.Json.Nodes.JsonNode.DeepEquals(
                    System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)), JsonSerializer.SerializeToNode(manifest, JsonOptions)))
                Save(manifest);
            return manifest;
        }
    }

    public void Save(AudioDiscProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.ProjectFolder);

        NormalizeManifest(manifest);
        manifest.ProjectFormatVersion = ProjectFormat.CurrentProjectFormatVersion;
        if (string.IsNullOrWhiteSpace(manifest.ProjectId)) manifest.ProjectId = Guid.NewGuid().ToString("N");
        RefreshProgressState(manifest);
        ProjectFolderLayout.EnsureProjectFolders(manifest.ProjectFolder);
        foreach (var disc in manifest.Discs)
            Directory.CreateDirectory(ProjectFolderLayout.GetDiscOriginalsFolder(manifest.ProjectFolder, Math.Max(1, disc.DiscNumber)));
        manifest.UpdatedUtc = DateTime.UtcNow;

        var path = ProjectFolderLayout.GetAudioDiscManifestPath(manifest.ProjectFolder);
        JsonStore.Save(path, manifest, JsonOptions);
    }

    private static string BuildDefaultChapterTitle(int globalIndex, string? projectTitle)
    {
        var normalizedTitle = projectTitle?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedTitle)
            ? $"{globalIndex:000} {ChapterFallbackLabelProvider.GetLabel()}"
            : $"{globalIndex:000} {normalizedTitle}";
    }

    private static ProjectMetadataProjection ResolveProjectMetadata(AudioDiscProjectManifest manifest)
    {
        return new ProjectMetadataMigrationService().LoadAuthoritativeProjectionOrLegacy(
            manifest.ProjectFolder,
            audioDiscManifest: manifest);
    }

    private static AudioDiscProjectManifestDisc GetDisc(
        AudioDiscProjectManifest manifest,
        int discNumber)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Discs.FirstOrDefault(disc => disc.DiscNumber == discNumber)
            ?? throw new InvalidOperationException($"Disc {discNumber} ist im Audio-CD-Projekt nicht vorhanden.");
    }

    internal AudioDiscProjectManifest NormalizeForUpgrade(AudioDiscProjectManifest manifest, string projectFolder)
    {
        manifest.ProjectFolder = projectFolder;
        NormalizeManifest(manifest);
        RefreshProgressState(manifest);
        return manifest;
    }

    private static bool NormalizeManifest(AudioDiscProjectManifest manifest)
    {
        var before = JsonSerializer.Serialize(manifest, JsonOptions);
        var changed = false;

        manifest.SchemaVersion = AudioDiscProjectManifestVersions.CurrentSchemaVersion;
        var loadedFormatVersion = manifest.FormatVersion;
        manifest.FormatVersion = AudioDiscProjectManifestVersions.Current;
        manifest.Status = string.IsNullOrWhiteSpace(manifest.Status)
            ? AudioDiscProjectStatus.AwaitingRip
            : manifest.Status;
        manifest.SourceDriveRoot ??= string.Empty;
        manifest.SourceDriveName ??= string.Empty;
        manifest.SourceDriveDevicePath ??= string.Empty;
        manifest.SourceVolumeLabel ??= string.Empty;
        manifest.ErrorMessage ??= string.Empty;
        manifest.ExportStatus = string.IsNullOrWhiteSpace(manifest.ExportStatus)
            ? AudioDiscExportStatus.NotStarted
            : manifest.ExportStatus;
        var sourcesComplete = manifest.TotalDiscs > 0 && manifest.Discs is not null &&
            Enumerable.Range(1, manifest.TotalDiscs).All(number =>
                manifest.Discs.Any(disc => disc.DiscNumber == number &&
                    string.Equals(disc.Status, AudioDiscStatus.Completed, StringComparison.OrdinalIgnoreCase)));
        var hasSuccessfulExport = manifest.HasSuccessfulExport ||
            string.Equals(
                manifest.ExportStatus,
                AudioDiscExportStatus.Completed,
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(manifest.LastSuccessfulOutputPath) ||
            !string.IsNullOrWhiteSpace(manifest.FinalOutputPath);
        manifest.HasSuccessfulExport = hasSuccessfulExport;
        manifest.LastSuccessfulOutputPath ??= string.Empty;
        if (string.IsNullOrWhiteSpace(manifest.LastSuccessfulOutputPath) &&
            !string.IsNullOrWhiteSpace(manifest.FinalOutputPath))
        {
            manifest.LastSuccessfulOutputPath = manifest.FinalOutputPath;
        }
        manifest.LastSuccessfulExportUtc ??= manifest.ExportCompletedUtc;
        var storedPipelineState = !string.Equals(
                loadedFormatVersion,
                AudioDiscProjectManifestVersions.Current,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                manifest.PipelineState,
                ProjectPipelineStateNames.Preparing,
                StringComparison.OrdinalIgnoreCase)
            ? (!string.Equals(manifest.ExportStatus, AudioDiscExportStatus.NotStarted, StringComparison.OrdinalIgnoreCase)
                ? manifest.ExportStatus
                : manifest.Status)
            : manifest.PipelineState;
        manifest.PipelineState = ProjectPipelineStateNames.FromManifestValue(
            storedPipelineState,
            hasSuccessfulExport,
            sourcesComplete).ToManifestValue();
        manifest.FinalOutputPath ??= string.Empty;
        manifest.LastSuccessfulOutputPath ??= string.Empty;
        manifest.ExportErrorMessage ??= string.Empty;
        manifest.Discs ??= [];

        foreach (var disc in manifest.Discs)
        {
            disc.Status = string.IsNullOrWhiteSpace(disc.Status)
                ? AudioDiscStatus.Pending
                : disc.Status;
            disc.DiscIdentity ??= string.Empty;
            disc.SourceDriveRoot ??= string.Empty;
            disc.SourceDriveName ??= string.Empty;
            disc.SourceDriveDevicePath ??= string.Empty;
            disc.SourceVolumeLabel ??= string.Empty;
            disc.ErrorMessage ??= string.Empty;
            disc.Tracks ??= [];

            foreach (var track in disc.Tracks)
            {
                track.TrackIdentity ??= string.Empty;
                track.FileName ??= string.Empty;
                track.RelativePath ??= string.Empty;
                track.ChapterTitle ??= string.Empty;
                track.Status = string.IsNullOrWhiteSpace(track.Status)
                    ? AudioDiscTrackStatus.Pending
                    : track.Status;
                track.ErrorMessage ??= string.Empty;
            }
        }

        changed |= BackfillMissingTrackSourceIds(manifest);
        return changed || before != JsonSerializer.Serialize(manifest, JsonOptions);
    }

    private static bool BackfillMissingTrackSourceIds(AudioDiscProjectManifest manifest)
    {
        var changed = false;
        var exportEvidence = BuildExportTrackSourceIdEvidence(manifest.ProjectFolder);

        foreach (var track in manifest.Discs.SelectMany(disc => disc.Tracks))
        {
            if (track.SourceId != Guid.Empty)
                continue;

            track.SourceId = ResolveExportTrackSourceId(manifest.ProjectFolder, track, exportEvidence) ?? Guid.Empty;
            changed = true;
        }

        return changed;
    }

    private static Guid? ResolveExportTrackSourceId(
        string projectFolder,
        AudioDiscProjectManifestTrack track,
        IReadOnlyCollection<ExportTrackSourceIdEvidence> exportEvidence)
    {
        if (exportEvidence.Count == 0 || string.IsNullOrWhiteSpace(track.RelativePath))
            return null;

        var sourcePath = Path.Combine(projectFolder, track.RelativePath);
        var matches = exportEvidence
            .Where(item => PathsEqual(item.SourcePath, sourcePath))
            .Select(item => item.SourceId)
            .Distinct()
            .Take(2)
            .ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    private static IReadOnlyCollection<ExportTrackSourceIdEvidence> BuildExportTrackSourceIdEvidence(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
            return [];

        var result = new List<ExportTrackSourceIdEvidence>();
        AddExportTrackSourceIdEvidence(ProjectFolderLayout.ResolveWorkManifestPath(projectFolder), result);
        AddExportTrackSourceIdEvidence(ProjectFolderLayout.ResolveExportManifestPath(projectFolder), result);
        return result;
    }

    private static void AddExportTrackSourceIdEvidence(
        string manifestPath,
        List<ExportTrackSourceIdEvidence> result)
    {
        try
        {
            if (!File.Exists(manifestPath))
                return;

            var json = File.ReadAllText(manifestPath);
            PersistentJsonSchema.Read(
                json,
                "Work-/Export-Manifest",
                manifestPath,
                ExportWorkManifestVersions.CurrentSchemaVersion);

            var manifest = JsonSerializer.Deserialize<ExportWorkManifest>(json, JsonOptions);
            if (manifest is null)
                return;

            foreach (var track in manifest.Tracks)
            {
                if (track.SourceId == Guid.Empty || string.IsNullOrWhiteSpace(track.SourcePath))
                    continue;

                result.Add(new ExportTrackSourceIdEvidence(track.SourceId, track.SourcePath));
            }
        }
        catch
        {
            // Export manifest evidence is optional during Audio-CD manifest normalization.
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record ExportTrackSourceIdEvidence(Guid SourceId, string SourcePath);

    private static void RefreshProgressState(AudioDiscProjectManifest manifest, DateTime now)
    {
        foreach (var disc in manifest.Discs)
        {
            if (disc.Tracks.Any(track => string.Equals(
                    track.Status,
                    AudioDiscTrackStatus.Failed,
                    StringComparison.OrdinalIgnoreCase)))
            {
                disc.Status = AudioDiscStatus.Failed;
                disc.CompletedUtc = null;
                if (string.IsNullOrWhiteSpace(disc.ErrorMessage))
                {
                    disc.ErrorMessage = disc.Tracks
                        .First(track => string.Equals(
                            track.Status,
                            AudioDiscTrackStatus.Failed,
                            StringComparison.OrdinalIgnoreCase))
                        .ErrorMessage;
                }
            }
            else if (disc.Tracks.Count > 0 && disc.Tracks.All(track => string.Equals(
                         track.Status,
                         AudioDiscTrackStatus.Ripped,
                         StringComparison.OrdinalIgnoreCase)))
            {
                disc.Status = AudioDiscStatus.Completed;
                disc.CompletedUtc ??= disc.Tracks
                    .Where(track => track.CompletedUtc.HasValue)
                    .Select(track => track.CompletedUtc)
                    .Max() ?? now;
                disc.ErrorMessage = string.Empty;
            }
            else if (!string.Equals(disc.Status, AudioDiscStatus.Ripping, StringComparison.OrdinalIgnoreCase))
            {
                disc.Status = AudioDiscStatus.Pending;
                disc.CompletedUtc = null;
            }
        }

        if (manifest.Discs.Any(disc => string.Equals(
                disc.Status,
                AudioDiscStatus.Failed,
                StringComparison.OrdinalIgnoreCase)))
        {
            manifest.Status = AudioDiscProjectStatus.Failed;
            manifest.PipelineState = ProjectPipelineStateNames.AcquiringSources;
            manifest.CompletedUtc = null;
            if (string.IsNullOrWhiteSpace(manifest.ErrorMessage))
            {
                manifest.ErrorMessage = manifest.Discs
                    .First(disc => string.Equals(
                        disc.Status,
                        AudioDiscStatus.Failed,
                        StringComparison.OrdinalIgnoreCase))
                    .ErrorMessage;
            }
            return;
        }

        if (manifest.TotalDiscs > 0 &&
            Enumerable.Range(1, manifest.TotalDiscs).All(number =>
                manifest.Discs.Any(disc => disc.DiscNumber == number &&
                    string.Equals(disc.Status, AudioDiscStatus.Completed, StringComparison.OrdinalIgnoreCase))))
        {
            manifest.Status = AudioDiscProjectStatus.RippingCompleted;
            if (!string.Equals(manifest.PipelineState, ProjectPipelineStateNames.ReviewBeforeMerge, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(manifest.PipelineState, ProjectPipelineStateNames.Merging, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(manifest.PipelineState, ProjectPipelineStateNames.Completed, StringComparison.OrdinalIgnoreCase))
            {
                manifest.PipelineState = ProjectPipelineStateNames.Converting;
            }
            manifest.CompletedUtc ??= now;
            manifest.ErrorMessage = string.Empty;
            return;
        }

        manifest.CompletedUtc = null;
        if (string.Equals(manifest.Status, AudioDiscProjectStatus.Canceled, StringComparison.OrdinalIgnoreCase))
            return;

        manifest.PipelineState = ProjectPipelineStateNames.AcquiringSources;

        if (manifest.Discs.Any(disc => string.Equals(
                disc.Status,
                AudioDiscStatus.Ripping,
                StringComparison.OrdinalIgnoreCase)))
        {
            manifest.Status = AudioDiscProjectStatus.Ripping;
        }
        else if (manifest.Discs.Any(disc => string.Equals(
                     disc.Status,
                     AudioDiscStatus.Completed,
                     StringComparison.OrdinalIgnoreCase)))
        {
            manifest.Status = AudioDiscProjectStatus.WaitingForDisc;
        }
        else
        {
            manifest.Status = AudioDiscProjectStatus.AwaitingRip;
        }
    }

    private static bool SameIdentity(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static double CalculatePcmSourceSizeMb(TimeSpan duration)
    {
        const double bytesPerSecond = 44_100d * 2d * 2d;
        return Math.Round(duration.TotalSeconds * bytesPerSecond / 1024d / 1024d, 1, MidpointRounding.AwayFromZero);
    }

    private static string GetWorkingExtension(string workingFormat)
    {
        return ".flac";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.ToString(duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);


    private static bool UpdateValue(
        string currentValue,
        string? newValue,
        Action<string> apply)
    {
        var normalized = newValue ?? string.Empty;
        if (string.Equals(currentValue, normalized, StringComparison.Ordinal))
            return false;

        apply(normalized);
        return true;
    }

    private static string MakeSafeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.Join("_", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
