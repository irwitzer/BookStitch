namespace BookStitch.Models;

// Optional packet-timing cache. The work manifest owns the prepared artifact identity.
public sealed record ConvertedAacTiming(
    int Version,
    long Length,
    long LastWriteUtcTicks,
    long SpanUnits,
    long TimeBaseNumerator,
    long TimeBaseDenominator)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Version == 1 && Length > 0 &&
        SpanUnits > 0 && TimeBaseNumerator > 0 && TimeBaseDenominator > 0;

    [System.Text.Json.Serialization.JsonIgnore]
    public decimal DurationSeconds => (decimal)SpanUnits * TimeBaseNumerator / TimeBaseDenominator;
}
