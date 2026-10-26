namespace BookStitch.Services;

public enum OutputFolderPrimaryAction
{
    Open,
    Change
}

public static class OutputFolderPrimaryActionService
{
    public const string DefaultAction = nameof(OutputFolderPrimaryAction.Open);

    public static string Normalize(string? value)
    {
        return Enum.TryParse<OutputFolderPrimaryAction>(value, ignoreCase: true, out var action)
            ? action.ToString()
            : DefaultAction;
    }

    public static OutputFolderPrimaryAction Resolve(string? preferredAction, bool canOpenOutputFolder)
    {
        if (!canOpenOutputFolder)
            return OutputFolderPrimaryAction.Change;

        var normalized = Normalize(preferredAction);
        return string.Equals(
            normalized,
            nameof(OutputFolderPrimaryAction.Change),
            StringComparison.Ordinal)
                ? OutputFolderPrimaryAction.Change
                : OutputFolderPrimaryAction.Open;
    }
}
