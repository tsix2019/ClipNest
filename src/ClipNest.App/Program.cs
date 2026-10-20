using ClipNest.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ClipNest.App;

public static class Program
{
    private static SingleInstance? _instance;

    [STAThread]
    private static int Main(string[] args)
    {
        _instance = SingleInstance.TryAcquire("ClipNest.SingleInstance");
        if (_instance is null)
        {
            // Already running: ask it to show the panel and leave.
            SingleInstance.SignalExisting();
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(args);
        });

        _instance.Dispose();
        return 0;
    }
}
