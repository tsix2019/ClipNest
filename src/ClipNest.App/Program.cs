using ClipNest.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ClipNest.App;

public static class Program
{
    private static SingleInstance? _instance;

    /// <summary>"--restart-after &lt;pid&gt;": the previous instance is still shutting down; wait for it.</summary>
    private static void WaitForRestartedInstance()
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, "--restart-after");
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid)) return;
        try
        {
            using var old = System.Diagnostics.Process.GetProcessById(pid);
            old.WaitForExit(10_000);
        }
        catch (ArgumentException)
        {
            // already gone
        }
    }

    [STAThread]
    private static int Main()
    {
        WaitForRestartedInstance();
        _instance = SingleInstance.TryAcquire("ClipNest.SingleInstance");
        if (_instance is null)
        {
            // Already running: ask it to show the panel and leave.
            SingleInstance.SignalExisting(toggle: Environment.GetCommandLineArgs().Contains("--toggle"));
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });

        _instance.Dispose();
        return 0;
    }
}
