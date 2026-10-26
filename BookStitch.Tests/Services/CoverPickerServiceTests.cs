using BookStitch.Dialog;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class CoverPickerServiceTests
{
    [Fact]
    public void Standard_folder_project_prefers_source_folder()
    {
        using var root = new TemporaryFolder();
        var source = Directory.CreateDirectory(Path.Combine(root.Path, "source")).FullName;
        var fallback = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var settings = new AppSettings { LastCoverFolder = fallback };
        var service = new CoverPickerService(settings, () => { }, () => null);

        var result = service.ResolveInitialDirectory(new CoverPickerContext(
            ProjectSetupSourceKind.Folder,
            source,
            null,
            fallback));

        Assert.Equal(source, result);
    }

    [Fact]
    public void Disc_project_ignores_source_folder_and_uses_fallback()
    {
        using var root = new TemporaryFolder();
        var discSource = Directory.CreateDirectory(Path.Combine(root.Path, "disc-source")).FullName;
        var fallback = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var service = new CoverPickerService(new AppSettings(), () => { }, () => null);

        var result = service.ResolveInitialDirectory(new CoverPickerContext(
            ProjectSetupSourceKind.Mp3Disc,
            discSource,
            null,
            fallback));

        Assert.Equal(fallback, result);
    }

    [Fact]
    public void Remembered_folder_has_priority_when_enabled_and_valid()
    {
        using var root = new TemporaryFolder();
        var source = Directory.CreateDirectory(Path.Combine(root.Path, "source")).FullName;
        var remembered = Directory.CreateDirectory(Path.Combine(root.Path, "remembered")).FullName;
        var settings = new AppSettings
        {
            RememberLastCoverPickerFolder = true,
            LastUserSelectedCoverFolder = remembered
        };
        var service = new CoverPickerService(settings, () => { }, () => null);

        var result = service.ResolveInitialDirectory(new CoverPickerContext(
            ProjectSetupSourceKind.Folder,
            source,
            null,
            null));

        Assert.Equal(remembered, result);
    }

    [Fact]
    public void Invalid_remembered_folder_falls_back_to_standard_policy()
    {
        using var root = new TemporaryFolder();
        var source = Directory.CreateDirectory(Path.Combine(root.Path, "source")).FullName;
        var settings = new AppSettings
        {
            RememberLastCoverPickerFolder = true,
            LastUserSelectedCoverFolder = Path.Combine(root.Path, "missing")
        };
        var service = new CoverPickerService(settings, () => { }, () => null);

        var result = service.ResolveInitialDirectory(new CoverPickerContext(
            ProjectSetupSourceKind.Folder,
            source,
            null,
            null));

        Assert.Equal(source, result);
    }

    [Fact]
    public void Internal_bookstitch_folder_is_rejected_as_automatic_start_folder()
    {
        using var root = new TemporaryFolder();
        var internalFolder = Directory.CreateDirectory(Path.Combine(root.Path, "working", "project")).FullName;
        var fallback = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var workingRoot = Path.Combine(root.Path, "working");
        var service = new CoverPickerService(new AppSettings(), () => { }, () => workingRoot);

        var result = service.ResolveInitialDirectory(new CoverPickerContext(
            ProjectSetupSourceKind.Folder,
            internalFolder,
            null,
            fallback));

        Assert.Equal(fallback, result);
    }

    [Fact]
    public void Successful_picker_selection_records_only_user_folder_when_remembering_is_enabled()
    {
        using var root = new TemporaryFolder();
        var selectedFile = root.CreateFile(Path.Combine("covers", "cover.jpg"));
        var saveCount = 0;
        var settings = new AppSettings { RememberLastCoverPickerFolder = true };
        var service = new CoverPickerService(settings, () => saveCount++, () => null);

        service.RecordSuccessfulPickerSelection(selectedFile);

        Assert.Equal(Path.GetDirectoryName(selectedFile), settings.LastUserSelectedCoverFolder);
        Assert.Equal(1, saveCount);
    }

    [Fact]
    public void Disabled_remember_setting_does_not_record_user_folder()
    {
        using var root = new TemporaryFolder();
        var selectedFile = root.CreateFile(Path.Combine("covers", "cover.jpg"));
        var settings = new AppSettings { RememberLastCoverPickerFolder = false };
        var service = new CoverPickerService(settings, () => { }, () => null);

        service.RecordSuccessfulPickerSelection(selectedFile);

        Assert.Null(settings.LastUserSelectedCoverFolder);
    }
}
