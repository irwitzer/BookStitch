using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MainWindowRuntimeCompositionTests
{
    [Fact]
    public void Create_ProvidesRuntimeServices()
    {
        using var folder = new TemporaryFolder();
        var settings = new AppSettings();

        var composition = MainWindowRuntimeComposition.Create(
            folder.Path,
            () => settings,
            () => null);

        Assert.NotNull(composition.DiagnosticLogService);
        Assert.NotNull(composition.NotificationService);
        Assert.Equal(
            Path.Combine(folder.Path, "software-settings", "logs"),
            composition.DiagnosticLogService.LogFolder);
    }

    [Fact]
    public void Create_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentException>(
            () => MainWindowRuntimeComposition.Create(
                string.Empty,
                () => new AppSettings(),
                () => null));

        Assert.Throws<ArgumentNullException>(
            () => MainWindowRuntimeComposition.Create(
                @"C:\Test",
                null!,
                () => null));

        Assert.Throws<ArgumentNullException>(
            () => MainWindowRuntimeComposition.Create(
                @"C:\Test",
                () => new AppSettings(),
                null!));
    }
}