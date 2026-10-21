using System.Runtime.InteropServices.WindowsRuntime;
using ClipNest.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClipNest.App;

/// <summary>
/// Developer / CI helpers (inactive unless the environment variable is set). <c>CLIPNEST_SNAPSHOT=&lt;dir&gt;</c> seeds sample data, renders the panel (light and dark)
/// and the settings window to PNG files, then exits. Combine with <c>CLIPNEST_DATA</c> to use a scratch data folder.
/// </summary>
internal static class DevTools
{
    public static async void RunSnapshot(AppHost host, string outDir)
    {
        try
        {
            Directory.CreateDirectory(outDir);
            // Baseline: started, panel never shown (the common state after login).
            await Task.Delay(3000);
            Platform.MemoryTrimmer.Trim();
            await Task.Delay(1000);
            var idleStart = MemoryLine();

            Seed(host);
            await host.Panel.ViewModel.ReloadAsync();
            host.Panel.SuppressAutoHide = true;

            foreach (var mode in new[] { ThemeManager.Light, ThemeManager.Dark })
            {
                ThemeManager.SetMode(mode);
                host.Panel.ShowPanel();
                await Task.Delay(2500);
                host.Panel.ViewModel.SetBadgesVisible(mode == ThemeManager.Dark);
                await Task.Delay(400);
                await SaveAsync((UIElement)host.Panel.Content, Path.Combine(outDir, $"panel-{mode}.png"));
                host.Panel.HideNow();
            }

            // Switching edges on a panel that has already been shown (the list keeps its first layout otherwise).
            foreach (var edge in new[] { Core.Settings.PanelEdge.Left, Core.Settings.PanelEdge.Top, Core.Settings.PanelEdge.Right, Core.Settings.PanelEdge.Bottom })
            {
                host.Settings.PanelEdge = edge;
                host.Panel.ShowPanel();
                await Task.Delay(1500);
                await SaveAsync((UIElement)host.Panel.Content, Path.Combine(outDir, $"panel-edge-{edge.ToString().ToLowerInvariant()}.png"));
                host.Panel.HideNow();
            }

            host.Panel.SuppressAutoHide = false;
            host.OpenSettings();
            await Task.Delay(2500);
            if (host.SettingsWindowForTests is { } settings)
            {
                await SaveAsync((UIElement)settings.Content, Path.Combine(outDir, "settings-dark.png"));
                ThemeManager.SetMode(ThemeManager.Light);
                await Task.Delay(800);
                await SaveAsync((UIElement)settings.Content, Path.Combine(outDir, "settings-light.png"));
            }

            var withWindows = MemoryLine();
            host.SettingsWindowForTests?.Close();
            host.Panel.HideNow();
            host.Panel.ViewModel.Unload();
            await Task.Delay(500);
            Platform.MemoryTrimmer.Trim();
            await Task.Delay(1500);
            File.WriteAllText(Path.Combine(outDir, "done.txt"),
                $"ok {DateTime.Now:O}\nidle after start: {idleStart}\nwith windows: {withWindows}\nhidden + trimmed: {MemoryLine()}\n" +
                $"native aot: {!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}\n");
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(outDir, "done.txt"), "error\n" + e);
        }
        host.Quit();
    }

    private static string MemoryLine()
    {
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        return $"workingSet={p.WorkingSet64 / 1048576} MB private={p.PrivateMemorySize64 / 1048576} MB";
    }

    private static void Seed(AppHost host)
    {
        if (host.Repository.Count() > 0) return;
        var now = DateTimeOffset.Now;
        var edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        var notepad = @"C:\Windows\System32\notepad.exe";
        var explorer = @"C:\Windows\explorer.exe";
        var terminal = @"C:\Windows\System32\cmd.exe";
        var paint = @"C:\Windows\System32\mspaint.exe";
        foreach (var app in new[] { edge, notepad, explorer, terminal, paint }) host.Apps.Resolve(app);

        void Add(ClipboardSnapshot s, TimeSpan ago) => host.Ingestor.Ingest(s, now - ago);

        var logo = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.png"));
        Add(new ClipboardSnapshot { Files = [@"C:\Windows\explorer.exe", @"C:\Windows\notepad.exe"], SourceAppPath = explorer }, TimeSpan.FromDays(2));
        Add(new ClipboardSnapshot { Text = "public static int Add(int a, int b)\n{\n    return a + b;\n}", SourceAppPath = terminal }, TimeSpan.FromHours(26));
        Add(new ClipboardSnapshot { Text = "#8B5CF6", SourceAppPath = paint }, TimeSpan.FromHours(5));
        Add(new ClipboardSnapshot { Png = logo, ImageWidth = 256, ImageHeight = 256, SourceAppPath = paint }, TimeSpan.FromHours(2));
        Add(new ClipboardSnapshot { Text = "https://github.com/microsoft/microsoft-ui-xaml", SourceAppPath = edge }, TimeSpan.FromMinutes(42));
        Add(new ClipboardSnapshot { Text = "会议纪要：周五前完成 ClipNest 的面板动画与 Pinboard 交互，下周一评审设计稿。", Html = "<b>会议纪要</b>", SourceAppPath = edge }, TimeSpan.FromMinutes(12));
        Add(new ClipboardSnapshot { Text = "#10B981", SourceAppPath = paint }, TimeSpan.FromMinutes(3));
        Add(new ClipboardSnapshot { Text = "The quick brown fox jumps over the lazy dog. ClipNest keeps everything you copy, so you can find it again in a second.", SourceAppPath = notepad }, TimeSpan.FromSeconds(20));

        var work = host.Repository.AddPinboard("Work", 5);
        host.Repository.AddPinboard("Snippets", 3);
        if (host.Repository.Query(new Core.Abstractions.ClipQuery(SearchQuery.Parse("type:link", now))).FirstOrDefault() is { } link)
        {
            host.Repository.SetPinboard(link.Id, work);
            host.Repository.SetLinkTitle(link.Id, "GitHub - microsoft/microsoft-ui-xaml: WinUI is a user interface layer");
        }
        host.Panel.ViewModel.ReloadTabs();
    }

    // ---------- demo (README screenshots) ----------

    private static Window? _backdrop;

    /// <summary>
    /// <c>CLIPNEST_DEMO=1</c>: fills an empty data folder with sample content (generated pictures, common apps,
    /// no real files) and puts a generated wallpaper behind the panel, for screenshots free of anyone's data.
    /// </summary>
    public static async void RunDemo(AppHost host)
    {
        host.IgnoreClipboard = true;
        try
        {
            var wallpaper = await Task.Run(() => SeedDemo(host));
            await host.Panel.ViewModel.ReloadAsync();
            host.Panel.ViewModel.ReloadTabs();
            ShowBackdrop(wallpaper);
            FillPasteStackWhenOpened(host);
            host.Panel.SuppressAutoHide = true; // focus moves to the screenshot tool between steps
        }
        catch (Exception e)
        {
            Platform.Log.Error("Demo", e);
        }
    }

    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? _stackTimer;

    /// <summary>Demo mode records nothing, so an opened Paste Stack gets sample entries (display only).</summary>
    private static void FillPasteStackWhenOpened(AppHost host)
    {
        _stackTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _stackTimer.Interval = TimeSpan.FromMilliseconds(300);
        _stackTimer.Tick += (_, _) =>
        {
            if (!host.Stack.IsOpen || host.Stack.Items.Count > 0) return;
            var now = DateTimeOffset.Now;
            string[] lines = ["收件人：王小明", "地址：上海市徐汇区示例路 100 号", "电话：021-5555 0123"];
            foreach (var text in lines)
                host.Stack.Items.Add(new ClipItem { Kind = ClipKind.Text, Text = text, CharCount = text.Length, CreatedAt = now });
        };
        _stackTimer.Start();
    }

    /// <summary>Seeds the demo items (once) and returns the wallpaper path.</summary>
    private static string SeedDemo(AppHost host)
    {
        var root = host.DataDirectory;
        var pictures = Path.Combine(root, "Pictures");
        var documents = Path.Combine(root, "Documents");
        var wallpaper = Path.Combine(root, "wallpaper.png");
        if (host.Repository.Count() > 0 && File.Exists(wallpaper)) return wallpaper;
        Directory.CreateDirectory(pictures);
        Directory.CreateDirectory(documents);

        var images = host.Images;
        byte[] Png(Func<double, double, (double R, double G, double B)> shade, int w, int h, string? saveAs = null)
        {
            var png = images.EncodePng(Paint(w, h, shade), w, h);
            if (saveAs is not null) File.WriteAllBytes(saveAs, png);
            return png;
        }

        Png(Aurora, 2560, 1600, wallpaper);
        var aurora = Png(Aurora, 1600, 1000);
        var sunsetFile = Path.Combine(pictures, "sunset.png");
        Png(Sunset, 1600, 1000, sunsetFile);
        Png(Mint, 1200, 800, Path.Combine(pictures, "mint.png"));
        Png(Aurora, 1200, 800, Path.Combine(pictures, "aurora.png"));
        var chart = Png(Chart, 960, 600);
        var roadmap = Path.Combine(documents, "产品路线图.docx");
        File.WriteAllBytes(roadmap, []);

        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var office = Path.Combine(programs, @"Microsoft Office\root\Office16");
        var edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        var chrome = Path.Combine(programs, @"Google\Chrome\Application\chrome.exe");
        var word = Path.Combine(office, "WINWORD.EXE");
        var excel = Path.Combine(office, "EXCEL.EXE");
        var powerpoint = Path.Combine(office, "POWERPNT.EXE");
        var code = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe");
        var notepad = @"C:\Windows\notepad.exe";
        var explorer = @"C:\Windows\explorer.exe";
        string App(string path) => File.Exists(path) ? path : edge;

        var now = DateTimeOffset.Now;
        long Add(ClipboardSnapshot s, TimeSpan ago)
        {
            if (s.SourceAppPath is { } app) host.Apps.Resolve(app);
            return host.Ingestor.Ingest(s, now - ago)?.Item.Id ?? 0;
        }

        Add(new ClipboardSnapshot { Files = Directory.GetFiles(pictures), SourceAppPath = explorer }, TimeSpan.FromDays(3));
        var snippet = Add(new ClipboardSnapshot
        {
            Text = "export function debounce<T extends (...args: any[]) => void>(fn: T, wait = 200) {\n  let timer: ReturnType<typeof setTimeout> | undefined;\n  return (...args: Parameters<T>) => {\n    clearTimeout(timer);\n    timer = setTimeout(() => fn(...args), wait);\n  };\n}",
            SourceAppPath = App(code),
        }, TimeSpan.FromHours(26));
        var sql = Add(new ClipboardSnapshot
        {
            Text = "SELECT app_name, COUNT(*) AS copies\nFROM items\nWHERE created_at > date('now', '-7 day')\nGROUP BY app_name\nORDER BY copies DESC\nLIMIT 10;",
            SourceAppPath = App(code),
        }, TimeSpan.FromHours(23));
        var shell = Add(new ClipboardSnapshot
        {
            Text = "git log --oneline --graph --decorate -20\ngit switch -c feature/pinboard-reorder\ngit push -u origin HEAD",
            SourceAppPath = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
        }, TimeSpan.FromHours(22));
        var template = Add(new ClipboardSnapshot { Text = "{date} 日报\n- 今日完成：\n- 明日计划：\n- 需要协助：", SourceAppPath = notepad }, TimeSpan.FromHours(20));
        Add(new ClipboardSnapshot { Text = "#8B5CF6", SourceAppPath = App(chrome) }, TimeSpan.FromHours(6));
        Add(new ClipboardSnapshot { Png = chart, ImageWidth = 960, ImageHeight = 600, SourceAppPath = App(excel) }, TimeSpan.FromHours(3));
        Add(new ClipboardSnapshot { Files = [roadmap], SourceAppPath = explorer }, TimeSpan.FromHours(2));
        var link = Add(new ClipboardSnapshot { Text = "https://learn.microsoft.com/windows/apps/winui/winui3/", SourceAppPath = edge }, TimeSpan.FromMinutes(75));
        Add(new ClipboardSnapshot
        {
            Text = "产品周会纪要\n1. 面板动画改为 220ms 缓动曲线\n2. Pinboard 支持拖动卡片固定\n3. 周五发布 Beta 版本",
            Html = "<b>产品周会纪要</b><ol><li>面板动画改为 220ms 缓动曲线</li><li>Pinboard 支持拖动卡片固定</li><li>周五发布 Beta 版本</li></ol>",
            SourceAppPath = App(word),
        }, TimeSpan.FromMinutes(30));
        var art = Add(new ClipboardSnapshot { Png = aurora, ImageWidth = 1600, ImageHeight = 1000, SourceAppPath = App(chrome) }, TimeSpan.FromMinutes(18));
        Add(new ClipboardSnapshot { Files = [sunsetFile], SourceAppPath = explorer }, TimeSpan.FromMinutes(9));
        Add(new ClipboardSnapshot { Text = "#FF6B6B", SourceAppPath = App(powerpoint) }, TimeSpan.FromMinutes(4));
        Add(new ClipboardSnapshot { Text = "明天 10:00 和设计组过一遍新版卡片样式，记得带上深色模式的截图。", SourceAppPath = notepad }, TimeSpan.FromMinutes(1));
        Add(new ClipboardSnapshot { Text = "Simplicity is prerequisite for reliability.", SourceAppPath = edge }, TimeSpan.FromSeconds(20));

        host.Repository.SetLinkTitle(link, "WinUI 3 - Windows apps | Microsoft Learn");
        var work = host.Repository.AddPinboard("工作", 5);
        var snippets = host.Repository.AddPinboard("代码片段", 3);
        var ideas = host.Repository.AddPinboard("灵感", 1);
        host.Repository.SetPinboard(link, work);
        host.Repository.SetPinboard(snippet, snippets);
        host.Repository.SetPinboard(template, snippets);
        host.Repository.SetPinboard(sql, snippets);
        host.Repository.SetPinboard(shell, snippets);
        host.Repository.SetPinboard(art, ideas);
        return wallpaper;
    }

    /// <summary>Full-screen wallpaper window (behind the panel) so screenshots don't show the real desktop.</summary>
    private static void ShowBackdrop(string wallpaper)
    {
        _backdrop = new Window
        {
            Content = new Microsoft.UI.Xaml.Controls.Image { Source = new BitmapImage(new Uri(wallpaper)), Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill },
        };
        var appWindow = _backdrop.AppWindow;
        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true; // ClipNest's own windows are topmost too and open above it
        }
        var (work, _) = Platform.DisplayHelper.GetCursorMonitor();
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X, work.Y, work.Width, work.Height));
        Platform.WindowStyler.MakeToolWindow(WinRT.Interop.WindowNative.GetWindowHandle(_backdrop));
        appWindow.Show(false);
    }

    private static byte[] Paint(int w, int h, Func<double, double, (double R, double G, double B)> shade)
    {
        var bgra = new byte[w * h * 4];
        Parallel.For(0, h, y =>
        {
            for (var x = 0; x < w; x++)
            {
                var (r, g, b) = shade((double)x / w, (double)y / h);
                var i = (y * w + x) * 4;
                bgra[i] = ToByte(b);
                bgra[i + 1] = ToByte(g);
                bgra[i + 2] = ToByte(r);
                bgra[i + 3] = 255;
            }
        });
        return bgra;

        static byte ToByte(double v) => (byte)Math.Clamp(v * 255, 0, 255);
    }

    private static (double, double, double) Mix((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private static (double, double, double) Hex(uint rgb) => (((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);

    /// <summary>Soft glowing blobs on deep indigo.</summary>
    private static (double, double, double) Aurora(double x, double y)
    {
        var c = Mix(Hex(0x0F1135), Hex(0x2A1458), y);
        (double X, double Y, double R, uint Color)[] blobs =
        [
            (0.22, 0.30, 0.38, 0x2DD4BF), (0.78, 0.25, 0.40, 0xF472B6), (0.55, 0.70, 0.42, 0x8B5CF6), (0.05, 0.85, 0.35, 0x3B82F6), (0.95, 0.90, 0.30, 0xF59E0B),
        ];
        foreach (var (bx, by, br, color) in blobs)
        {
            var dx = (x - bx) * 1.6;
            var dy = y - by;
            var t = Math.Exp(-(dx * dx + dy * dy) / (br * br)) * 0.85;
            c = Mix(c, Hex(color), t);
        }
        return c;
    }

    /// <summary>Warm sky, soft sun and layered hills.</summary>
    private static (double, double, double) Sunset(double x, double y)
    {
        var sky = y < 0.5 ? Mix(Hex(0xFF7E5F), Hex(0xFEB47B), y * 2) : Mix(Hex(0xFEB47B), Hex(0xFFD8A8), (y - 0.5) * 2);
        var sd = Math.Sqrt(Math.Pow((x - 0.62) * 1.6, 2) + Math.Pow(y - 0.52, 2));
        sky = Mix(sky, Hex(0xFFF4D6), Math.Clamp((0.12 - sd) / 0.02, 0, 1));
        (double Base, double Amp, double Freq, double Phase, uint Color)[] hills =
        [
            (0.62, 0.05, 5.0, 0.3, 0xC2577A), (0.72, 0.06, 3.2, 1.7, 0x8E3B6B), (0.84, 0.05, 2.1, 4.1, 0x4B2457),
        ];
        foreach (var (baseline, amp, freq, phase, color) in hills)
        {
            var ridge = baseline + amp * Math.Sin(x * freq * Math.PI + phase) + amp * 0.4 * Math.Sin(x * freq * 2.7 * Math.PI + phase * 2);
            if (y > ridge) sky = Hex(color);
        }
        return sky;
    }

    /// <summary>Pale mint with diagonal light bands.</summary>
    private static (double, double, double) Mint(double x, double y)
    {
        var c = Mix(Hex(0xA1FFCE), Hex(0xFAFFD1), (x + y) / 2);
        var band = 0.5 + 0.5 * Math.Sin((x * 1.2 + y) * Math.PI * 3);
        return Mix(c, Hex(0x6EE7B7), band * 0.25);
    }

    /// <summary>A small bar chart, the kind of thing copied out of a spreadsheet.</summary>
    private static (double, double, double) Chart(double x, double y)
    {
        double[] values = [0.35, 0.52, 0.47, 0.68, 0.61, 0.82];
        var bg = Hex(0xFFFFFF);
        if (x < 0.08 || x > 0.95 || y < 0.08 || y > 0.88) return bg;
        if (Math.Abs(y - 0.88) < 0.004 || ((y - 0.08) % 0.2 < 0.003)) return Hex(0xE5E7EB);
        var slot = (x - 0.08) / 0.87 * values.Length;
        var i = (int)slot;
        var within = slot - i;
        if (i < values.Length && within is > 0.2 and < 0.8 && y > 0.88 - values[i] * 0.78)
            return Mix(Hex(0x22C55E), Hex(0x15803D), (y - 0.1) / 0.8);
        return bg;
    }

    private static async Task SaveAsync(UIElement element, string path)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(element);
        var pixels = (await rtb.GetPixelsAsync()).ToArray();

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        await File.WriteAllBytesAsync(path, bytes);
    }
}
