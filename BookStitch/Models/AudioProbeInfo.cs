namespace BookStitch.Models;

public class AudioProbeInfo
{
    public string FilePath { get; set; } = "";

    public int StreamCount { get; set; }
    public int AudioStreamCount { get; set; }
    public bool HasNonCoverVideo { get; set; }
    public string? AudioProfile { get; set; }
    public string? AudioTimeBase { get; set; }
    public string? AudioChannelLayout { get; set; }
    public string? SampleFormat { get; set; }
    public int AacExtradataSize { get; set; }
    public string? AacConfigurationId { get; set; }
    public bool SupportsAacStreamCopy => HasPlausibleAudioProperties && IsAac &&
        AudioStreamCount == 1 && !HasNonCoverVideo && AudioProfile == "LC" &&
        SampleFormat == "fltp" && AacExtradataSize > 0 &&
        AudioTimeBase == $"1/{SampleRateHz}" &&
        ((Channels == 1 && AudioChannelLayout == "mono") || (Channels == 2 && AudioChannelLayout == "stereo")) &&
        !string.IsNullOrWhiteSpace(AacConfigurationId);

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public bool TimedOut { get; set; }

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
