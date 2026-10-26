namespace BookStitch.Services;

public sealed record BookStitchEnvironmentContext(
    BookStitchEnvironment Environment,
    string? RepositoryRoot,
    string AppDataRoot,
    string DefaultProjectRoot)
{
    public bool IsProduction => Environment == BookStitchEnvironment.Production;

    public string BrandText =>
        Environment switch
        {
            BookStitchEnvironment.Development => "BookDevel.",
            BookStitchEnvironment.XTester => "BookTester",
            _ => "BookStitch"
        };

    public string DisplayName =>
        Environment switch
        {
            BookStitchEnvironment.Development => "Development",
            BookStitchEnvironment.XTester => "Tester",
            _ => "Standard"
        };

    public string WindowTitle =>
        Environment switch
        {
            BookStitchEnvironment.Development => "BookStitch [Development]",
            BookStitchEnvironment.XTester => "BookStitch [Tester]",
            _ => "BookStitch"
        };
}
