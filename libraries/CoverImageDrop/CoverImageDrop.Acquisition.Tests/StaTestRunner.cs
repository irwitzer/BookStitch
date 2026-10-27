using System.Runtime.ExceptionServices;

namespace CoverImageDrop.Acquisition.Tests;

internal static class StaTestRunner
{
    public static void Run(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    public static void Run(Func<Task> action)
    {
        Run(() => action().GetAwaiter().GetResult());
    }
}
