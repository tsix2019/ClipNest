using ClipNest.Platform;
using Microsoft.UI.Xaml;

namespace ClipNest.App;

public partial class App : Application
{
    private readonly string[] _args;
    private AppHost? _host;

    public App(string[] args)
    {
        _args = args;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled", e.Exception);
            e.Handled = true; // a background app should survive a bad clipboard item
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("UnobservedTask", e.Exception);
            e.SetObserved();
        };
    }

    public static AppHost Host => ((App)Current)._host!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host = new AppHost(autostart: _args.Contains(StartupRegistration.AutostartArgument));
        _host.Start();
    }
}
