namespace BookStitch.Services;

public sealed class MainWindowDiscComposition
{
    private MainWindowDiscComposition()
    {
        Mp3DiscImportService = new Mp3DiscImportService();
        Mp3DiscUiStatusService = new Mp3DiscUiStatusService();
        Mp3DiscPreparationService = new Mp3DiscPreparationService();
        Mp3DiscTrackReconciliationService = new Mp3DiscTrackReconciliationService();
        DiscDriveMeasurementStore = new DiscDriveMeasurementStore();
        DiscDriveService = new DiscDriveService(DiscDriveMeasurementStore);
        DiscDriveConfigurationService = new DiscDriveConfigurationService();
        DiscDriveRotationService = new DiscDriveRotationService();
        AudioDiscReaderService = new AudioDiscReaderService();
        AudioDiscRipService = new AudioDiscRipService();
        AudioDiscPipelineTimingService = new AudioDiscPipelineTimingService();
        AudioDiscLiveConversionService = new AudioDiscLiveConversionService();
    }

    public Mp3DiscImportService Mp3DiscImportService { get; }
    public Mp3DiscUiStatusService Mp3DiscUiStatusService { get; }
    public Mp3DiscPreparationService Mp3DiscPreparationService { get; }
    public Mp3DiscTrackReconciliationService Mp3DiscTrackReconciliationService { get; }
    public DiscDriveMeasurementStore DiscDriveMeasurementStore { get; }
    public DiscDriveService DiscDriveService { get; }
    public DiscDriveConfigurationService DiscDriveConfigurationService { get; }
    public DiscDriveRotationService DiscDriveRotationService { get; }
    public AudioDiscReaderService AudioDiscReaderService { get; }
    public AudioDiscRipService AudioDiscRipService { get; }
    public AudioDiscPipelineTimingService AudioDiscPipelineTimingService { get; }
    public AudioDiscLiveConversionService AudioDiscLiveConversionService { get; }

    public static MainWindowDiscComposition Create() => new();
}