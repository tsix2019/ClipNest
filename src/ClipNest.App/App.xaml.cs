using ClipNest.Platform;
using Microsoft.UI.Xaml;

namespace ClipNest.App;

public partial class App : Application
{
    private AppHost? _host;

    public App()
    {
        InitializeComponent();
        // Match the application theme to an explicit choice: some control states (the NavigationView
        // selection in Settings) resolve against the application theme instead of the window's, and a
        // light application theme under a dark window shows a white, unreadable selected item.
        var settings = AppHost.PeekSettings();
        AppFonts.Initialize(settings.FontFamily);
        switch (ThemeManager.Normalize(settings.Theme))
        {
            case ThemeManager.Dark:
                RequestedTheme = ApplicationTheme.Dark;
                break;
            case ThemeManager.Light:
                RequestedTheme = ApplicationTheme.Light;
                break;
        }
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
        _host = new AppHost(autostart: Environment.GetCommandLineArgs().Contains(StartupRegistration.AutostartArgument));
        _host.Start();
    }
}
