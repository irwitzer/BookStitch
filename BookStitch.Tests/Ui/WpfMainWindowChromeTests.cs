using BookStitch.Controls;
using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Xml.Linq;
using System.Windows.Markup;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using Xunit;

namespace BookStitch.Tests.Ui;

// Windows/WPF: caption binding, HWND state and work-area geometry.
public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData("BookStitch", BookStitchEnvironment.Production)]
    [InlineData("BookStitch [Development]", BookStitchEnvironment.Development)]
    [InlineData("BookStitch [Tester]", BookStitchEnvironment.XTester)]
    public void Main_chrome_uses_system_caption_height_and_preserves_window_actions(string title, BookStitchEnvironment environment)
    {
        RunOnStaThread(() =>
        {
            DeveloperToolsCommands.AccessState.Initialize(new AppSettings(), environment, () => true);
            // Instantiate the production caption grid, including its developer command above the folder action.
            var source = XDocument.Parse(RepositorySource.Read(["BookStitch", "MainWindow.xaml"]));
            Assert.Equal("{StaticResource MainWindowStyle}", source.Root!.Attribute("Style")!.Value);
            var caption = new XElement(source.Descendants().Single(e => e.Name.LocalName == "LargeResizableWindowHeader").Parent!);
            caption.Descendants().Single(e => e.Name.LocalName == "LargeResizableWindowHeader").Attribute("CloseRequested")!.Remove();
            foreach (var element in caption.DescendantsAndSelf())
            {
                if (element.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal) && !element.Name.NamespaceName.Contains(";assembly="))
                    element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=BookStitch");
                foreach (var attribute in element.Attributes().Where(attribute =>
                    !attribute.IsNamespaceDeclaration && attribute.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal) && !attribute.Name.NamespaceName.Contains(";assembly=")).ToArray())
                {
                    element.SetAttributeValue(XName.Get(attribute.Name.LocalName,
                        attribute.Name.NamespaceName + ";assembly=BookStitch"), attribute.Value);
                    attribute.Remove();
                }
            }
            foreach (var ns in source.Root.Attributes().Where(a => a.IsNamespaceDeclaration))
                caption.SetAttributeValue(ns.Name, ns.Value.StartsWith("clr-namespace:", StringComparison.Ordinal) && !ns.Value.Contains(";assembly=")
                    ? ns.Value + ";assembly=BookStitch" : ns.Value);
            var captionGrid = (Grid)XamlReader.Parse(caption.ToString());
            var header = captionGrid.Children.OfType<LargeResizableWindowHeader>().Single();
            var window = new Window
            {
                Title = title, Width = 1250, Height = 860,
                Style = (Style)System.Windows.Application.Current.FindResource("MainWindowStyle"),
                Content = new DockPanel()
            };
            DockPanel.SetDock(captionGrid, Dock.Top);
            ((DockPanel)window.Content).Children.Add(captionGrid);
            ((DockPanel)window.Content).Children.Add(new Border());
            var closed = false;
            window.Closed += (_, _) => closed = true;
            header.CloseRequested += (_, _) => window.Close();
            try
            {
                window.Show();
                DrainDispatcher();
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.Null(FindVisualDescendant<TextBlock>(captionGrid, block => block.Text == title));
                Assert.Equal(LargeResizableWindowHeader.SystemCaptionHeight, header.ActualHeight);
                var wrench = FindVisualDescendant<Button>(captionGrid, button => button.Command == DeveloperToolsCommands.OpenDeveloperToolsWindow)!;
                Assert.NotNull(wrench);
                if (wrench.Visibility == Visibility.Visible)
                {
                    // The caption action must remain reachable after branding/layout changes.
                    Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(wrench));
                    var left = wrench.TransformToAncestor(captionGrid).Transform(new Point(0, 0)).X;
                    Assert.InRange(left, 0, captionGrid.ActualWidth - wrench.ActualWidth);
                }
                Assert.Equal(new Thickness(0), header.BorderThickness);

                Assert.Equal(environment == BookStitchEnvironment.Production ? Visibility.Collapsed : Visibility.Visible, wrench.Visibility);
                if (wrench.Visibility == Visibility.Visible)
                {
                    Assert.True(wrench.ActualHeight <= header.ActualHeight);
                    Assert.True(wrench.TransformToAncestor(captionGrid).Transform(new Point(wrench.ActualWidth, wrench.ActualHeight)).Y <= header.ActualHeight);
                }
                SaveLayoutRepairPreview(window, title.Replace(" ", "-") + "-caption-new");
                Assert.Equal(LargeResizableWindowHeader.SystemCaptionHeight, WindowChrome.GetWindowChrome(window).CaptionHeight);
                var minimize = (Button)header.Template.FindName("PART_MinimizeButton", header);
                var maximize = (Button)header.Template.FindName("PART_MaximizeRestoreButton", header);
                var close = (Button)header.Template.FindName("PART_CloseButton", header);
                Assert.Equal(Visibility.Visible, minimize.Visibility);
                // Native minimize/maximize changes desktop foreground ownership. Run these actions
                // in an isolated smoke process so modal keyboard tests keep their focus environment.
                if (Environment.GetEnvironmentVariable("BOOKSTITCH_RUN_WINDOW_STATE_SMOKE_TEST") == "1")
                {
                    minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    DrainDispatcher();
                    Assert.Equal(WindowState.Minimized, window.WindowState);
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                    DrainDispatcher();
                    maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    DrainDispatcher();
                    Assert.Equal(WindowState.Maximized, window.WindowState);
                    var captionTop = header.PointToScreen(new Point(0, 0));
                    var scale = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformToDevice;
                    Assert.InRange(captionTop.Y - SystemParameters.WorkArea.Top * scale.M22, -1, 2);
                    maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    DrainDispatcher();
                    Assert.Equal(WindowState.Normal, window.WindowState);
                }
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(closed);
            }
            finally
            {
                if (!closed) window.Close();
                DeveloperToolsCommands.AccessState.Initialize(new AppSettings(), BookStitchEnvironment.Production, () => false);
            }
        });
    }

}
