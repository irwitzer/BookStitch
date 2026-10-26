namespace BookStitch.Services;

public sealed record ProgressValueChange<T>(
    T Value,
    bool HasChanged);

public static class ProgressStateChangeService
{
    public static ProgressValueChange<double> CreatePercentChange(
        double currentValue,
        double incomingValue)
    {
        var normalized = Math.Clamp(incomingValue, 0, 100);

        return new ProgressValueChange<double>(
            normalized,
            Math.Abs(currentValue - normalized) >= 0.05);
    }

    public static ProgressValueChange<string> CreateForegroundChange(
        string currentValue,
        string? incomingValue)
    {
        var normalized = incomingValue ?? string.Empty;

        return new ProgressValueChange<string>(
            normalized,
            !string.Equals(
                currentValue,
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    public static ProgressValueChange<string> CreateTextChange(
        string currentValue,
        string? incomingValue)
    {
        var normalized = incomingValue ?? string.Empty;

        return new ProgressValueChange<string>(
            normalized,
            !string.Equals(
                currentValue,
                normalized,
                StringComparison.Ordinal));
    }

    public static ProgressValueChange<bool> CreateIndeterminateChange(
        bool currentValue,
        bool incomingValue) =>
        CreateBooleanChange(currentValue, incomingValue);

    public static ProgressValueChange<bool> CreateAnalysisActiveChange(
        bool currentValue,
        bool incomingValue) =>
        CreateBooleanChange(currentValue, incomingValue);

    private static ProgressValueChange<bool> CreateBooleanChange(
        bool currentValue,
        bool incomingValue) =>
        new(
            incomingValue,
            currentValue != incomingValue);
}