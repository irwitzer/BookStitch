using System.ComponentModel;
using System.IO;

namespace BookStitch.Services;

public sealed class AudioDiscSectorReadException : IOException
{
    public AudioDiscSectorReadException(
        int startSector,
        int sectorCount,
        int attempts,
        Exception innerException)
        : base(
            $"Die CD-Sektoren ab Position {startSector} konnten nach {attempts} Versuchen nicht zuverlässig gelesen werden. " +
            innerException.Message,
            innerException)
    {
        StartSector = startSector;
        SectorCount = sectorCount;
        Attempts = attempts;
    }

    public int StartSector { get; }

    public int SectorCount { get; }

    public int Attempts { get; }
}

/// <summary>
/// Repeats short, isolated raw-sector reads a limited number of times. This is
/// deliberately a small robustness layer, not a secure-ripping implementation.
/// </summary>
public sealed class AudioDiscSectorReadRetryService
{
    private static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(600)
    ];

    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    public AudioDiscSectorReadRetryService(IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _retryDelays = retryDelays is null
            ? DefaultRetryDelays
            : [.. retryDelays];

        if (_retryDelays.Any(delay => delay < TimeSpan.Zero))
            throw new ArgumentOutOfRangeException(nameof(retryDelays), "Retry-Verzögerungen dürfen nicht negativ sein.");
    }

    public async Task<byte[]> ReadAsync(
        int startSector,
        int sectorCount,
        Func<int, int, byte[]> readSectors,
        CancellationToken cancellationToken)
    {
        if (startSector < 0)
            throw new ArgumentOutOfRangeException(nameof(startSector));
        if (sectorCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sectorCount));
        ArgumentNullException.ThrowIfNull(readSectors);

        var maximumAttempts = _retryDelays.Count + 1;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return readSectors(startSector, sectorCount);
            }
            catch (Exception ex) when (IsRetriable(ex))
            {
                lastError = ex;
                if (attempt >= maximumAttempts)
                    break;

                await Task.Delay(_retryDelays[attempt - 1], cancellationToken);
            }
        }

        throw new AudioDiscSectorReadException(
            startSector,
            sectorCount,
            maximumAttempts,
            lastError ?? new IOException("Unbekannter CD-Lesefehler."));
    }

    private static bool IsRetriable(Exception exception) =>
        exception is Win32Exception or IOException;
}
