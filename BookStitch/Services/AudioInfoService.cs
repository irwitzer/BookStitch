using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using BookStitch.Models;

namespace BookStitch.Services;

public class AudioInfoService
{
    public async Task<AudioProbeInfo> ProbeAsync(string filePath, string? ffprobePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new AudioProbeInfo
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = "Datei wurde nicht gefunden."
            };
        }

        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
        {
            return new AudioProbeInfo
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = "ffprobe.exe wurde nicht gefunden."
            };
        }

        try
        {
            var arguments =
                "-v error " +
                "-print_format json " +
                "-show_format " +
                "-show_streams " +
                "-show_chapters " +
                $"\"{filePath}\"";

            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                return new AudioProbeInfo
                {
                    FilePath = filePath,
                    Success = false,
                    ErrorMessage = string.IsNullOrWhiteSpace(error)
                        ? $"ffprobe beendet mit ExitCode {process.ExitCode}."
                        : error.Trim()
                };
            }

            return ParseProbeJson(filePath, output);
        }
        catch (Exception ex)
        {
            return new AudioProbeInfo
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> CanDecodeAudioAsync(string filePath, string? ffmpegPath, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) ||
            string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            return false;
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-nostdin -v error -xerror -i \"{filePath}\" -map 0:a:0 -t 0.5 -f null -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                TryKill(process);
                return false;
            }

            await outputTask;
            await errorTask;
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Die kurze Prüfprobe darf beim Aufräumen keinen Folgefehler auslösen.
        }
    }

    private static AudioProbeInfo ParseProbeJson(string filePath, string json)
    {
        var info = new AudioProbeInfo
        {
            FilePath = filePath,
            Success = true
        };

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("format", out var format))
        {
            info.FormatName = GetStringOrNull(format, "format_name");
            info.FormatLongName = GetStringOrNull(format, "format_long_name");

            var formatBitrate = GetLongOrNull(format, "bit_rate");
            if (formatBitrate is not null)
                info.BitrateKbps = (int)Math.Round(formatBitrate.Value / 1000.0);

            var durationSeconds = GetDoubleOrNull(format, "duration");
            if (durationSeconds is not null)
                info.Duration = TimeSpan.FromSeconds(durationSeconds.Value);
        }

        if (root.TryGetProperty("streams", out var streams) &&
            streams.ValueKind == JsonValueKind.Array)
        {
            var audioStream = streams
                .EnumerateArray()
                .FirstOrDefault(stream =>
                    string.Equals(GetStringOrNull(stream, "codec_type"), "audio", StringComparison.OrdinalIgnoreCase));

            if (audioStream.ValueKind != JsonValueKind.Undefined)
            {
                info.CodecName = GetStringOrNull(audioStream, "codec_name");
                info.CodecLongName = GetStringOrNull(audioStream, "codec_long_name");
                info.CodecType = GetStringOrNull(audioStream, "codec_type");

                var sampleRateText = GetStringOrNull(audioStream, "sample_rate");
                if (int.TryParse(sampleRateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sampleRate))
                    info.SampleRateHz = sampleRate;

                var channels = GetIntOrNull(audioStream, "channels");
                if (channels is not null)
                    info.Channels = channels;

                var streamBitrate = GetLongOrNull(audioStream, "bit_rate");
                if (streamBitrate is not null)
                    info.BitrateKbps = (int)Math.Round(streamBitrate.Value / 1000.0);

                if (info.Duration is null)
                {
                    var streamDurationSeconds = GetDoubleOrNull(audioStream, "duration");
                    if (streamDurationSeconds is not null)
                        info.Duration = TimeSpan.FromSeconds(streamDurationSeconds.Value);
                }
            }
        }

        info.Chapters = ParseChapters(root);

        if (string.IsNullOrWhiteSpace(info.CodecName))
        {
            info.Success = false;
            info.ErrorMessage = "Keine Audiospur gefunden.";
        }

        return info;
    }

    private static List<TrackEmbeddedChapterInfo> ParseChapters(JsonElement root)
    {
        if (!root.TryGetProperty("chapters", out var chapters) || chapters.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<TrackEmbeddedChapterInfo>();
        var fallbackIndex = 1;

        foreach (var chapter in chapters.EnumerateArray())
        {
            var start = GetChapterTime(chapter, "start_time", "start");
            var end = GetChapterTime(chapter, "end_time", "end");
            if (start is null || end is null || end <= start)
                continue;

            var title = GetChapterTitle(chapter);
            if (string.IsNullOrWhiteSpace(title))
                title = "Kapitel " + fallbackIndex.ToString(CultureInfo.InvariantCulture);

            result.Add(new TrackEmbeddedChapterInfo
            {
                Title = title.Trim(),
                StartTicks = start.Value.Ticks,
                EndTicks = end.Value.Ticks
            });
            fallbackIndex++;
        }

        return result;
    }

    private static TimeSpan? GetChapterTime(JsonElement chapter, string secondsPropertyName, string integerPropertyName)
    {
        var seconds = GetDoubleOrNull(chapter, secondsPropertyName);
        if (seconds is not null)
            return TimeSpan.FromSeconds(seconds.Value);

        var integerValue = GetLongOrNull(chapter, integerPropertyName);
        if (integerValue is null)
            return null;

        var timeBase = GetStringOrNull(chapter, "time_base");
        if (TryParseTimeBase(timeBase, out var multiplier))
            return TimeSpan.FromSeconds(integerValue.Value * multiplier);

        return TimeSpan.FromMilliseconds(integerValue.Value);
    }

    private static bool TryParseTimeBase(string? value, out double multiplier)
    {
        multiplier = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
        {
            return false;
        }

        multiplier = numerator / denominator;
        return multiplier > 0;
    }

    private static string GetChapterTitle(JsonElement chapter)
    {
        if (!chapter.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
            return string.Empty;

        return GetStringOrNull(tags, "title")
               ?? GetStringOrNull(tags, "TITLE")
               ?? string.Empty;
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            _ => null
        };
    }

    private static int? GetIntOrNull(JsonElement element, string propertyName)
    {
        var text = GetStringOrNull(element, propertyName);

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }

    private static long? GetLongOrNull(JsonElement element, string propertyName)
    {
        var text = GetStringOrNull(element, propertyName);

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }

    private static double? GetDoubleOrNull(JsonElement element, string propertyName)
    {
        var text = GetStringOrNull(element, propertyName);

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;

        return null;
    }
}
