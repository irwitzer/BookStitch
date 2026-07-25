using BookStitch.Models;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BookStitch.Services;

public sealed record Mp3DiscAdditionalImportPlan(
    int CompletedDiscCount,
    int CurrentTotalDiscs,
    int MinimumTotalDiscs,
    int DefaultTotalDiscs,
    int MaximumTotalDiscs);

public sealed record Mp3DiscResumePlan(
    int CompletedDiscCount,
    int CurrentTotalDiscs,
    int MinimumTotalDiscs,
    int? NextMissingDiscNumber,
    string SetupMessage);

public sealed class Mp3DiscProjectService
{
    private const string ManifestFileName = ProjectFolderLayout.WorkManifestFileName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public Mp3DiscProjectManifest LoadOrCreate(
        string projectFolder,
        string sourceFolder,
        int totalDiscs,
        string exportPreset,
        string parallelJobs,
        string outputExtension,
        string outputFolder,
        string fileNameTemplate,
        DiscDriveInfo? sourceDriveInfo = null)
    {
        new ProjectFolderMigrationService().MigrateIfNeeded(projectFolder);
        ProjectFolderLayout.EnsureProjectFolders(projectFolder);
        var manifestPath = ProjectFolderLayout.ResolveWorkManifestPath(projectFolder);

        try
        {
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<Mp3DiscProjectManifest>(json, JsonOptions);

                if (loaded is not null)
                {
                    NormalizeLoadedManifest(loaded, projectFolder);
                    loaded.SourceFolder = sourceFolder;
                    ApplyDriveSnapshot(loaded, sourceDriveInfo);
                    loaded.TotalDiscs = totalDiscs;
                    UpdateSettingsSnapshot(loaded, exportPreset, parallelJobs, outputExtension, outputFolder, fileNameTemplate);
                    return loaded;
                }
            }
        }
        catch
        {
            // Eine beschädigte Projektdatei darf den Import nicht blockieren.
            // Sie wird unten sauber neu geschrieben.
        }

        var now = DateTime.UtcNow;
        return new Mp3DiscProjectManifest
        {
            ProjectType = "Mp3Disc",
            ProjectFolder = projectFolder,
            SourceFolder = sourceFolder,
            SourceDriveRoot = sourceDriveInfo?.RootPath ?? string.Empty,
            SourceDriveName = sourceDriveInfo?.DiagnosticDriveName ?? string.Empty,
            SourceDriveDevicePath = sourceDriveInfo?.DevicePath ?? string.Empty,
            SourceVolumeLabel = sourceDriveInfo?.VolumeLabel ?? string.Empty,
            TotalDiscs = totalDiscs,
            CreatedUtc = now,
            UpdatedUtc = now,
            ImportedDiscs = [],
            ExportPreset = exportPreset,
            ParallelJobs = parallelJobs,
            OutputExtension = outputExtension,
            OutputFolder = outputFolder,
            FileNameTemplate = fileNameTemplate
        };
    }


    public Mp3DiscProjectManifest? TryLoad(string projectFolder)
    {
        try
        {
            new ProjectFolderMigrationService().MigrateIfNeeded(projectFolder);
            var manifestPath = ProjectFolderLayout.ResolveWorkManifestPath(projectFolder);
            if (!File.Exists(manifestPath))
                return null;

            var json = File.ReadAllText(manifestPath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<Mp3DiscProjectManifest>(json, JsonOptions);
            if (manifest is not null)
            {
                NormalizeLoadedManifest(manifest, projectFolder);
            }

            return manifest;
        }
        catch
        {
            return null;
        }
    }

    public int CountCompletedImportedDiscs(Mp3DiscProjectManifest manifest)
    {
        return manifest.ImportedDiscs
            .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .Select(disc => disc.DiscNumber)
            .Distinct()
            .Count();
    }

    public int GetMinimumDiscCount(Mp3DiscProjectManifest manifest)
    {
        var highestImportedDiscNumber = manifest.ImportedDiscs
            .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .Select(disc => disc.DiscNumber)
            .DefaultIfEmpty(1)
            .Max();

        return Math.Clamp(highestImportedDiscNumber, 1, 99);
    }


    public int GetMinimumTotalDiscsForAdditionalImport(Mp3DiscProjectManifest manifest)
    {
        var highestCompletedDiscNumber = GetMinimumDiscCount(manifest);
        var minimumTotalDiscs = highestCompletedDiscNumber + 1;

        return Math.Clamp(minimumTotalDiscs, 1, 99);
    }

    public Mp3DiscResumePlan BuildResumePlan(Mp3DiscProjectManifest manifest, int maximumTotalDiscs = 99)
    {
        var clampedMaximum = Math.Clamp(maximumTotalDiscs, 1, 99);
        var completedDiscCount = CountCompletedImportedDiscs(manifest);
        var minimumTotalDiscs = Math.Min(GetMinimumDiscCount(manifest), clampedMaximum);
        var currentTotalDiscs = Math.Clamp(
            Math.Max(manifest.TotalDiscs, minimumTotalDiscs),
            minimumTotalDiscs,
            clampedMaximum);
        var nextMissingDiscNumber = GetNextMissingDiscNumber(manifest, currentTotalDiscs);

        return new Mp3DiscResumePlan(
            CompletedDiscCount: completedDiscCount,
            CurrentTotalDiscs: currentTotalDiscs,
            MinimumTotalDiscs: minimumTotalDiscs,
            NextMissingDiscNumber: nextMissingDiscNumber,
            SetupMessage: BuildResumeSetupMessage(completedDiscCount, currentTotalDiscs, nextMissingDiscNumber));
    }

    public void UpdateResumeDiscPlan(
        Mp3DiscProjectManifest manifest,
        int totalDiscs,
        string sourceFolder,
        int maximumTotalDiscs = 99)
    {
        var clampedMaximum = Math.Clamp(maximumTotalDiscs, 1, 99);
        var minimumTotalDiscs = Math.Min(GetMinimumDiscCount(manifest), clampedMaximum);

        if (totalDiscs < minimumTotalDiscs || totalDiscs > clampedMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalDiscs),
                $"Die Gesamtzahl muss zwischen {minimumTotalDiscs} und {clampedMaximum} liegen.");
        }

        manifest.TotalDiscs = totalDiscs;
        manifest.SourceFolder = sourceFolder?.Trim() ?? string.Empty;
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public Mp3DiscAdditionalImportPlan BuildAdditionalImportPlan(Mp3DiscProjectManifest manifest, int maximumTotalDiscs = 99)
    {
        var clampedMaximum = Math.Clamp(maximumTotalDiscs, 1, 99);
        var completedDiscCount = CountCompletedImportedDiscs(manifest);
        var minimumTotalDiscs = Math.Min(GetMinimumTotalDiscsForAdditionalImport(manifest), clampedMaximum);
        var currentTotalDiscs = Math.Clamp(manifest.TotalDiscs, 1, clampedMaximum);
        var defaultTotalDiscs = minimumTotalDiscs;

        return new Mp3DiscAdditionalImportPlan(
            CompletedDiscCount: completedDiscCount,
            CurrentTotalDiscs: currentTotalDiscs,
            MinimumTotalDiscs: minimumTotalDiscs,
            DefaultTotalDiscs: defaultTotalDiscs,
            MaximumTotalDiscs: clampedMaximum);
    }

    public void IncreaseTotalDiscsForAdditionalImport(Mp3DiscProjectManifest manifest, int newTotalDiscs)
    {
        var minimumTotalDiscs = GetMinimumTotalDiscsForAdditionalImport(manifest);
        if (newTotalDiscs < minimumTotalDiscs || newTotalDiscs > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newTotalDiscs),
                $"Die Gesamtzahl muss zwischen {minimumTotalDiscs} und 99 liegen.");
        }

        manifest.TotalDiscs = newTotalDiscs;
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public int? GetNextMissingDiscNumber(Mp3DiscProjectManifest manifest, int totalDiscs)
    {
        var importedDiscNumbers = manifest.ImportedDiscs
            .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .Select(disc => disc.DiscNumber)
            .ToHashSet();

        for (var discNumber = 1; discNumber <= totalDiscs; discNumber++)
        {
            if (!importedDiscNumbers.Contains(discNumber))
                return discNumber;
        }

        return null;
    }

    public string BuildResumeSetupMessage(int importedDiscCount, int totalDiscs, int? nextMissingDisc)
    {
        var status = $"Importiert: {importedDiscCount} von {totalDiscs} CDs.";
        var next = nextMissingDisc.HasValue
            ? $"Nächste fehlende CD: CD {nextMissingDisc.Value}."
            : "Alle eingestellten CDs sind bereits importiert.";

        return status + "\n" + next + "\n\n" +
               "Du kannst hier die CD-Anzahl und die Export-Einstellungen korrigieren. " +
               "Die CD-Anzahl darf nicht kleiner sein als die bereits importierten CDs.";
    }

    public void UpdateSettingsSnapshot(
        Mp3DiscProjectManifest manifest,
        string exportPreset,
        string parallelJobs,
        string outputExtension,
        string outputFolder,
        string fileNameTemplate)
    {
        manifest.ExportPreset = exportPreset ?? "";
        manifest.ParallelJobs = parallelJobs ?? "";
        manifest.OutputExtension = outputExtension ?? "";
        manifest.OutputFolder = outputFolder ?? "";
        manifest.FileNameTemplate = fileNameTemplate ?? "";
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public void UpdateMetadataSnapshot(
        Mp3DiscProjectManifest manifest,
        string title,
        string author,
        string album,
        string narrator,
        string genre,
        string coverSourcePath,
        string processedCoverPath,
        string outputFileName)
    {
        manifest.Title = title ?? "";
        manifest.Author = author ?? "";
        manifest.Album = album ?? "";
        manifest.Narrator = narrator ?? "";
        manifest.Genre = genre ?? "";
        manifest.CoverSourcePath = coverSourcePath ?? "";
        manifest.ProcessedCoverPath = processedCoverPath ?? "";
        manifest.OutputFileName = outputFileName ?? "";
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkDiscCompleted(
        Mp3DiscProjectManifest manifest,
        int discNumber,
        string signature,
        string sourcePath,
        string localFolder,
        int fileCount,
        int copiedFiles,
        DiscDriveInfo? sourceDriveInfo = null)
    {
        manifest.ImportedDiscs.RemoveAll(disc => disc.DiscNumber == discNumber);
        manifest.ImportedDiscs.RemoveAll(disc =>
            !string.IsNullOrWhiteSpace(signature) &&
            string.Equals(disc.Signature, signature, StringComparison.OrdinalIgnoreCase));

        manifest.ImportedDiscs.Add(new Mp3DiscProjectManifestDisc
        {
            DiscNumber = discNumber,
            Status = Mp3DiscImportStatus.Completed,
            Signature = signature,
            SourcePath = sourcePath,
            SourceDriveRoot = sourceDriveInfo?.RootPath ?? string.Empty,
            SourceDriveName = sourceDriveInfo?.DiagnosticDriveName ?? string.Empty,
            SourceDriveDevicePath = sourceDriveInfo?.DevicePath ?? string.Empty,
            SourceVolumeLabel = sourceDriveInfo?.VolumeLabel ?? string.Empty,
            LocalFolder = localFolder,
            FileCount = fileCount,
            CopiedFiles = copiedFiles,
            CompletedUtc = DateTime.UtcNow
        });

        manifest.ImportedDiscs = manifest.ImportedDiscs
            .OrderBy(disc => disc.DiscNumber)
            .ToList();
        manifest.PipelineState = manifest.ImportedDiscs.Count >= manifest.TotalDiscs
            ? ProjectPipelineStateNames.Converting
            : ProjectPipelineStateNames.AcquiringSources;
        manifest.UpdatedUtc = DateTime.UtcNow;
    }

    private static void ApplyDriveSnapshot(Mp3DiscProjectManifest manifest, DiscDriveInfo? sourceDriveInfo)
    {
        if (sourceDriveInfo is null)
            return;

        manifest.SourceDriveRoot = sourceDriveInfo.RootPath;
        manifest.SourceDriveName = sourceDriveInfo.DiagnosticDriveName;
        manifest.SourceDriveDevicePath = sourceDriveInfo.DevicePath;
        manifest.SourceVolumeLabel = sourceDriveInfo.VolumeLabel;
    }

    public void Save(Mp3DiscProjectManifest manifest)
    {
        NormalizeLoadedManifest(manifest, manifest.ProjectFolder);
        ProjectFolderLayout.EnsureProjectFolders(manifest.ProjectFolder);
        manifest.UpdatedUtc = DateTime.UtcNow;

        var manifestPath = GetManifestPath(manifest.ProjectFolder);
        var partPath = manifestPath + ".part";
        var json = JsonSerializer.Serialize(manifest, JsonOptions);

        File.WriteAllText(partPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (File.Exists(manifestPath))
        {
            File.Replace(partPath, manifestPath, null);
        }
        else
        {
            File.Move(partPath, manifestPath);
        }
    }

    private static void NormalizeLoadedManifest(Mp3DiscProjectManifest manifest, string projectFolder)
    {
        var loadedFormatVersion = manifest.FormatVersion;
        manifest.FormatVersion = Mp3DiscManifestVersions.Current;
        manifest.ProjectType = string.IsNullOrWhiteSpace(manifest.ProjectType) ? "Mp3Disc" : manifest.ProjectType;
        manifest.ProjectFolder = projectFolder ?? string.Empty;
        var sourcesComplete = manifest.ImportedDiscs?.Count >= manifest.TotalDiscs && manifest.TotalDiscs > 0;
        var storedPipelineState = !string.Equals(
                loadedFormatVersion,
                Mp3DiscManifestVersions.Current,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                manifest.PipelineState,
                ProjectPipelineStateNames.Preparing,
                StringComparison.OrdinalIgnoreCase)
            ? (sourcesComplete ? ProjectPipelineStateNames.Converting : ProjectPipelineStateNames.AcquiringSources)
            : manifest.PipelineState;
        manifest.PipelineState = ProjectPipelineStateNames.FromManifestValue(
            storedPipelineState,
            hasSuccessfulExport: false,
            sourcesComplete: sourcesComplete).ToManifestValue();
        manifest.SourceFolder ??= string.Empty;
        manifest.SourceDriveRoot ??= string.Empty;
        manifest.SourceDriveName ??= string.Empty;
        manifest.SourceDriveDevicePath ??= string.Empty;
        manifest.SourceVolumeLabel ??= string.Empty;
        manifest.ExportPreset ??= string.Empty;
        manifest.ParallelJobs ??= string.Empty;
        manifest.OutputExtension ??= string.Empty;
        manifest.OutputFolder ??= string.Empty;
        manifest.FileNameTemplate ??= string.Empty;
        manifest.OutputFileName ??= string.Empty;
        manifest.Title ??= string.Empty;
        manifest.Author ??= string.Empty;
        manifest.Narrator ??= string.Empty;
        manifest.Genre ??= string.Empty;
        manifest.CoverSourcePath ??= string.Empty;
        manifest.ProcessedCoverPath ??= string.Empty;
        manifest.ImportedDiscs ??= [];

        foreach (var disc in manifest.ImportedDiscs)
        {
            disc.Status ??= Mp3DiscImportStatus.Completed;
            disc.Signature ??= string.Empty;
            disc.SourcePath ??= string.Empty;
            disc.SourceDriveRoot ??= string.Empty;
            disc.SourceDriveName ??= string.Empty;
            disc.SourceDriveDevicePath ??= string.Empty;
            disc.SourceVolumeLabel ??= string.Empty;
            disc.LocalFolder ??= string.Empty;
        }
    }

    public static string GetManifestPath(string projectFolder)
    {
        return ProjectFolderLayout.GetWorkManifestPath(projectFolder);
    }
}
