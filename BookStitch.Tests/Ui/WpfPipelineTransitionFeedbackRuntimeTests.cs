using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed partial class WpfDialogRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pending_primary_and_secondary_bind_visible_disabled_before_background_work(bool stopping)
    {
        RunOnStaThread(() =>
        {
            var targetBinding = stopping ? "{Binding SecondaryButtonText}" : "{Binding ExportButtonText}";
            var document = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
            var element = document.Descendants().Single(e => e.Name.LocalName == "Button" &&
                (string?)e.Attribute("Content") == targetBinding);
            // Isolate the real bindings from MainWindow's event handlers and application styles.
            element.Attribute("Click")!.Remove();
            element.Attribute("Style")!.Remove();
            var button = Assert.IsType<Button>(XamlReader.Parse(element.ToString()));
            var window = new Window { Content = button, Width = 400, Height = 120 };
            try
            {
                window.Show();
                var input = new ExportUiStateInput(ProjectPipelineState.Converting, true, false, false,
                    false, false, true, true, false, false, false, true, 20, true, 0, false,
                    UiTransition: new(stopping ? PipelineUiTransitionKind.Stopping : PipelineUiTransitionKind.Pausing,
                        stopping ? PipelineUiTransitionTarget.Secondary : PipelineUiTransitionTarget.Primary));
                button.DataContext = new ExportUiStateService().Create(input);
                var frame = new DispatcherFrame();
                var renderObserved = false;
                window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    Assert.True(renderObserved, "Cancellation/cleanup continuation ran before the pending controls rendered.");
                    frame.Continue = false;
                }));
                window.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    try
                    {
                        Assert.Equal(Visibility.Visible, button.Visibility);
                        Assert.False(button.IsEnabled);
                        Assert.Equal(input.UiTransition!.ButtonText, button.Content);
                        Assert.Equal(input.UiTransition.ButtonText, System.Windows.Automation.AutomationProperties.GetName(button));
                        renderObserved = true;
                    }
                    catch { frame.Continue = false; throw; }
                }));
                Dispatcher.PushFrame(frame);
                Assert.True(renderObserved);
                button.DataContext = new ExportUiStateService().Create(input with
                    { IsBusy = false, IsRunPaused = true, UiTransition = null, IsPresetReconversionRun = stopping });
                DrainDispatcher();
                Assert.True(button.IsEnabled);
                Assert.Equal(LocalizationText.Get(stopping ? "MainWindow.Action.Discard" : "MainWindow.Action.Continue"), button.Content);
            }
            finally { window.Close(); }
        });
    }
}
