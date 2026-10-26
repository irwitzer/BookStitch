using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Production_resume_bindings_enable_metadata_and_settings_then_disable_before_delayed_worker(bool audio)
    {
        RunOnStaThread(() =>
        {
            var document = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
            Button ButtonFor(string handler) => Assert.IsType<Button>(LoadResumePermissionElement(document,
                document.Descendants().Single(e => e.Name.LocalName == "Button" && (string?)e.Attribute("Click") == handler)));
            var metadata = ButtonFor("OpenMetadataDialog_Click");
            var settings = ButtonFor("Settings_Click");
            var projects = ButtonFor("OpenResumeProjects_Click");
            var primary = ButtonFor("Export_Click");
            var track = Assert.IsType<Button>(LoadResumePermissionElement(document,
                document.Descendants().First(e => e.Name.LocalName == "Button" && (string?)e.Attribute("IsEnabled") == "{Binding CanEditTrackOrder}")));
            var chapterMarkup = new XElement(document.Descendants().Single(e =>
                (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "MetadataDetailsContent"));
            var combo = new XElement(chapterMarkup.Descendants().Single(e =>
                (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "MainChapterModeComboBox"));
            chapterMarkup.RemoveNodes();
            chapterMarkup.Add(combo);
            var chapter = Assert.IsType<StackPanel>(LoadResumePermissionElement(document, chapterMarkup));
            var panel = new StackPanel();
            foreach (var control in new FrameworkElement[] { metadata, settings, projects, primary, track, chapter })
                panel.Children.Add(control);
            var window = new Window { Content = panel, Width = 600, Height = 420, ShowInTaskbar = false };
            var service = new ExportUiStateService();
            var preview = new DiscResumePreviewState();
            if (audio) preview.Set(new AudioDiscProjectManifestDisc { DiscNumber = 3, DiscIdentity = "fixture" });
            else preview.Set(new Mp3DiscPendingChapterDisc(3, "fixture", []));
            var input = new ExportUiStateInput(ProjectPipelineState.AcquiringSources, false, true, false,
                false, false, false, true, true, !audio, audio, false, 3, true, 0, false,
                HasPreparedResumeDiscPreview: true);
            void Present(bool metadataAvailable = true)
            {
                var state = service.Create(input with { ResumeDiscPreviewProcessingStarted = preview.ProcessingStarted });
                panel.DataContext = state with { CanChangeBookMetadata = metadataAvailable && state.CanChangeBookMetadata };
            }
            var clicks = 0;
            metadata.Click += (_, _) => clicks++;
            settings.Click += (_, _) => clicks++;
            static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button)
                .GetPattern(PatternInterface.Invoke)).Invoke();
            try
            {
                window.Show();
                input = input with { IsBusy = true };
                Present(); DrainDispatcher();
                Assert.False(metadata.IsEnabled);
                Assert.False(settings.IsEnabled);
                input = input with { IsBusy = false };
                Present(); DrainDispatcher();
                Assert.True(metadata.IsEnabled);
                Assert.True(settings.IsEnabled);
                Assert.True(track.IsEnabled);
                Assert.True(Assert.IsType<ComboBox>(chapter.Children[0]).IsEnabled);
                Assert.False(projects.IsEnabled);
                Invoke(metadata); Invoke(settings); DrainDispatcher();
                Assert.Equal(2, clicks);
                Present(metadataAvailable: false); DrainDispatcher();
                Assert.False(metadata.IsEnabled);
                Assert.True(settings.IsEnabled);
                Present(); DrainDispatcher();
                var workerObserved = false;
                primary.Click += (_, _) =>
                {
                    preview.MarkProcessingStarted();
                    var start = new PipelinePauseTransitionService().CreateContinuationStart();
                    input = input with { IsRunPaused = start.IsPipelinePaused, IsBusy = start.IsBusy };
                    Present();
                    window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        Assert.False(metadata.IsEnabled);
                        Assert.False(settings.IsEnabled);
                        Assert.False(track.IsEnabled);
                        Assert.False(Assert.IsType<ComboBox>(chapter.Children[0]).IsEnabled);
                        Assert.False(projects.IsEnabled);
                        workerObserved = true;
                    }));
                };
                Invoke(primary); DrainDispatcher();
                Assert.True(workerObserved);
                Assert.Throws<ElementNotEnabledException>(() => Invoke(metadata));
                Assert.Throws<ElementNotEnabledException>(() => Invoke(settings));
                // Old identity survives a later pause without reopening these actions.
                input = input with { IsRunPaused = new PipelinePauseTransitionService().CreateCompletedPause().IsPipelinePaused };
                Present(); DrainDispatcher();
                Assert.False(metadata.IsEnabled);
                Assert.False(settings.IsEnabled);
                Assert.False(projects.IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    private static object LoadResumePermissionElement(XDocument document, XElement source)
    {
        var element = new XElement(source);
        foreach (var child in element.DescendantsAndSelf())
        {
            child.Attribute("Click")?.Remove();
            child.Attribute("SelectionChanged")?.Remove();
            foreach (var attribute in child.Attributes().Where(a => a.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal) && !a.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal)).ToArray())
            {
                child.SetAttributeValue(XName.Get(attribute.Name.LocalName, attribute.Name.NamespaceName + ";assembly=BookStitch"), attribute.Value);
                attribute.Remove();
            }
            if (child.Name.NamespaceName.StartsWith("clr-namespace:", StringComparison.Ordinal) && !child.Name.NamespaceName.Contains(";assembly=", StringComparison.Ordinal))
                child.Name = XName.Get(child.Name.LocalName, child.Name.NamespaceName + ";assembly=BookStitch");
        }
        foreach (var ns in document.Root!.Attributes().Where(a => a.IsNamespaceDeclaration))
            element.SetAttributeValue(ns.Name, ns.Value.StartsWith("clr-namespace:", StringComparison.Ordinal) && !ns.Value.Contains(";assembly=", StringComparison.Ordinal)
                ? ns.Value + ";assembly=BookStitch" : ns.Value);
        return XamlReader.Parse(element.ToString());
    }
}
