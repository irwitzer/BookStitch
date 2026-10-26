namespace BookStitch.Services;

public sealed class Mp3DiscPollingService
{
    private readonly Mp3DiscImportService _mp3DiscImportService;
    private readonly DiscDriveService _discDriveService;
    private readonly Func<string?, bool> _tryEjectDisc;

    public Mp3DiscPollingService(
        Mp3DiscImportService mp3DiscImportService,
        DiscDriveService discDriveService)
        : this(mp3DiscImportService, discDriveService, discDriveService.TryEjectDisc)
    {
    }

    internal Mp3DiscPollingService(
        Mp3DiscImportService mp3DiscImportService,
        DiscDriveService discDriveService,
        Func<string?, bool> tryEjectDisc)
    {
        _mp3DiscImportService = mp3DiscImportService;
        _discDriveService = discDriveService;
        _tryEjectDisc = tryEjectDisc;
    }

    public Task<DiscPollingResult> CheckDiscSourceForNextImportAsync(
        string sourceFolder,
        int discNumber,
        int totalDiscs,
        ISet<string> importedDiscSignatures,
        CancellationToken cancellationToken = default,
        bool acceptDuplicate = false) =>
        _discDriveService.MeasureOperationAsync(
            sourceFolder,
            "drive.data-analysis",
            () => CheckDiscSourceForNextImport(
                sourceFolder,
                discNumber,
                totalDiscs,
                importedDiscSignatures,
                acceptDuplicate),
            cancellationToken);

    public DiscPollingResult CheckDiscSourceForNextImport(
        string sourceFolder,
        int discNumber,
        int totalDiscs,
        ISet<string> importedDiscSignatures,
        bool acceptDuplicate = false)
    {
        if (!_discDriveService.IsDiscSourceReady(sourceFolder))
        {
            return new DiscPollingResult(
                CanImport: false,
                DialogText: LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Dialog", discNumber, totalDiscs),
                StatusText: LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Status", discNumber),
                ProgressText: LocalizationText.Format("Disc.Polling.Mp3.SourceNotReady.Progress", discNumber),
                DisplayState: DiscPollingDisplayState.Waiting);
        }

        var analysis = _mp3DiscImportService.AnalyzeSource(sourceFolder, DiscDriveOperationService.CurrentCancellationToken);
        if (!analysis.IsSupportedDataDisc)
        {
            var ejected = _tryEjectDisc(sourceFolder);
            return new DiscPollingResult(
                CanImport: false,
                DialogText: LocalizationText.Format(
                    ejected ? "Disc.Polling.Mp3.Unsupported.Ejected.Dialog" : "Disc.Polling.Mp3.Unsupported.ManualEject.Dialog",
                    discNumber,
                    totalDiscs),
                StatusText: LocalizationText.Format(
                    ejected ? "Disc.Polling.Mp3.Unsupported.Ejected.Status" : "Disc.Polling.Mp3.Unsupported.ManualEject.Status",
                    discNumber),
                ProgressText: LocalizationText.Format(
                    ejected ? "Disc.Polling.Mp3.Unsupported.Ejected.Progress" : "Disc.Polling.Mp3.Unsupported.ManualEject.Progress",
                    discNumber),
                DisplayState: DiscPollingDisplayState.Unsupported);
        }

        var signature = _mp3DiscImportService.CreateDiscSignature(sourceFolder, analysis);

        if (importedDiscSignatures.Contains(signature) && !acceptDuplicate)
        {
            var ejected = _tryEjectDisc(sourceFolder);
            return new DiscPollingResult(
                CanImport: false,
                DialogText: LocalizationText.Format(
                    ejected ? "Disc.Polling.Mp3.Duplicate.Ejected.Dialog" : "Disc.Polling.Mp3.Duplicate.ManualEject.Dialog",
                    discNumber,
                    totalDiscs),
                StatusText: LocalizationText.Format(
                    ejected ? "Disc.Polling.Mp3.Duplicate.Ejected.Status" : "Disc.Polling.Mp3.Duplicate.ManualEject.Status",
                    discNumber),
                ProgressText: LocalizationText.Format(
                    ejected ? "Disc.Polling.Mp3.Duplicate.Ejected.Progress" : "Disc.Polling.Mp3.Duplicate.ManualEject.Progress",
                    discNumber),
                DisplayState: DiscPollingDisplayState.Duplicate);
        }

        return new DiscPollingResult(
            CanImport: true,
            DialogText: LocalizationText.Format("Disc.Polling.Mp3.Ready.Dialog", discNumber, totalDiscs),
            StatusText: LocalizationText.Format("Disc.Polling.Mp3.Ready.Status", discNumber, totalDiscs),
            ProgressText: LocalizationText.Format("Disc.Polling.Mp3.Ready.Progress", discNumber),
            DisplayState: DiscPollingDisplayState.Ready);
    }
}
