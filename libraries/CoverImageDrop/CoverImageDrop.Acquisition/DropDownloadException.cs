namespace CoverImageDrop.Acquisition;

public sealed class DropDownloadException : Exception
{
    public DropDownloadException(string message, Exception? innerException, IReadOnlyList<DropDownloadAttempt> attempts)
        : base(message, innerException)
    {
        Attempts = attempts;
    }

    public IReadOnlyList<DropDownloadAttempt> Attempts { get; }
}
