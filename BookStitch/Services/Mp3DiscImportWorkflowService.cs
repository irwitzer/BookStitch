using BookStitch.Models;

namespace BookStitch.Services;

public sealed record Mp3DiscImportPrecheckResult(
    DiscSourceAnalysis Analysis,
    string Signature,
    bool IsAlreadyImported)
{
    public int TotalFiles => Analysis.SupportedAudioFiles.Count;
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

    public Mp3DiscImportWorkflowService(
        Mp3DiscImportService mp3DiscImportService,
        Mp3DiscProjectService mp3DiscProjectService,
        DiscDriveService? discDriveService = null)
    {
        _mp3DiscImportService = mp3DiscImportService;
        _mp3DiscProjectService = mp3DiscProjectService;
        _discDriveService = discDriveService ?? new DiscDriveService();
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
        ISet<string> importedDiscSignatures)
    {
        var analysis = _mp3DiscImportService.AnalyzeSource(sourceFolder);
        var signature = _mp3DiscImportService.CreateDiscSignature(sourceFolder, analysis);
        var isAlreadyImported = !string.IsNullOrWhiteSpace(signature) && importedDiscSignatures.Contains(signature);

        return new Mp3DiscImportPrecheckResult(
            analysis,
            signature,
            isAlreadyImported);
    }

    public Task<DiscImportResult> CopyDiscAsync(
        string sourceFolder,
        string projectFolder,
        int discNumber,
        int totalDiscs,
        IProgress<DiscCopyProgress>? progress,
        IProgress<DiscCopiedFile>? copiedFileProgress,
        CancellationToken token)
    {
        return _mp3DiscImportService.CopyDiscAsync(
            sourceFolder,
            projectFolder,
            discNumber,
            totalDiscs,
            progress,
            copiedFileProgress,
            token);
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
        CancellationToken token)
    {
        var precheck = AnalyzeDiscForImport(sourceFolder, importedDiscSignatures);

        if (precheck.IsAlreadyImported)
            return new Mp3DiscSingleDiscImportResult(precheck, ImportResult: null);

        onReadyToCopy?.Invoke(precheck);

        var importResult = await CopyDiscAsync(
            sourceFolder,
            projectFolder,
            discNumber,
            totalDiscs,
            progress,
            copiedFileProgress,
            token);

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
                sourceDriveInfo);
        }

        return new Mp3DiscSingleDiscImportResult(precheck, importResult);
    }

    public void MarkDiscCompleted(
        Mp3DiscProjectManifest manifest,
        int discNumber,
        string signature,
        string sourceFolder,
        DiscImportResult importResult,
        int fileCount,
        DiscDriveInfo? sourceDriveInfo = null)
    {
        _mp3DiscProjectService.MarkDiscCompleted(
            manifest,
            discNumber,
            signature,
            sourceFolder,
            importResult.ImportedFolder,
            fileCount,
            importResult.CopiedFiles,
            sourceDriveInfo);
    }
}
