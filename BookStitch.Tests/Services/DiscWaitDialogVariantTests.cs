using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DiscWaitDialogVariantTests
{
    [Fact]
    public void Application_windows_use_rounded_app_chrome_and_no_native_message_boxes()
    {
        var root = Path.GetDirectoryName(RepositorySource.Find(["BookStitch", "App.xaml"]))!;
        var windows = Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(path => (Path: path, Xml: System.Xml.Linq.XDocument.Load(path)))
            .Where(item => item.Xml.Root?.Name.LocalName == "Window").ToList();
        Assert.True(windows.Count >= 10);
        foreach (var (path, xml) in windows)
        {
            var node = xml.Root!;
            var style = node.Attribute("Style")?.Value;
            if (style is "{StaticResource LargeResizableWindowStyle}" or "{StaticResource MainWindowStyle}") continue;
            Assert.True(node.Attribute("WindowStyle")?.Value == "None", path);
            Assert.True(node.Descendants().Any(e => e.Name.LocalName == "WindowChrome"
                && e.Attribute("CornerRadius")?.Value == "8"), path);
        }
        var styles = RepositorySource.Read(["BookStitch", "Themes", "WindowChromeStyles.xaml"]);
        Assert.Contains("Property=\"WindowStyle\" Value=\"None\"", styles);
        Assert.Contains("CornerRadius=\"8\"", styles);
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
            Assert.DoesNotContain("MessageBox.Show(", File.ReadAllText(path));
    }
    [Fact]
    public void DebugSettings_DefaultsToLegacyDialog()
    {
        var settings = new DebugSettings();
        Assert.False(settings.UseBoxedDiscWaitDialog);
    }

    [Fact]
    public void BothDialogImplementationsRemainConstructible()
    {
        Assert.IsAssignableFrom<IDiscWaitDialogService>(new DiscWaitDialogService());
        Assert.IsAssignableFrom<IDiscWaitDialogService>(new BoxedDiscWaitDialogService());
    }

    [Fact]
    public void Both_entry_points_use_boxed_completion_and_duplicate_override_handling()
    {
        var original = RepositorySource.Read(["BookStitch", "Services", "DiscWaitDialogService.cs"]);
        Assert.Contains("BoxedDiscWaitDialogService _dialog = new()", original);
        Assert.Contains("_dialog.WaitForDiscAsync(owner, request, checkDiscAsync, setStatusText, setProgressText, token)", original);
        Assert.DoesNotContain("new Window", original);
        var source = RepositorySource.Read(["BookStitch", "Services", "BoxedDiscWaitDialogService.cs"]);

        Assert.Contains("DiscWaitDialogOutcome.ImportCompleted", source, StringComparison.Ordinal);
        Assert.Contains("DiscWaitDialogOutcome.ReadyWithDuplicateOverride", source, StringComparison.Ordinal);
        Assert.Contains("AcceptNextDuplicate", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DiscPollingDisplayState.Unsupported)]
    [InlineData(DiscPollingDisplayState.Duplicate)]
    public void ActionableNotice_RemainsVisibleWhileDriveReturnsToWaiting(DiscPollingDisplayState displayedState)
    {
        Assert.True(DiscPollingDisplayStateRules.ShouldKeepNoticeVisible(
            displayedState,
            DiscPollingDisplayState.Waiting));
    }

    [Theory]
    [InlineData(DiscPollingDisplayState.Unsupported, DiscPollingDisplayState.Ready)]
    [InlineData(DiscPollingDisplayState.Unsupported, DiscPollingDisplayState.Duplicate)]
    [InlineData(DiscPollingDisplayState.Duplicate, DiscPollingDisplayState.Unsupported)]
    [InlineData(DiscPollingDisplayState.Waiting, DiscPollingDisplayState.Waiting)]
    public void Notice_IsReplacedForNewRelevantState(
        DiscPollingDisplayState displayedState,
        DiscPollingDisplayState nextState)
    {
        Assert.False(DiscPollingDisplayStateRules.ShouldKeepNoticeVisible(displayedState, nextState));
    }
}
