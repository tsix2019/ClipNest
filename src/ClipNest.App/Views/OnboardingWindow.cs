using ClipNest.Core;
using ClipNest.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace ClipNest.App.Views;

/// <summary>First-run welcome: shows the shortcut and asks for retention and launch-at-startup.</summary>
public sealed partial class OnboardingWindow : Window
{
    private static readonly RetentionPeriod[] RetentionOrder =
        [RetentionPeriod.Day, RetentionPeriod.Week, RetentionPeriod.Month, RetentionPeriod.Year, RetentionPeriod.Forever];

    public OnboardingWindow(AppHost host)
    {
        Title = "ClipNest";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ClipNest.ico"));

        var logo = new Image { Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.png"))), Width = 72, Height = 72 };
        var title = new TextBlock { Text = Loc.Get("Onboarding_Title"), Style = (Style)Application.Current.Resources["TitleTextBlockStyle"], HorizontalAlignment = HorizontalAlignment.Center };
        var subtitle = new TextBlock
        {
            Text = Loc.Get("Onboarding_Subtitle"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = ThemeManager.Brush("TextFillColorSecondaryBrush"),
        };

        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var part in host.Settings.PanelHotkey.Split('+'))
        {
            keys.Children.Add(new Border
            {
                Padding = new Thickness(14, 8, 14, 8),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1, 1, 1, 3),
                BorderBrush = ThemeManager.Brush("ControlStrongStrokeColorDefaultBrush"),
                Background = ThemeManager.Brush("ControlFillColorDefaultBrush"),
                Child = new TextBlock { Text = part, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
            });
        }
        var hotkeyHint = new TextBlock
        {
            Text = Loc.Get("Onboarding_Hotkey"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeManager.Brush("TextFillColorSecondaryBrush"),
        };

        var retention = new ComboBox { MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var p in RetentionOrder) retention.Items.Add(Loc.Get("Retention_" + p));
        retention.SelectedIndex = Array.IndexOf(RetentionOrder, host.Settings.Retention);

        var startup = new ToggleSwitch { IsOn = host.Settings.LaunchAtStartup, OnContent = "", OffContent = "", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Right };

        var options = new Grid { RowSpacing = 10, ColumnSpacing = 16, Margin = new Thickness(0, 8, 0, 0) };
        options.ColumnDefinitions.Add(new ColumnDefinition());
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        options.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        options.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddRow(options, 0, Loc.Get("Settings_Retention"), retention);
        AddRow(options, 1, Loc.Get("Settings_Startup"), startup);

        var start = new Button
        {
            Content = Loc.Get("Onboarding_Start"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 10, 0, 10),
            Margin = new Thickness(0, 8, 0, 0),
        };
        start.Click += (_, _) =>
        {
            host.Settings.Retention = RetentionOrder[Math.Max(0, retention.SelectedIndex)];
            host.Settings.LaunchAtStartup = startup.IsOn;
            host.Settings.OnboardingDone = true;
            host.SaveSettings();
            host.SyncStartupRegistration();
            Close();
        };
        Closed += (_, _) =>
        {
            if (host.Settings.OnboardingDone) return;
            host.Settings.OnboardingDone = true; // closed with the X: keep defaults, don't ask again
            host.SaveSettings();
        };

        var root = new StackPanel { Spacing = 14, Padding = new Thickness(36, 40, 36, 28), VerticalAlignment = VerticalAlignment.Center };
        root.Children.Add(logo);
        root.Children.Add(title);
        root.Children.Add(subtitle);
        root.Children.Add(keys);
        root.Children.Add(hotkeyHint);
        root.Children.Add(options);
        root.Children.Add(start);
        Content = root;
        ThemeManager.Register(this);

        var (work, scale) = DisplayHelper.GetCursorMonitor();
        var w = (int)(520 * scale);
        var h = (int)(600 * scale);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));
    }

    private static void AddRow(Grid grid, int row, string label, FrameworkElement control)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(text, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
    }
}
