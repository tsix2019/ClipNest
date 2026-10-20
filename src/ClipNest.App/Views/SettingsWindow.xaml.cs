using ClipNest.Core;
using ClipNest.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ClipNest.App.Views;

public sealed partial class SettingsWindow : Window
{
    private static readonly RetentionPeriod[] RetentionOrder =
        [RetentionPeriod.Day, RetentionPeriod.Week, RetentionPeriod.Month, RetentionPeriod.Year, RetentionPeriod.Forever];

    private readonly AppHost _host;
    private bool _loading = true;

    public SettingsWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        Title = Loc.Get("Settings_Title");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ClipNest.ico"));
        WindowStyler.SetCaptureExcluded(WindowNative.GetWindowHandle(this), host.Settings.HideFromCapture);

        var (work, scale) = DisplayHelper.GetCursorMonitor();
        var w = (int)(900 * scale);
        var h = (int)(640 * scale);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h));

        PanelHotkeyBox.GestureChanged += PanelHotkeyBox_GestureChanged;
        StackHotkeyBox.GestureChanged += StackHotkeyBox_GestureChanged;
        HotkeyBox.RecordingChanged += OnHotkeyRecording;
        host.PauseChanged += SyncPause;
        ThemeManager.Changed += RefreshIgnoredApps; // rows use code-built brushes
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) CancelHotkeyRecording();
        };
        Closed += (_, _) =>
        {
            // Recording unregisters the global hotkeys; make sure they are back whatever happens.
            CancelHotkeyRecording();
            HotkeyBox.RecordingChanged -= OnHotkeyRecording;
            ThemeManager.Changed -= RefreshIgnoredApps;
            host.PauseChanged -= SyncPause;
            _host.RegisterHotkeys();
        };

        ThemeManager.Register(this);
        LoadValues();
        _loading = false;
    }

    private void LoadValues()
    {
        var s = _host.Settings;

        LanguageBox.Items.Add(Loc.Get("Settings_LangSystem"));
        LanguageBox.Items.Add("English");
        LanguageBox.Items.Add("简体中文");
        LanguageBox.SelectedIndex = s.Language switch { "en-US" => 1, "zh-CN" => 2, _ => 0 };

        ThemeBox.Items.Add(Loc.Get("Theme_System"));
        ThemeBox.Items.Add(Loc.Get("Theme_Light"));
        ThemeBox.Items.Add(Loc.Get("Theme_Dark"));
        ThemeBox.SelectedIndex = ThemeManager.Normalize(s.Theme) switch { ThemeManager.Light => 1, ThemeManager.Dark => 2, _ => 0 };

        StartupToggle.IsOn = s.LaunchAtStartup;

        foreach (var p in RetentionOrder) RetentionBox.Items.Add(Loc.Get("Retention_" + p));
        RetentionBox.SelectedIndex = Array.IndexOf(RetentionOrder, s.Retention);

        PasteModeBox.Items.Add(Loc.Get("Settings_PasteToApp"));
        PasteModeBox.Items.Add(Loc.Get("Settings_PasteToClipboard"));
        PasteModeBox.SelectedIndex = s.PasteToActiveApp ? 0 : 1;

        PlainTextToggle.IsOn = s.AlwaysPlainText;
        LinkTitlesToggle.IsOn = s.FetchLinkTitles;
        OcrToggle.IsOn = s.OcrImages;

        HotkeyGesture.TryParse(s.PanelHotkey, out var panel);
        HotkeyGesture.TryParse(s.StackHotkey, out var stack);
        PanelHotkeyBox.Gesture = panel;
        StackHotkeyBox.Gesture = stack;
        HotkeyError.IsOpen = false;
        KeysHelp.Text = Loc.Get("Settings_KeysHelp");

        ConfidentialToggle.IsOn = s.IgnoreConfidential;
        CaptureToggle.IsOn = s.HideFromCapture;
        SyncPause();
        RefreshIgnoredApps();

        VersionText.Text = Loc.Format("Settings_Version", typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3));
        DataFolderRow.Description = _host.DataDirectory;
        StatsRow.Description = Loc.Format("Settings_StatsValue", _host.Repository.Count(), _host.Repository.GetPinboards().Count);
    }

    private void Save() => _host.SaveSettings();

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        GeneralPage.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPage.Visibility = tag == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        PrivacyPage.Visibility = tag == "Privacy" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "About" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- general ----------

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _host.Settings.Language = LanguageBox.SelectedIndex switch { 1 => "en-US", 2 => "zh-CN", _ => "system" };
        Save();
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _host.Settings.Theme = ThemeBox.SelectedIndex switch { 1 => ThemeManager.Light, 2 => ThemeManager.Dark, _ => ThemeManager.System };
        Save();
        ThemeManager.SetMode(_host.Settings.Theme);
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _host.Settings.LaunchAtStartup = StartupToggle.IsOn;
        Save();
        _host.SyncStartupRegistration();
    }

    private async void RetentionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || RetentionBox.SelectedIndex < 0) return;
        var next = RetentionOrder[RetentionBox.SelectedIndex];
        var current = _host.Settings.Retention;
        if (next == current) return;

        if (RetentionPolicy.IsShrinking(current, next))
        {
            var ok = await ConfirmAsync(Loc.Get("Settings_RetentionConfirmTitle"),
                Loc.Format("Settings_RetentionConfirmBody", Loc.Get("Retention_" + next)), Loc.Get("Common_Delete"));
            if (!ok)
            {
                _loading = true;
                RetentionBox.SelectedIndex = Array.IndexOf(RetentionOrder, current);
                _loading = false;
                return;
            }
        }

        _host.Settings.Retention = next;
        Save();
        await _host.ApplyRetentionAsync();
        _ = _host.Panel.ViewModel.ReloadAsync();
    }

    private void PasteModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _host.Settings.PasteToActiveApp = PasteModeBox.SelectedIndex == 0;
        Save();
    }

    private void PlainTextToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _host.Settings.AlwaysPlainText = PlainTextToggle.IsOn;
        Save();
    }

    private void LinkTitlesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _host.Settings.FetchLinkTitles = LinkTitlesToggle.IsOn;
        Save();
    }

    private void OcrToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _host.Settings.OcrImages = OcrToggle.IsOn;
        Save();
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        var count = _host.Repository.Count() - _host.Repository.GetPinboards().Sum(p => _host.Repository.Count(p.Id));
        var ok = await ConfirmAsync(Loc.Get("Settings_ClearConfirmTitle"), Loc.Format("Settings_ClearConfirmBody", count), Loc.Get("Common_Delete"));
        if (!ok) return;
        await Task.Run(_host.ClearHistory);
        _ = _host.Panel.ViewModel.ReloadAsync();
        StatsRow.Description = Loc.Format("Settings_StatsValue", _host.Repository.Count(), _host.Repository.GetPinboards().Count);
    }

    private async Task<bool> ConfirmAsync(string title, string body, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ((FrameworkElement)Content).ActualTheme,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = title,
            Content = body,
            PrimaryButtonText = primary,
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // ---------- shortcuts ----------

    private void OnHotkeyRecording(bool recording)
    {
        if (recording)
        {
            _host.Hotkeys.Unregister(AppHost.PanelHotkeyId);
            _host.Hotkeys.Unregister(AppHost.StackHotkeyId);
        }
        else
        {
            HotkeyError.IsOpen = _host.RegisterHotkeys().Count > 0;
        }
    }

    private void CancelHotkeyRecording()
    {
        PanelHotkeyBox.CancelRecording();
        StackHotkeyBox.CancelRecording();
    }

    private void PanelHotkeyBox_GestureChanged(object? sender, HotkeyGesture gesture) =>
        ApplyHotkey(PanelHotkeyBox, gesture, () => _host.Settings.PanelHotkey, v => _host.Settings.PanelHotkey = v);

    private void StackHotkeyBox_GestureChanged(object? sender, HotkeyGesture gesture) =>
        ApplyHotkey(StackHotkeyBox, gesture, () => _host.Settings.StackHotkey, v => _host.Settings.StackHotkey = v);

    /// <summary>Switches to <paramref name="gesture"/>; if another app owns it, keeps the previous shortcut.</summary>
    private void ApplyHotkey(HotkeyBox box, HotkeyGesture gesture, Func<string> get, Action<string> set)
    {
        var previous = get();
        set(gesture.ToString());
        var failed = _host.RegisterHotkeys().Count > 0;
        if (failed)
        {
            set(previous);
            if (HotkeyGesture.TryParse(previous, out var old)) box.Gesture = old;
            _host.RegisterHotkeys();
        }
        else
        {
            Save();
        }
        HotkeyError.IsOpen = failed;
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new Core.Settings.AppSettings();
        _host.Settings.PanelHotkey = defaults.PanelHotkey;
        _host.Settings.StackHotkey = defaults.StackHotkey;
        Save();
        HotkeyGesture.TryParse(defaults.PanelHotkey, out var panel);
        HotkeyGesture.TryParse(defaults.StackHotkey, out var stack);
        PanelHotkeyBox.Gesture = panel;
        StackHotkeyBox.Gesture = stack;
        HotkeyError.IsOpen = _host.RegisterHotkeys().Count > 0;
    }

    // ---------- privacy ----------

    private void ConfidentialToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _host.Settings.IgnoreConfidential = ConfidentialToggle.IsOn;
        Save();
    }

    private void CaptureToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _host.Settings.HideFromCapture = CaptureToggle.IsOn;
        Save();
        _host.Panel.ApplyCaptureSetting();
        WindowStyler.SetCaptureExcluded(WindowNative.GetWindowHandle(this), CaptureToggle.IsOn);
    }

    private void PauseToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || PauseToggle.IsOn == _host.IsPaused) return;
        if (PauseToggle.IsOn) _host.Pause(null);
        else _host.Resume();
    }

    private void SyncPause()
    {
        var was = _loading;
        _loading = true;
        PauseToggle.IsOn = _host.IsPaused;
        _loading = was;
    }

    private void RefreshIgnoredApps()
    {
        IgnoredList.Items.Clear();
        foreach (var path in _host.Settings.IgnoredApps.OrderBy(Path.GetFileName))
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(8, 6, 4, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var view = _host.AppView(path);
            var icon = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            if (view.IconFile is not null) icon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(view.IconFile));
            row.Children.Add(icon);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = view.Name.Length > 0 ? view.Name : Path.GetFileNameWithoutExtension(path) });
            text.Children.Add(new TextBlock
            {
                Text = path,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = ThemeManager.Brush("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var remove = new Button { Content = Loc.Get("Settings_Remove") };
            remove.Click += (_, _) =>
            {
                _host.Settings.IgnoredApps.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                Save();
                RefreshIgnoredApps();
            };
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            IgnoredList.Items.Add(row);
        }
        NoIgnoredText.Visibility = _host.Settings.IgnoredApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddIgnored(string path)
    {
        if (_host.Settings.IsIgnored(path)) return;
        _host.Settings.IgnoredApps.Add(path);
        _host.Apps.Resolve(path); // cache name + icon for the list
        Save();
        RefreshIgnoredApps();
    }

    private async void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) AddIgnored(file.Path);
    }

    private void RunningAppsMenu_Opening(object sender, object e)
    {
        RunningAppsMenu.Items.Clear();
        var paths = System.Diagnostics.Process.GetProcesses()
            .Where(p => p.MainWindowHandle != 0)
            .Select(p => ProcessInfo.ExePathOf((uint)p.Id))
            .OfType<string>()
            .Where(p => !_host.Settings.IsIgnored(p) && !p.Equals(_host.ExePath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => (Path: p, Name: AppInfoProviderName(p)))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);

        foreach (var (path, name) in paths)
        {
            var item = new MenuFlyoutItem { Text = name };
            ToolTipService.SetToolTip(item, path);
            item.Click += (_, _) => AddIgnored(path);
            RunningAppsMenu.Items.Add(item);
        }
    }

    private static string AppInfoProviderName(string path) => AppInfoProvider.DisplayName(path);

    // ---------- about ----------

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => _host.OpenDataFolder();
}
