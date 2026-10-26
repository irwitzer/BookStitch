using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class EnvironmentBootstrapWiringTests
{
    [Fact]
    public void Diagnostics_and_main_window_share_the_single_app_environment_context()
    {
        var app = RepositorySource.Read(["BookStitch", "App.xaml.cs"]);
        var mainWindow = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

        Assert.Contains(
            "_activeEnvironmentContext ??= new BookStitchEnvironmentResolver(_projectEnvironmentSelectionService).Resolve()",
            app,
            StringComparison.Ordinal);
        Assert.Contains("InitializeDiagnostics(ActiveEnvironmentContext)", app, StringComparison.Ordinal);
        Assert.Contains("new SettingsService(environmentContext)", app, StringComparison.Ordinal);
        Assert.Contains("app.ActiveEnvironmentContext", mainWindow, StringComparison.Ordinal);
        var normalizedMainWindow = mainWindow.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("MainWindowServiceComposition.Create(\n            environmentContext,", normalizedMainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Relaunch_occurs_from_app_exit_and_not_from_the_running_window()
    {
        var app = RepositorySource.Read(["BookStitch", "App.xaml.cs"]);
        var mainWindow = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

        Assert.Contains("_applicationRestartService.TryRelaunchOnExit();", app, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRelaunchOnExit", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ApplicationRestartService.RequestRestart()", mainWindow, StringComparison.Ordinal);
    }
}
