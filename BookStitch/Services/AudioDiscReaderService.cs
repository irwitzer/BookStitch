using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BookStitch.Models;

namespace BookStitch.Services;

/// <summary>
/// Reads the table of contents of a Windows audio CD without ripping audio data.
/// MCI remains the device-access boundary for the first pipeline version. Exact
/// frame offsets are requested separately so the result can be used for a
/// MusicBrainz-compatible disc lookup without coupling metadata access to ripping.
/// </summary>
public sealed class AudioDiscReaderService
{
    private readonly AudioDiscTocReaderService _tocReaderService = new();

    public AudioDiscReadResult ReadDisc(string sourcePath)
    {
        var driveRoot = NormalizeDriveRoot(sourcePath);
        if (string.IsNullOrWhiteSpace(driveRoot))
            return AudioDiscReadResult.NotAudioDisc("Das ausgewählte CD-Laufwerk ist ungültig.");

        var exactToc = _tocReaderService.TryReadToc(driveRoot);
        var alias = $"BookStitchAudioDisc_{Guid.NewGuid():N}";

        try
        {
            SendRequired($"open {Quote(driveRoot.TrimEnd('\\'))} type cdaudio alias {alias} shareable");

            SendRequired($"set {alias} time format milliseconds");
            var trackCount = ReadPositiveInt($"status {alias} number of tracks");
            if (trackCount <= 0)
                return AudioDiscReadResult.NotAudioDisc();

            var rawTracks = ReadTrackTimes(alias, trackCount);
            var toc = exactToc is not null && exactToc.LastTrackNumber - exactToc.FirstTrackNumber + 1 == trackCount
                ? exactToc
                : null;
            var discIdentity = CreateDiscIdentity(rawTracks);

            var tracks = rawTracks
                .Select(track => new AudioDiscTrackInfo(
                    track.Number,
                    TimeSpan.FromMilliseconds(track.StartMilliseconds),
                    TimeSpan.FromMilliseconds(track.DurationMilliseconds),
                    CreateTrackIdentity(discIdentity, track.Number, track.StartMilliseconds, track.DurationMilliseconds),
                    toc?.TrackSectorOffsets.ElementAtOrDefault(track.Number - 1)))
                .ToList();

            var totalDuration = TimeSpan.FromMilliseconds(
                rawTracks.Max(track => track.StartMilliseconds + track.DurationMilliseconds));

            return AudioDiscReadResult.Success(new AudioDiscInfo(
                driveRoot,
                driveRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                tracks,
                totalDuration,
                discIdentity,
                toc));
        }
        catch (AudioDiscReadException ex)
        {
            return AudioDiscReadResult.NotAudioDisc(ex.Message);
        }
        catch (Exception ex)
        {
            return AudioDiscReadResult.NotAudioDisc($"Die Audio-CD konnte nicht gelesen werden: {ex.Message}");
        }
        finally
        {
            try
            {
                NativeMciSendString($"close {alias}", null, 0, IntPtr.Zero);
            }
            catch
            {
                // Releasing the MCI alias is best-effort and must never close BookStitch.
            }
        }
    }

    public static string CreateDiscIdentity(
        IEnumerable<(int Number, long StartMilliseconds, long DurationMilliseconds)> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var normalized = tracks
            .OrderBy(track => track.Number)
            .Select(track => $"{track.Number}:{track.StartMilliseconds}:{track.DurationMilliseconds}");

        return ComputeSha256(string.Join("|", normalized));
    }

    public static string CreateTrackIdentity(
        string discIdentity,
        int trackNumber,
        long startMilliseconds,
        long durationMilliseconds)
    {
        return ComputeSha256($"{discIdentity}|{trackNumber}|{startMilliseconds}|{durationMilliseconds}");
    }

    public static string CreateMusicBrainzDiscId(
        int firstTrackNumber,
        int lastTrackNumber,
        int leadOutSectorOffset,
        IReadOnlyList<int> trackSectorOffsets)
    {
        ArgumentNullException.ThrowIfNull(trackSectorOffsets);
        if (firstTrackNumber < 1 || lastTrackNumber < firstTrackNumber || lastTrackNumber > 99)
            throw new ArgumentOutOfRangeException(nameof(lastTrackNumber));
        if (trackSectorOffsets.Count != lastTrackNumber - firstTrackNumber + 1)
            throw new ArgumentException("Die Anzahl der Track-Offsets passt nicht zum Trackbereich.", nameof(trackSectorOffsets));

        var hashInput = new StringBuilder(804);
        hashInput.Append(firstTrackNumber.ToString("X2", CultureInfo.InvariantCulture));
        hashInput.Append(lastTrackNumber.ToString("X2", CultureInfo.InvariantCulture));
        hashInput.Append(leadOutSectorOffset.ToString("X8", CultureInfo.InvariantCulture));

        var offsetsByTrack = new Dictionary<int, int>();
        for (var index = 0; index < trackSectorOffsets.Count; index++)
            offsetsByTrack[firstTrackNumber + index] = trackSectorOffsets[index];

        for (var trackNumber = 1; trackNumber <= 99; trackNumber++)
        {
            var offset = offsetsByTrack.GetValueOrDefault(trackNumber);
            hashInput.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
        }

        var digest = SHA1.HashData(Encoding.ASCII.GetBytes(hashInput.ToString()));
        return Convert.ToBase64String(digest)
            .Replace('+', '.')
            .Replace('/', '_')
            .Replace('=', '-');
    }

    private static List<(int Number, long StartMilliseconds, long DurationMilliseconds)> ReadTrackTimes(
        string alias,
        int trackCount)
    {
        var rawTracks = new List<(int Number, long StartMilliseconds, long DurationMilliseconds)>(trackCount);
        long cumulativeStart = 0;

        for (var trackNumber = 1; trackNumber <= trackCount; trackNumber++)
        {
            var durationMilliseconds = ReadPositiveLong($"status {alias} length track {trackNumber}");
            if (durationMilliseconds <= 0)
                throw new AudioDiscReadException($"Track {trackNumber:00} besitzt keine lesbare Dauer.");

            var reportedStart = ReadNonNegativeLongOrDefault(
                $"status {alias} position track {trackNumber}",
                cumulativeStart);

            var startMilliseconds = reportedStart >= cumulativeStart
                ? reportedStart
                : cumulativeStart;

            rawTracks.Add((trackNumber, startMilliseconds, durationMilliseconds));
            cumulativeStart = startMilliseconds + durationMilliseconds;
        }

        return rawTracks;
    }

    private static string NormalizeDriveRoot(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;

        try
        {
            var root = Path.GetPathRoot(sourcePath.Trim());
            return string.IsNullOrWhiteSpace(root) ? string.Empty : root;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static int ReadPositiveInt(string command)
    {
        var value = ReadValue(command);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static long ReadPositiveLong(string command)
    {
        var value = ReadValue(command);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static long ReadNonNegativeLongOrDefault(string command, long fallback)
    {
        try
        {
            var value = ReadValue(command);
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
                ? parsed
                : fallback;
        }
        catch (AudioDiscReadException)
        {
            return fallback;
        }
    }

    private static string ReadValue(string command)
    {
        var buffer = new StringBuilder(256);
        var errorCode = NativeMciSendString(command, buffer, buffer.Capacity, IntPtr.Zero);
        if (errorCode != 0)
            throw new AudioDiscReadException(GetMciErrorMessage(errorCode));

        return buffer.ToString().Trim();
    }

    private static void SendRequired(string command)
    {
        var errorCode = NativeMciSendString(command, null, 0, IntPtr.Zero);
        if (errorCode != 0)
            throw new AudioDiscReadException(GetMciErrorMessage(errorCode));
    }

    private static string GetMciErrorMessage(int errorCode)
    {
        var buffer = new StringBuilder(256);
        return NativeMciGetErrorString(errorCode, buffer, buffer.Capacity)
            ? buffer.ToString().Trim()
            : $"Windows-MCI-Fehler {errorCode}.";
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int NativeMciSendString(
        string command,
        StringBuilder? returnValue,
        int returnLength,
        IntPtr callback);

    [DllImport("winmm.dll", EntryPoint = "mciGetErrorStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeMciGetErrorString(
        int errorCode,
        StringBuilder errorText,
        int errorTextSize);

    private sealed class AudioDiscReadException(string message) : Exception(message);
}
