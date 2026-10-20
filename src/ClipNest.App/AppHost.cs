using ClipNest.App.Services;
using ClipNest.App.Views;
using ClipNest.Core;
using ClipNest.Core.Settings;
using ClipNest.Data;
using ClipNest.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ClipNest.App;

/// <summary>Display data for a source app (falls back to a neutral header when unknown).</summary>
public sealed record AppInfoView(string Name, uint Color, string? IconFile);

/// <summary>Composition root: owns every service and coordinates capture, paste, tray, hotkeys and windows.</summary>
public sealed class AppHost : IDisposable
{
    public const int PanelHotkeyId = 1;
    public const int StackHotkeyId = 2;

    private static readonly AppInfoView UnknownApp = new("", ColorMath.NeutralHeader, null);

    private readonly bool _autostart;
    private readonly SemaphoreSlim _ingestGate = new(1, 1);
    private readonly List<ClipItem> _undo = []; // newest last
    private readonly Dictionary<string, AppInfoView> _appViews = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherQueueTimer? _maintenanceTimer;
    private DispatcherQueueTimer? _resumeTimer;
    private nint _trayIcon;
    private SettingsWindow? _settingsWindow;

    public AppHost(bool autostart)
    {
        _autostart = autostart;
        // CLIPNEST_DATA lets tests and portable setups use another folder.
        DataDirectory = Environment.GetEnvironmentVariable("CLIPNEST_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipNest");
    }

    public string DataDirectory { get; }
    public DispatcherQueue UI { get; private set; } = null!;
    public SettingsStore SettingsStore { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;
    public ClipRepository Repository { get; private set; } = null!;
    public BlobStore Blobs { get; private set; } = null!;
    public ImageProcessor Images { get; private set; } = null!;
    public AppInfoProvider Apps { get; private set; } = null!;
    public ClipIngestor Ingestor { get; private set; } = null!;
    public MessageWindow Messages { get; private set; } = null!;
    public ClipboardMonitor Monitor { get; private set; } = null!;
    public ClipboardReader Reader { get; private set; } = null!;
    public ClipboardWriter Writer { get; private set; } = null!;
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public PasteInjector Injector { get; private set; } = null!;
    public TrayIcon Tray { get; private set; } = null!;
    public PanelWindow Panel { get; private set; } = null!;
    public Enricher Enricher { get; private set; } = null!;
    public PasteStack Stack { get; private set; } = null!;

    public bool IsPaused => Monitor.Paused;
    public DateTimeOffset? PausedUntil { get; private set; }

    /// <summary>A copy was recorded. Raised on the UI thread.</summary>
    public event Action<IngestResult>? Ingested;

    /// <summary>An item changed (rename, pin, OCR…) or was restored. Raised on the UI thread.</summary>
    public event Action<ClipItem>? ItemChanged;

    public event Action? PauseChanged;

    public string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ClipNest.exe");

    // ---------- startup ----------

    public void Start()
    {
        UI = DispatcherQueue.GetForCurrentThread();
        Log.Initialize(Path.Combine(DataDirectory, "logs"));
        Log.Info($"Starting ClipNest {typeof(AppHost).Assembly.GetName().Version} (sqlite {Data.Sqlite.SqliteConnection.LibVersion})");

        SettingsStore = new SettingsStore(Path.Combine(DataDirectory, "settings.json"));
        Settings = SettingsStore.Load();
        Loc.Initialize(Settings.Language);
        ThemeManager.Initialize(Settings.Theme);

        Repository = ClipRepository.Open(Path.Combine(DataDirectory, "clipnest.db"), out var recovered);
        Blobs = new BlobStore(DataDirectory);
        Images = new ImageProcessor();
        Apps = new AppInfoProvider(Path.Combine(DataDirectory, "icons"), Repository, Images);
        Ingestor = new ClipIngestor(Repository, Blobs, Images, Apps.Resolve);

        Messages = new MessageWindow();
        Messages.AddHandler(OnMessage);
        Reader = new ClipboardReader(Images, () => Settings.IgnoreConfidential);
        Writer = new ClipboardWriter(Messages, Images);
        Monitor = new ClipboardMonitor(Messages);
        Monitor.Changed += OnClipboardChanged;
        Hotkeys = new HotkeyManager(Messages);
        Hotkeys.Pressed += OnHotkey;
        Injector = new PasteInjector();

        _trayIcon = NativeIcon.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "ClipNest.ico"), NativeIcon.SmallIconSize());
        Tray = new TrayIcon(Messages, _trayIcon, TrayTooltip()) { MenuFactory = BuildTrayMenu };
        Tray.Clicked += TogglePanel;

        Panel = new PanelWindow(this);
        Enricher = new Enricher(this);
        Stack = new PasteStack(this);

        var failed = RegisterHotkeys();
        if (failed.Count > 0)
            Tray.ShowNotification(Loc.Get("Notify_HotkeyFailedTitle"), Loc.Format("Notify_HotkeyFailed", string.Join(", ", failed)));
        else if (!Settings.OnboardingDone && !_autostart && Environment.GetEnvironmentVariable("CLIPNEST_SNAPSHOT") is null)
            new OnboardingWindow(this).Activate();
        else if (!_autostart)
            Tray.ShowNotification("ClipNest", Loc.Format("Notify_Running", Settings.PanelHotkey));

        if (recovered)
            Tray.ShowNotification("ClipNest", Loc.Get("Notify_DbRecovered"));

        SyncStartupRegistration();

        // Retention + orphan cleanup now and hourly, off the UI thread.
        _ = Task.Run(() => RunMaintenance(sweepOrphans: true));
        _maintenanceTimer = UI.CreateTimer();
        _maintenanceTimer.Interval = TimeSpan.FromHours(1);
        _maintenanceTimer.Tick += (_, _) => Task.Run(() => RunMaintenance(sweepOrphans: false));
        _maintenanceTimer.Start();

        // OCR older images once things have settled.
        var backfill = UI.CreateTimer();
        backfill.Interval = TimeSpan.FromSeconds(20);
        backfill.IsRepeating = false;
        backfill.Tick += (_, _) => Enricher.Backfill();
        backfill.Start();

        // Startup touched a lot of code once; give those pages back while we sit in the tray.
        var trim = UI.CreateTimer();
        trim.Interval = TimeSpan.FromSeconds(25);
        trim.IsRepeating = false;
        trim.Tick += (_, _) =>
        {
            if (!Panel.IsShown) MemoryTrimmer.Trim();
        };
        trim.Start();

        if (Environment.GetEnvironmentVariable("CLIPNEST_SNAPSHOT") is { Length: > 0 } snapshotDir)
            DevTools.RunSnapshot(this, snapshotDir);
    }

    /// <summary>Registers hotkeys from settings; returns the gestures that could not be registered.</summary>
    public List<string> RegisterHotkeys()
    {
        var failed = new List<string>();
        if (!HotkeyGesture.TryParse(Settings.PanelHotkey, out var panel) || !Hotkeys.Register(PanelHotkeyId, panel))
            failed.Add(Settings.PanelHotkey);
        if (HotkeyGesture.TryParse(Settings.StackHotkey, out var stack) && !Hotkeys.Register(StackHotkeyId, stack))
            failed.Add(Settings.StackHotkey);
        return failed;
    }

    public void SyncStartupRegistration()
    {
        try
        {
            if (StartupRegistration.IsEnabled() != Settings.LaunchAtStartup)
                StartupRegistration.SetEnabled(Settings.LaunchAtStartup, ExePath);
        }
        catch (Exception e)
        {
            Log.Error("Startup registration", e);
        }
    }

    public void SaveSettings() => SettingsStore.Save(Settings);

    private bool OnMessage(uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg != SingleInstance.ActivateMessage) return false;
        if (wParam == 1) TogglePanel();
        else ShowPanel();
        return true;
    }

    private void OnHotkey(int id)
    {
        if (id == PanelHotkeyId) TogglePanel();
        else if (id == StackHotkeyId) Stack.Toggle();
    }

    // ---------- capture ----------

    private void OnClipboardChanged()
    {
        if (IsPaused) return;
        _ = Task.Run(async () =>
        {
            await _ingestGate.WaitAsync();
            try
            {
                var snapshot = Reader.Read();
                if (snapshot is null || Settings.IsIgnored(snapshot.SourceAppPath)) return;
                var result = Ingestor.Ingest(snapshot, DateTimeOffset.Now);
                if (result is null) return;
                if (result.IsNew) Enricher.Enqueue(result.Item);
                UI.TryEnqueue(() => Ingested?.Invoke(result));
            }
            catch (Exception e)
            {
                Log.Error("Ingest", e);
            }
            finally
            {
                _ingestGate.Release();
            }
        });
    }

    public AppInfoView AppView(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return UnknownApp;
        if (_appViews.TryGetValue(exePath, out var view)) return view;
        var info = Repository.GetApp(exePath);
        view = info is null ? UnknownApp with { Name = Path.GetFileNameWithoutExtension(exePath) } : new AppInfoView(info.Name, info.Color, info.IconFile);
        if (info is not null) _appViews[exePath] = view;
        return view;
    }

    public string? ThumbnailPath(ClipItem item)
    {
        if (item.Blobs.HasFlag(BlobFlags.Thumb)) return Blobs.PathFor(item.ContentHash, BlobFlags.Thumb);
        if (item.Blobs.HasFlag(BlobFlags.Png)) return Blobs.PathFor(item.ContentHash, BlobFlags.Png);
        return null;
    }

    // ---------- paste / copy ----------

    public ClipboardPayload BuildPayload(IReadOnlyList<ClipItem> items)
    {
        if (items.Count == 1)
        {
            var item = items[0];
            return item.Kind switch
            {
                ClipKind.Image => new ClipboardPayload { Png = Blobs.Read(item.ContentHash, BlobFlags.Png) },
                ClipKind.File => new ClipboardPayload { Files = item.FilePaths, Text = string.Join("\r\n", item.FilePaths) },
                ClipKind.RichText => new ClipboardPayload
                {
                    Text = item.Text,
                    Rtf = item.Blobs.HasFlag(BlobFlags.Rtf) ? Blobs.ReadText(item.ContentHash, BlobFlags.Rtf) : null,
                    Html = item.Blobs.HasFlag(BlobFlags.Html) ? Blobs.ReadText(item.ContentHash, BlobFlags.Html) : null,
                },
                _ => new ClipboardPayload { Text = item.Text },
            };
        }

        // Several items: files merge into one file list, everything else into newline-joined text.
        if (items.All(i => i.Kind == ClipKind.File))
        {
            var files = items.SelectMany(i => i.FilePaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new ClipboardPayload { Files = files, Text = string.Join("\r\n", files) };
        }
        var texts = items.Where(i => i.Text is not null && i.Kind != ClipKind.Image).Select(i => i.Text!);
        return new ClipboardPayload { Text = string.Join("\r\n", texts) };
    }

    /// <summary>Puts items on the clipboard and (by default) pastes them into the previously active app.</summary>
    public async Task PasteAsync(IReadOnlyList<ClipItem> items, bool plainText, bool pasteIntoApp = true)
    {
        if (items.Count == 0) return;
        var payload = await Task.Run(() => BuildPayload(items));
        var plain = plainText || Settings.AlwaysPlainText;
        var written = await Task.Run(() => Writer.Write(payload, plain));
        Panel.HideNow();
        if (!written) return;

        TouchAsync(items[0]);

        if (!pasteIntoApp || !Settings.PasteToActiveApp) return;
        var outcome = await Injector.PasteAsync();
        switch (outcome)
        {
            case PasteOutcome.Blocked:
                Tray.ShowNotification(Loc.Get("Notify_CopiedTitle"), Loc.Get("Notify_PasteBlocked"));
                break;
            case PasteOutcome.FocusFailed:
                Tray.ShowNotification(Loc.Get("Notify_CopiedTitle"), Loc.Get("Notify_PasteFailed"));
                break;
        }
    }

    /// <summary>Moves a used item to the front of history.</summary>
    private void TouchAsync(ClipItem item)
    {
        var now = DateTimeOffset.Now;
        item.CreatedAt = now;
        Repository.Touch(item.Id, now, null, null);
        ItemChanged?.Invoke(item);
    }

    /// <summary>Announces an item update made off the UI thread (OCR, link title).</summary>
    public void NotifyItemChanged(ClipItem item) => UI.TryEnqueue(() => ItemChanged?.Invoke(item));

    // ---------- edit operations ----------

    public void Delete(IEnumerable<ClipItem> items)
    {
        foreach (var item in items)
        {
            Repository.Delete(item.Id);
            _undo.Add(item);
        }
        if (_undo.Count > 50) _undo.RemoveRange(0, _undo.Count - 50);
    }

    public ClipItem? Undo()
    {
        if (_undo.Count == 0) return null;
        var item = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var restored = Exclusive(() =>
        {
            if (Repository.FindByHash(item.ContentHash) is not null) return false; // copied again meanwhile
            Repository.Restore(item);
            return true;
        });
        if (!restored) return null;
        ItemChanged?.Invoke(item);
        return item;
    }

    public void Rename(ClipItem item, string? title)
    {
        Repository.Rename(item.Id, title);
        item.Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        ItemChanged?.Invoke(item);
    }

    public void UpdateText(ClipItem item, ClipKind kind, string text)
    {
        // Same normalization as ClipIngestor, so re-copying the edited content dedupes against it.
        text = kind switch
        {
            ClipKind.Color when ClipClassifier.TryParseColor(text, out var hex) => hex,
            ClipKind.Link => text.Trim(),
            _ => ContentHasher.NormalizeText(text),
        };
        var hash = ContentHasher.ForText(kind, text);
        Exclusive(() =>
        {
            Repository.UpdateText(item.Id, kind, text, hash, text.Length);
            return true;
        });
        item.Kind = kind;
        item.Text = text;
        item.ContentHash = hash;
        item.CharCount = text.Length;
        item.Blobs &= ~(BlobFlags.Rtf | BlobFlags.Html);
        ItemChanged?.Invoke(item);
        UI.TryEnqueue(() => _ = Panel.ViewModel.ReloadAsync()); // kind/template may have changed
    }

    public void SetPinboard(IEnumerable<ClipItem> items, long? pinboardId)
    {
        foreach (var item in items)
        {
            Repository.SetPinboard(item.Id, pinboardId);
            item.PinboardId = pinboardId;
            ItemChanged?.Invoke(item);
        }
    }

    /// <summary>Deletes all unpinned items. Blocks while a capture is in progress; call off the UI thread.</summary>
    public void ClearHistory()
    {
        Exclusive(() =>
        {
            DeleteBlobs(Repository.ClearHistory());
            return true;
        });
        UI.TryEnqueue(_undo.Clear);
    }

    /// <summary>Deletes a pinboard with its items. Call off the UI thread.</summary>
    public void DeletePinboard(long id) => Exclusive(() =>
    {
        DeleteBlobs(Repository.DeletePinboard(id));
        return true;
    });

    /// <summary>Runs <paramref name="body"/> while no capture is in progress (captures write blobs before rows).</summary>
    private T Exclusive<T>(Func<T> body)
    {
        _ingestGate.Wait();
        try
        {
            return body();
        }
        finally
        {
            _ingestGate.Release();
        }
    }

    /// <summary>Blob files are shared by content hash: only delete those no remaining item uses.</summary>
    private void DeleteBlobs(IEnumerable<string> hashes)
    {
        foreach (var hash in hashes)
        {
            if (!Repository.IsHashReferenced(hash)) Blobs.DeleteAll(hash);
        }
    }

    // ---------- pause ----------

    public void Pause(TimeSpan? duration)
    {
        Monitor.Paused = true;
        PausedUntil = duration is { } d ? DateTimeOffset.Now + d : null;
        _resumeTimer?.Stop();
        if (duration is { } span)
        {
            _resumeTimer = UI.CreateTimer();
            _resumeTimer.Interval = span;
            _resumeTimer.IsRepeating = false;
            _resumeTimer.Tick += (_, _) => Resume();
            _resumeTimer.Start();
        }
        Tray.SetTooltip(TrayTooltip());
        PauseChanged?.Invoke();
    }

    public void Resume()
    {
        _resumeTimer?.Stop();
        Monitor.Paused = false;
        PausedUntil = null;
        Tray.SetTooltip(TrayTooltip());
        PauseChanged?.Invoke();
    }

    private string TrayTooltip() =>
        IsPaused ? Loc.Get("Tray_TooltipPaused") : Loc.Format("Tray_Tooltip", Settings.PanelHotkey);

    // ---------- maintenance ----------

    private void RunMaintenance(bool sweepOrphans)
    {
        try
        {
            Exclusive(() =>
            {
                RunMaintenanceCore(sweepOrphans);
                return true;
            });
        }
        catch (Exception e)
        {
            Log.Error("Maintenance", e);
        }
    }

    private void RunMaintenanceCore(bool sweepOrphans)
    {
        var removed = new List<string>();
        if (RetentionPolicy.Cutoff(Settings.Retention, DateTimeOffset.Now) is { } cutoff)
            removed.AddRange(Repository.PurgeOlderThan(cutoff));
        removed.AddRange(Repository.EnforceCap(RetentionPolicy.HardCap));
        DeleteBlobs(removed);
        if (removed.Count > 0) Log.Info($"Retention removed {removed.Count} items");

        if (sweepOrphans)
        {
            var swept = Blobs.SweepOrphans(Repository.IsHashReferenced);
            if (swept > 0) Log.Info($"Removed {swept} orphaned blob files");
        }
    }

    /// <summary>Applies a new retention period immediately (after the UI confirmed shrinking).</summary>
    public Task ApplyRetentionAsync() => Task.Run(() => RunMaintenance(sweepOrphans: false));

    // ---------- windows ----------

    public void ShowPanel() => Panel.ShowPanel();

    public void TogglePanel()
    {
        if (Panel.IsShown) Panel.HidePanel();
        else Panel.ShowPanel();
    }

    public void OpenSettings()
    {
        Panel.HideNow();
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Activate();
    }

    internal SettingsWindow? SettingsWindowForTests => _settingsWindow;

    public void OpenDataFolder() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{DataDirectory}\"") { UseShellExecute = true });

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        TrayMenuItem pause = IsPaused
            ? new TrayMenuItem(Loc.Get("Tray_Resume"), Resume)
            : new TrayMenuItem(Loc.Get("Tray_Pause"), Children:
            [
                new TrayMenuItem(Loc.Get("Tray_Pause15"), () => Pause(TimeSpan.FromMinutes(15))),
                new TrayMenuItem(Loc.Get("Tray_Pause60"), () => Pause(TimeSpan.FromHours(1))),
                new TrayMenuItem(Loc.Get("Tray_PauseIndef"), () => Pause(null)),
            ]);

        return
        [
            new TrayMenuItem(Loc.Format("Tray_Open", Settings.PanelHotkey), ShowPanel),
            TrayMenuItem.Separator,
            pause,
            new TrayMenuItem(Loc.Get("Tray_Settings"), OpenSettings),
            TrayMenuItem.Separator,
            new TrayMenuItem(Loc.Get("Tray_Quit"), Quit),
        ];
    }

    public void Quit()
    {
        Dispose();
        Application.Current.Exit();
    }

    public void Dispose()
    {
        Stack?.Dispose();
        Enricher?.Dispose();
        Injector?.Dispose();
        // Let an in-flight capture or maintenance pass finish before closing the database.
        var idle = _ingestGate.Wait(TimeSpan.FromSeconds(2));
        _maintenanceTimer?.Stop();
        _resumeTimer?.Stop();
        Tray?.Dispose();
        Hotkeys?.Dispose();
        Monitor?.Dispose();
        Messages?.Dispose();
        NativeIcon.Destroy(_trayIcon);
        Repository?.Dispose();
        if (idle) _ingestGate.Release();
    }
}
