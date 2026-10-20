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
                $"ok {DateTime.Now:O}\nwith windows: {withWindows}\nhidden + trimmed: {MemoryLine()}\n" +
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
