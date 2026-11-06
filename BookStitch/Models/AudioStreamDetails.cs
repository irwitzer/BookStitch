namespace BookStitch.Models;

// Measured stream metadata, never a substitute for preparation/merge authorization.
public sealed record AudioStreamDetails(string Codec, string? Profile, int SampleRateHz, int Channels,
    decimal? BitrateBitsPerSecond)
{
    public static AudioStreamDetails? FromProbe(AudioProbeInfo probe) => probe.Success && !probe.HasProbeErrors &&
        probe.SampleRateHz is > 0 && probe.Channels is > 0
        ? new(probe.CodecName ?? "", probe.AudioProfile, probe.SampleRateHz.Value, probe.Channels.Value, probe.AudioBitrateBitsPerSecond)
        : null;
}
