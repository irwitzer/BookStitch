using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed record Mp3DiscImportPrecheckResult(
    DiscSourceAnalysis Analysis,
    string Signature,
    bool IsAlreadyImported,
    IReadOnlyList<Mp3DiscPlannedSourceIdentity> PlannedSources)
{
    public int TotalFiles => Analysis.SupportedAudioFiles.Count;

    // Uses only scan evidence already available; never probes or reads media.
    public PerformanceWorkloadSnapshot CreatePerformanceWorkload(int trackCount) => new()
    {
        ProjectType = PerformanceProjectType.Mp3Disc,
        DiscCount = 1,
        FileCount = TotalFiles,
        TrackCount = trackCount,
        InputBytes = PlannedSources.Sum(source => source.SourceSizeBytes)
    };
}

public sealed record Mp3DiscSingleDiscImportResult(
    Mp3DiscImportPrecheckResult Precheck,
    DiscImportResult? ImportResult)
{
    public bool IsAlreadyImported => Precheck.IsAlreadyImported;

    public int TotalFilesOnDisc => Precheck.TotalFiles;

    public bool WasCanceled => ImportResult?.WasCanceled == true;

    public int CopiedFiles => ImportResult?.CopiedFiles ?? 0;
}

public sealed class Mp3DiscImportWorkflowService
{
    private readonly Mp3DiscImportService _mp3DiscImportService;
    private readonly Mp3DiscProjectService _mp3DiscProjectService;
    private readonly DiscDriveService _discDriveService;
    private readonly SourceCopyIdentityStore _sourceCopyIdentityStore;

    public Mp3DiscImportWorkflowService(
        Mp3DiscImportService mp3DiscImportService,
        Mp3DiscProjectService mp3DiscProjectService,
        DiscDriveService? discDriveService = null,
        SourceCopyIdentityStore? sourceCopyIdentityStore = null)
    {
        _mp3DiscImportService = mp3DiscImportService;
        _mp3DiscProjectService = mp3DiscProjectService;
        _discDriveService = discDriveService ?? new DiscDriveService();
        _sourceCopyIdentityStore = sourceCopyIdentityStore ?? new SourceCopyIdentityStore();
    }

    public int CountCompletedCopiedFiles(Mp3DiscProjectManifest manifest)
    {
        return manifest.ImportedDiscs
            .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .Sum(disc => disc.CopiedFiles);
    }

    public HashSet<string> BuildCompletedDiscSignatureSet(Mp3DiscProjectManifest manifest)
    {
        return manifest.ImportedDiscs
            .Where(disc => string.Equals(disc.Status, Mp3DiscImportStatus.Completed, StringComparison.OrdinalIgnoreCase))
            .Select(disc => disc.Signature)
            .Where(signature => !string.IsNullOrWhiteSpace(signature))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public Mp3DiscImportPrecheckResult AnalyzeDiscForImport(
        string sourceFolder,
        ISet<string> importedDiscSignatures,
        CancellationToken cancellationToken = default)
    {
        var analysis = _mp3DiscImportService.AnalyzeSource(sourceFolder, cancellationToken);
        var signature = _mp3DiscImportService.CreateDiscSignature(sourceFolder, analysis);
        var isAlreadyImported = !string.IsNullOrWhiteSpace(signature) && importedDiscSignatures.Contains(signature);

        return new Mp3DiscImportPrecheckResult(
            analysis,
            signature,
            isAlreadyImported,
            Mp3DiscSourceIdentityPlanner.Create(
                sourceFolder,
                signature,
                analysis.SupportedAudioFiles));
    }

    public Task<DiscImportResult> CopyDiscAsync(
        string sourceFolder,
        string projectFolder,
        int discNumber,
        int totalDiscs,
        IProgress<DiscCopyProgress>? progress,
        IProgress<DiscCopiedFile>? copiedFileProgress,
        CancellationToken token,
        IReadOnlyDictionary<string, Guid>? plannedSourceIdsByRelativePath = null, Action<string>? sourceReadStarted = null,
        IProgress<DiscCopiedFile>? reusedFileProgress = null)
    {
        return _mp3DiscImportService.CopyDiscAsync(
            sourceFolder,
            projectFolder,
            discNumber,
            totalDiscs,
            progress,
            copiedFileProgress,
            token,
            plannedSourceIdsByRelativePath, sourceReadStarted, reusedFileProgress);
    }


    public async Task<Mp3DiscSingleDiscImportResult> ImportDiscAsync(
        string sourceFolder,
        string projectFolder,
        Mp3DiscProjectManifest manifest,
        int discNumber,
        int totalDiscs,
        ISet<string> importedDiscSignatures,
        IProgress<DiscCopyProgress>? progress,
        IProgress<DiscCopiedFile>? copiedFileProgress,
        Action<Mp3DiscImportPrecheckResult>? onReadyToCopy,
        CancellationToken token,
        bool acceptDuplicate = false,
        Mp3DiscPendingChapterDisc? preparedDisc = null, Action<string>? sourceReadStarted = null,
        IProgress<DiscCopiedFile>? reusedFileProgress = null)
    {
        var precheck = await Task.Run(
            () => AnalyzeDiscForImport(sourceFolder, importedDiscSignatures, token), token);

        if (precheck.IsAlreadyImported && !acceptDuplicate)
            return new Mp3DiscSingleDiscImportResult(precheck, ImportResult: null);

        if (precheck.IsAlreadyImported)
            precheck = precheck with { IsAlreadyImported = false };

        precheck = precheck with
        {
            PlannedSources = ReuseExistingSourceIds(
                projectFolder,
                discNumber,
                preparedDisc?.DiscNumber == discNumber &&
                    string.Equals(preparedDisc.Signature, precheck.Signature, StringComparison.OrdinalIgnoreCase)
                    ? preparedDisc.Sources : precheck.PlannedSources,
                manifest)
        };

        onReadyToCopy?.Invoke(precheck);

        var plannedSourceIdsByRelativePath = precheck.PlannedSources
            .ToDictionary(
                source => Mp3DiscSourceIdentityPlanner.NormalizeRelativePath(source.RelativePath),
                source => source.SourceId,
                StringComparer.OrdinalIgnoreCase);

        var importResult = await CopyDiscAsync(
            sourceFolder,
            projectFolder,
            discNumber,
            totalDiscs,
            progress,
            copiedFileProgress,
            token,
            plannedSourceIdsByRelativePath, sourceReadStarted, reusedFileProgress);

        if (!importResult.WasCanceled)
        {
            importedDiscSignatures.Add(precheck.Signature);
            DiscDriveInfo? sourceDriveInfo = null;
            try
            {
                sourceDriveInfo = await Task.Run(() =>
                    _discDriveService.GetDriveDiagnosticsForPath(sourceFolder));
            }
            catch
            {
                // Diagnoseinformationen sind optional und dürfen einen erfolgreichen Import nie zurückrollen.
            }

            MarkDiscCompleted(
                manifest,
                discNumber,
                precheck.Signature,
                sourceFolder,
                importResult,
                precheck.TotalFiles,
                sourceDriveInfo,
                preserveExistingDiscWithSameSignature: acceptDuplicate);
        }

        return new Mp3DiscSingleDiscImportResult(precheck, importResult);
    }


    public IReadOnlyList<Mp3DiscPlannedSourceIdentity> ReuseExistingSourceIds(
        string projectFolder,
        int discNumber,
        IReadOnlyList<Mp3DiscPlannedSourceIdentity> plannedSources,
        Mp3DiscProjectManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(projectFolder) || discNumber <= 0 || plannedSources.Count == 0)
            return plannedSources;

        var existingByTargetPath = _sourceCopyIdentityStore.Load(projectFolder)
            .Where(identity => identity.SourceId != Guid.Empty)
            .GroupBy(
                identity => Mp3DiscSourceIdentityPlanner.NormalizeRelativePath(identity.TargetRelativePath),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().SourceId, StringComparer.OrdinalIgnoreCase);

        var usedSourceIds = existingByTargetPath.Values
            .Concat(manifest.ImportedDiscs.SelectMany(disc => disc.SourceFiles).Select(source => source.SourceId))
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        var discFolder = ProjectFolderLayout.GetDiscOriginalsFolder(projectFolder, discNumber);
        return plannedSources
            .Select(source =>
            {
                var targetFile = Path.Combine(discFolder, source.RelativePath);
                var targetRelativePath = Mp3DiscSourceIdentityPlanner.NormalizeRelativePath(
                    Path.GetRelativePath(projectFolder, targetFile));
                if (existingByTargetPath.TryGetValue(targetRelativePath, out var existingSourceId))
                    return source with { SourceId = existingSourceId };

                // Repeated media are separate logical sources; retries keep their persisted identity.
                var sourceId = source.SourceId;
                while (sourceId == Guid.Empty || !usedSourceIds.Add(sourceId))
                    sourceId = Guid.NewGuid();
                return source with { SourceId = sourceId };
            })
            .ToArray();
    }

    public void MarkDiscCompleted(
        Mp3DiscProjectManifest manifest,
        int discNumber,
        string signature,
        string sourceFolder,
        DiscImportResult importResult,
        int fileCount,
        DiscDriveInfo? sourceDriveInfo = null,
        bool preserveExistingDiscWithSameSignature = false)
    {
        _mp3DiscProjectService.MarkDiscCompleted(
            manifest,
            discNumber,
            signature,
            sourceFolder,
            importResult.ImportedFolder,
            fileCount,
            importResult.CopiedFiles,
            sourceDriveInfo,
            sourceFiles: importResult.ImportedFiles,
            preserveExistingDiscWithSameSignature: preserveExistingDiscWithSameSignature);
    }
}
