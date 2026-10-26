using System.IO;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class DialogOwnerFocusStructureTests
{
    [Fact]
    public void AppDialogService_RestoresOwnerInsteadOfClosedDialog()
    {
        var text = LoadText("BookStitch", "Services", "AppDialogService.cs");

        Assert.Contains("ModalDialogService.ShowDialog(dialog, owner, restoreOwnerFocus: restoreOwnerFocus)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dialog.Activate()", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalModalBookStitchDialogs_UseCentralModalDialogService()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var debugWindow = LoadText("BookStitch", "Windows", "DeveloperToolsWindow.xaml.cs");
        var dialogPreview = LoadText("BookStitch", "Dialog", "DialogPreviewWindow.xaml.cs");

        Assert.Contains("ModalDialogService.ShowDialog(settingsWindow, owner)", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("owner.Activate();", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("owner.Focus();", mainWindow, StringComparison.Ordinal);

        Assert.Contains("ModalDialogService.ShowDialog(dialog, this)", debugWindow, StringComparison.Ordinal);
        Assert.Contains("ModalDialogService.ShowDialog(dialog, this)", dialogPreview, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugWindow_IsIntentionallyModelessAndDoesNotUseCentralModalDialogServiceForItself()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");

        Assert.Contains("developerToolsWindow.Show();", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("debugWindow.ShowDialog", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ModalDialogService.ShowDialog(debugWindow", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeFilePickers_KeepNativeOwnerShowDialog()
    {
        var mainWindow = LoadText("BookStitch", "MainWindow.xaml.cs");
        var projectSetup = LoadText("BookStitch", "Dialog", "DiscProjectSetupDialog.xaml.cs");

        Assert.Contains("dialog.ShowDialog(this)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("folderDialog.ShowDialog(this)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("fileDialog.ShowDialog(this)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("dialog.ShowDialog(this)", projectSetup, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscWaitAndFatalShutdownDialogs_RemainOutsideCentralRestore()
    {
        var boxedDiscWait = LoadText("BookStitch", "Services", "BoxedDiscWaitDialogService.cs");
        var discWait = LoadText("BookStitch", "Services", "DiscWaitDialogService.cs");
        var app = LoadText("BookStitch", "App.xaml.cs");

        Assert.Contains("dialog.ShowDialog()", boxedDiscWait, StringComparison.Ordinal);
        Assert.Contains("_dialog.WaitForDiscAsync(", discWait, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyAppearanceResources", boxedDiscWait, StringComparison.Ordinal);
        Assert.DoesNotContain("ModalDialogService", boxedDiscWait, StringComparison.Ordinal);
        Assert.DoesNotContain("ModalDialogService", discWait, StringComparison.Ordinal);
        Assert.Contains("dialog.ShowDialog()", app, StringComparison.Ordinal);
        Assert.DoesNotContain("MessageBox.Show(", app, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalDialogServices_DoNotCopyAppearanceResourcesLocally()
    {
        var modalDialog = LoadText("BookStitch", "Services", "ModalDialogService.cs");
        var boxedDiscWait = LoadText("BookStitch", "Services", "BoxedDiscWaitDialogService.cs");

        Assert.DoesNotContain("CopyAppearanceResources", modalDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyAppearanceResources", boxedDiscWait, StringComparison.Ordinal);
    }

    private static string LoadText(params string[] relativePathParts)
    {
        var path = FindRepositoryFile(relativePathParts);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryFile(params string[] relativePathParts)
    {
        foreach (var root in RepositorySearchRoots())
        {
            var directory = new DirectoryInfo(root);

            while (directory is { Exists: true })
            {
                var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativePathParts).ToArray());
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Repository file not found.", Path.Combine(relativePathParts));
    }

    private static IEnumerable<string> RepositorySearchRoots(
        [System.Runtime.CompilerServices.CallerFilePath] string testFilePath = "")
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        var sourceRoot = Directory.GetParent(testFilePath)?.Parent?.Parent?.FullName;
        if (!string.IsNullOrWhiteSpace(sourceRoot))
            yield return sourceRoot;
    }
}
