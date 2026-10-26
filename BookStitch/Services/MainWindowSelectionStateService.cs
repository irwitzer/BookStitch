namespace BookStitch.Services;

public sealed record SelectionStateChange<T>(
    T Value,
    bool HasChanged);

public static class MainWindowSelectionStateService
{
    public static SelectionStateChange<bool> CreateBooleanChange(
        bool currentValue,
        bool incomingValue) =>
        new(
            incomingValue,
            currentValue != incomingValue);

    public static SelectionStateChange<T?> CreateReferenceChange<T>(
        T? currentValue,
        T? incomingValue)
        where T : class =>
        new(
            incomingValue,
            !ReferenceEquals(currentValue, incomingValue));

    public static SelectionStateChange<string> CreateOrdinalStringChange(
        string currentValue,
        string normalizedIncomingValue) =>
        new(
            normalizedIncomingValue,
            !string.Equals(
                currentValue,
                normalizedIncomingValue,
                StringComparison.Ordinal));

}