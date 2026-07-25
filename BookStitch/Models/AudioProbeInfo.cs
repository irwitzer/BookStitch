namespace BookStitch.Models;

public class AudioProbeInfo
{
    public string FilePath { get; set; } = "";

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public string? FormatName { get; set; }

    public string? FormatLongName { get; set; }

    public string? CodecName { get; set; }

    public string? CodecLongName { get; set; }

    public string? CodecType { get; set; }

    public int? BitrateKbps { get; set; }

    public int? SampleRateHz { get; set; }

    public int? Channels { get; set; }

    public TimeSpan? Duration { get; set; }

    public List<TrackEmbeddedChapterInfo> Chapters { get; set; } = [];

    public bool IsAac => string.Equals(CodecName, "aac", StringComparison.OrdinalIgnoreCase);

    public bool IsMp3 => string.Equals(CodecName, "mp3", StringComparison.OrdinalIgnoreCase);

    public bool IsAlac => string.Equals(CodecName, "alac", StringComparison.OrdinalIgnoreCase);

    public bool IsFlac => string.Equals(CodecName, "flac", StringComparison.OrdinalIgnoreCase);

    public bool IsPcm => !string.IsNullOrWhiteSpace(CodecName) &&
                         CodecName.StartsWith("pcm", StringComparison.OrdinalIgnoreCase);

    public bool IsAudio => string.Equals(CodecType, "audio", StringComparison.OrdinalIgnoreCase);

    public bool HasPlausibleAudioProperties =>
        Success &&
        IsAudio &&
        Channels is > 0 &&
        SampleRateHz is > 0 &&
        Duration is { } duration && duration > TimeSpan.Zero;

    public string DurationText
    {
        get
        {
            if (Duration is null)
                return "";

            var duration = Duration.Value;

            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

            return $"{duration.Minutes:00}:{duration.Seconds:00}";
        }
    }
}
