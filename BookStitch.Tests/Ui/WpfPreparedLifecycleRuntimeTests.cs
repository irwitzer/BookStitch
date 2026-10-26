using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using System.Windows.Threading;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class WpfPreparedLifecycleRuntimeTests
{
    [Fact]
    public async Task Handled_child_failure_followed_by_disposal_does_not_reach_dispatcher_unhandled_exception()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var folder = new TemporaryFolder();
                var dispatcher = Dispatcher.CurrentDispatcher;
                var frame = new DispatcherFrame();
                dispatcher.UnhandledException += (_, args) =>
                {
                    args.Handled = true;
                    completed.TrySetException(args.Exception);
                    frame.Continue = false;
                };
                dispatcher.BeginInvoke(new Action(async () =>
                {
                    var session = new PreparedTrackLifecycleSession(new WorkManifestService(),
                        new RecordingAudioExportProcessingService(), new ExportWorkManifest(),
                        ProjectFolderLayout.GetWorkManifestPath(folder.Path), new object(), "probe", 4);
                    var tasks = (List<Task>)typeof(PreparedTrackLifecycleSession).GetField("_tasks",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;
                    tasks.Add(Task.FromException(new IOException("handled lifecycle failure")));
                    await Assert.ThrowsAsync<IOException>(() => session.DrainAsync());
                    await session.DisposeAsync();
                    await session.DisposeAsync();
                    completed.TrySetResult();
                    frame.Continue = false;
                }));
                Dispatcher.PushFrame(frame);
                dispatcher.InvokeShutdown();
            }
            catch (Exception error) { completed.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }
}
