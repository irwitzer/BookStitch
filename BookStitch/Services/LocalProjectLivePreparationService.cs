using System.IO;
using System.Runtime.ExceptionServices;

namespace BookStitch.Services;

public sealed record LocalProjectLivePreparationRequest(
    string SourceFolder,
    IReadOnlyCollection<string> SourceFiles,
    string ProjectFolder,
    int ParallelJobs,
    Func<Task>? WaitForPreparedChildrenAsync = null);

public sealed record LocalProjectLivePreparationProgress(
    int CopiedFiles,
    int TotalFiles,
    int PreparedFiles,
    string CurrentFileName,
    IReadOnlyList<string> ActiveFileNames,
    IReadOnlyList<int> ActiveTrackNumbers,
    double ActivePreparationFraction = 0);

public sealed record LocalProjectLivePreparationResult(
    LocalProjectImportResult ImportResult,
    int PreparedFiles,
    bool WasCanceled);

public sealed class LocalProjectLivePreparationService
{
    private readonly ILocalProjectImportService _importService;

    public LocalProjectLivePreparationService(ILocalProjectImportService importService)
    {
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
    }

    public async Task<LocalProjectLivePreparationResult> RunAsync(
        LocalProjectLivePreparationRequest request,
        Func<LocalProjectCopiedFile, IProgress<double>, CancellationToken, Task> prepareCopiedFileAsync,
        IProgress<LocalProjectLivePreparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(prepareCopiedFileAsync);

        var parallelJobs = Math.Clamp(request.ParallelJobs, 1, 40);
        using var workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var workerToken = workerCancellation.Token;
        Exception? preparationException = null;
        using var semaphore = new SemaphoreSlim(parallelJobs, parallelJobs);
        var preparationTasks = new List<Task>();
        var preparedFiles = 0;
        var copiedFiles = 0;
        var totalFiles = request.SourceFiles.Count;
        var activePreparationFractions = new Dictionary<int, double>();
        var syncRoot = new object();
        var sourceIndexes = request.SourceFiles
            .Select((path, index) => new { Path = Path.GetFullPath(path), TrackNumber = index + 1 })
            .ToDictionary(item => item.Path, item => item.TrackNumber, StringComparer.OrdinalIgnoreCase);
        var activeFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeTrackNumbers = new HashSet<int>();

        void Report(string currentFileName)
        {
            string[] activeFileSnapshot;
            int[] activeTrackSnapshot;
            double activePreparationFraction;
            lock (syncRoot)
            {
                activeFileSnapshot = activeFileNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                activeTrackSnapshot = activeTrackNumbers.OrderBy(number => number).ToArray();
                activePreparationFraction = activePreparationFractions.Values.Sum();
            }

            progress?.Report(new LocalProjectLivePreparationProgress(
                Volatile.Read(ref copiedFiles),
                totalFiles,
                Volatile.Read(ref preparedFiles),
                currentFileName,
                activeFileSnapshot,
                activeTrackSnapshot,
                activePreparationFraction));
        }

        var copiedFileProgress = new CallbackProgress<LocalProjectCopiedFile>(copiedFile =>
        {
            Interlocked.Exchange(ref copiedFiles, copiedFile.CompletedFiles);
            Report(Path.GetFileName(copiedFile.TargetFile));

            var task = PrepareAsync(copiedFile);
            lock (syncRoot)
                preparationTasks.Add(task);
        });

        async Task PrepareAsync(LocalProjectCopiedFile copiedFile)
        {
            var semaphoreEntered = false;
            int? activeTrackNumber = null;

            try
            {
                await semaphore.WaitAsync(workerToken).ConfigureAwait(false);
                semaphoreEntered = true;
                workerToken.ThrowIfCancellationRequested();

                var activeFileName = Path.GetFileName(copiedFile.TargetFile);
                var sourcePath = Path.GetFullPath(copiedFile.SourceFile);
                lock (syncRoot)
                {
                    activeFileNames.Add(activeFileName);
                    if (sourceIndexes.TryGetValue(sourcePath, out var trackNumber))
                    {
                        activeTrackNumber = trackNumber;
                        activeTrackNumbers.Add(trackNumber);
                        activePreparationFractions[trackNumber] = 0;
                    }
                }
                Report(activeFileName);

                var preparationProgress = new CallbackProgress<double>(fraction =>
                {
                    if (activeTrackNumber is not int trackNumber)
                        return;

                    lock (syncRoot)
                    {
                        activePreparationFractions[trackNumber] = Math.Clamp(fraction, 0d, 0.999d);
                    }

                    Report(activeFileName);
                });

                await prepareCopiedFileAsync(copiedFile, preparationProgress, workerToken).ConfigureAwait(false);
                workerToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref preparedFiles);
                if (activeTrackNumber is int completedTrackNumber)
                {
                    lock (syncRoot)
                        activePreparationFractions[completedTrackNumber] = 0;
                }
                Report(Path.GetFileName(copiedFile.TargetFile));
            }
            catch (OperationCanceledException) when (workerToken.IsCancellationRequested)
            {
                // Ein kontrollierter Benutzerabbruch ist kein Vorbereitungsfehler.
            }
            catch (Exception error)
            {
                lock (syncRoot) preparationException ??= error;
                // A failed critical checkpoint must stop acquisition and sibling workers,
                // then settle children before publishing the original controlled failure.
                try { await workerCancellation.CancelAsync().ConfigureAwait(false); }
                catch (Exception cancelError) { System.Diagnostics.Trace.TraceWarning("Preparation cancellation failed: {0}", cancelError); }
            }
            finally
            {
                if (semaphoreEntered)
                {
                    var activeFileName = Path.GetFileName(copiedFile.TargetFile);
                    var sourcePath = Path.GetFullPath(copiedFile.SourceFile);
                    lock (syncRoot)
                    {
                        activeFileNames.Remove(activeFileName);
                        if (sourceIndexes.TryGetValue(sourcePath, out var trackNumber))
                        {
                            activeTrackNumbers.Remove(trackNumber);
                            activePreparationFractions.Remove(trackNumber);
                        }
                    }
                    Report(activeFileName);
                    semaphore.Release();
                }
            }
        }

        LocalProjectImportResult? importResult = null;
        Exception? importException = null;

        try
        {
            importResult = await _importService.CopySourcesAsync(
                request.SourceFolder,
                request.SourceFiles,
                request.ProjectFolder,
                progress: null,
                copiedFileProgress,
                workerToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            importException = ex;
        }

        Task[] tasks;
        lock (syncRoot)
            tasks = preparationTasks.ToArray();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch when (importException is not null)
        {
            // Preserve the original import failure after preparation producers settle.
        }
        finally
        {
            if (request.WaitForPreparedChildrenAsync is not null)
                await request.WaitForPreparedChildrenAsync().ConfigureAwait(false);
        }

        if (preparationException is not null && (importException is null or OperationCanceledException))
            ExceptionDispatchInfo.Capture(preparationException).Throw();

        if (importException is not null)
        {
            if (importException is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                return new LocalProjectLivePreparationResult(
                    new LocalProjectImportResult(
                        request.SourceFolder,
                        request.ProjectFolder,
                        Path.Combine(request.ProjectFolder, LocalProjectImportService.OriginalsFolderName),
                        totalFiles,
                        Volatile.Read(ref copiedFiles),
                        WasCanceled: true),
                    Volatile.Read(ref preparedFiles),
                    WasCanceled: true);
            }

            ExceptionDispatchInfo.Capture(importException).Throw();
        }

        return new LocalProjectLivePreparationResult(
            importResult!,
            Volatile.Read(ref preparedFiles),
            cancellationToken.IsCancellationRequested);
    }

    public Task<LocalProjectLivePreparationResult> RunAsync(
        LocalProjectLivePreparationRequest request,
        Func<LocalProjectCopiedFile, CancellationToken, Task> prepareCopiedFileAsync,
        IProgress<LocalProjectLivePreparationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepareCopiedFileAsync);

        return RunAsync(
            request,
            (copiedFile, _, token) => prepareCopiedFileAsync(copiedFile, token),
            progress,
            cancellationToken);
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
