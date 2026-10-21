using ClipNest.App.ViewModels;
using ClipNest.Core;
using ClipNest.Platform;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.FileProperties;
using WinRT.Interop;

namespace ClipNest.App.Views;

/// <summary>
/// Quick Look–style preview shown above the panel. It never takes focus, so the panel keeps handling keys
/// (Space/Esc close it, arrow keys switch the previewed item).
/// </summary>
public sealed partial class PreviewWindow : Window
{
    private static PreviewWindow? _current;

    private readonly AppHost _host;
    private readonly nint _hwnd;
    private readonly ContentControl _content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _subtitle = new() { FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };

    public long? ItemId { get; private set; }

    private PreviewWindow(AppHost host)
    {
        _host = host;
        _hwnd = WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        WindowStyler.MakeToolWindow(_hwnd);
        WindowStyler.MakeNoActivate(_hwnd);
        WindowStyler.SetRoundedCorners(_hwnd);
        WindowStyler.RemoveFrame(_hwnd);
        WindowStyler.SetCaptureExcluded(_hwnd, host.Settings.HideFromCapture);
        SystemBackdrop = new DesktopAcrylicBackdrop();

        var header = new StackPanel { Spacing = 2, Margin = new Thickness(20, 14, 20, 10) };
        header.Children.Add(_title);
        header.Children.Add(_subtitle);
        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
        root.Children.Add(header);
        Grid.SetRow(_content, 1);
        root.Children.Add(_content);
        Content = root;
        ThemeManager.Register(this);
    }

    public static bool IsOpen => _current is not null;

    /// <summary>Opens the preview for <paramref name="item"/>, or closes it if already showing that item.</summary>
    public static void Toggle(AppHost host, ClipItem item, PanelWindow panel)
    {
        if (_current is { } open && open.ItemId == item.Id)
        {
            Close(panel);
            return;
        }
        _current ??= new PreviewWindow(host);
        panel.SuppressAutoHide = true;
        _current.ShowItem(item);
    }

    /// <summary>Follows the panel selection while open.</summary>
    public static void Follow(ClipItem item)
    {
        if (_current is { } open && open.ItemId != item.Id) open.ShowItem(item);
    }

    public static void Close(PanelWindow? panel)
    {
        if (_current is null) return;
        var window = _current;
        _current = null;
        window.Close();
        if (panel is not null) panel.SuppressAutoHide = false;
    }

    private void ShowItem(ClipItem item)
    {
        ItemId = item.Id;
        var app = _host.AppView(item.SourceAppPath);
        _title.Text = string.IsNullOrWhiteSpace(item.Title) ? Loc.KindName(item.Kind) : item.Title;
        _subtitle.Text = string.Join("  ·  ", new[] { app.Name, Loc.RelativeTime(item.CreatedAt, DateTimeOffset.Now) }.Where(s => s.Length > 0));
        _content.Content = Build(item);

        // Centered in the part of the screen the panel leaves free.
        var (work, scale) = DisplayHelper.GetCursorMonitor();
        var panel = PanelWindow.DockedRect(_host.Settings, work, scale);
        var gap = (int)(8 * scale);
        var (fx, fy, fw, fh) = _host.Settings.PanelEdge switch
        {
            Core.Settings.PanelEdge.Top => (work.X, panel.Y + panel.Height + gap, work.Width, work.Y + work.Height - panel.Y - panel.Height - gap),
            Core.Settings.PanelEdge.Left => (panel.X + panel.Width + gap, work.Y, work.X + work.Width - panel.X - panel.Width - gap, work.Height),
            Core.Settings.PanelEdge.Right => (work.X, work.Y, panel.X - gap - work.X, work.Height),
            _ => (work.X, work.Y, work.Width, panel.Y - gap - work.Y),
        };
        var w = (int)Math.Min(fw * 0.9, work.Width * 0.56);
        var h = (int)Math.Max(240 * scale, fh * 0.86);
        var x = fx + (fw - w) / 2;
        var y = fy + (fh - h) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
        WindowStyler.RemoveFrame(_hwnd); // the presenter may restore its frame styles
        AppWindow.Show(false);
    }

    private UIElement Build(ClipItem item)
    {
        switch (item.Kind)
        {
            case ClipKind.Image:
            {
                var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(16, 0, 16, 16) };
                var path = item.Blobs.HasFlag(BlobFlags.Png) ? _host.Blobs.PathFor(item.ContentHash, BlobFlags.Png) : null;
                if (path is not null) image.Source = new BitmapImage(new Uri(path));
                return image;
            }
            case ClipKind.Color:
            {
                var vm = new ClipCardViewModel(item, _host, new CardMetrics());
                var swatch = new Border { Background = vm.ColorBrush, CornerRadius = new CornerRadius(12), Margin = new Thickness(16, 0, 16, 16) };
                swatch.Child = new TextBlock
                {
                    Text = $"{item.Text}\n{vm.MetaText}",
                    Foreground = vm.ColorForeground,
                    FontSize = 28,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                return swatch;
            }
            case ClipKind.File when item.FilePaths.Count == 1:
            {
                var grid = new Grid { Margin = new Thickness(16, 0, 16, 16), RowSpacing = 10 };
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var image = new Image { Stretch = Stretch.Uniform };
                var path = new TextBlock { Text = item.FilePaths[0], FontSize = 13, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.75 };
                Grid.SetRow(path, 1);
                grid.Children.Add(image);
                grid.Children.Add(path);
                LoadFilePreview(image, item.FilePaths[0]);
                return grid;
            }
            case ClipKind.File:
                return Scroll(new TextBlock { Text = string.Join("\n", item.FilePaths), FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 22 });
            case ClipKind.Link:
            {
                var panel = new StackPanel { Spacing = 8 };
                if (!string.IsNullOrEmpty(item.LinkTitle))
                    panel.Children.Add(new TextBlock { Text = item.LinkTitle, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = item.Text, FontSize = 15, TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Brush("AccentTextFillColorPrimaryBrush") });
                return Scroll(panel);
            }
            default:
                return Scroll(new TextBlock
                {
                    Text = item.Text ?? "",
                    FontSize = 14,
                    LineHeight = 21,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = LooksLikeCode(item.Text) ? new FontFamily("Cascadia Mono, Consolas") : AppFonts.Ui,
                });
        }

        static ScrollViewer Scroll(UIElement content) => new()
        {
            Content = content,
            Padding = new Thickness(20, 0, 20, 16),
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic", ".avif", ".jxr"];

    /// <summary>Full image for picture files; for anything else (video, PDF…) a large shell thumbnail.</summary>
    private static void LoadFilePreview(Image image, string path)
    {
        if (!ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            LoadShellThumbnail(image, path);
            return;
        }
        var bitmap = new BitmapImage();
        // No codec for the format (or not a real image): the shell may still have a thumbnail.
        bitmap.ImageFailed += (_, _) => LoadShellThumbnail(image, path);
        bitmap.UriSource = new Uri(path);
        image.Source = bitmap;
    }

    private static async void LoadShellThumbnail(Image image, string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 1024, ThumbnailOptions.ResizeThumbnail);
            if (thumb is null || thumb.Size == 0) return;
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumb);
            if (thumb.Type == ThumbnailType.Icon) image.MaxHeight = image.MaxWidth = 128;
            image.Source = bitmap;
        }
        catch (Exception)
        {
            // missing file or no access: the path text is all we can show
        }
    }

    /// <summary>Heuristic for a monospace preview: indentation or common code punctuation on several lines.</summary>
    internal static bool LooksLikeCode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var lines = text.Split('\n', 50);
        if (lines.Length < 3) return false;
        var codeish = lines.Count(l => l.StartsWith("    ") || l.StartsWith('\t') || l.TrimEnd().EndsWith(';') || l.TrimEnd().EndsWith('{') || l.TrimEnd().EndsWith('}')
            || CodeLineStarts.Any(s => l.StartsWith(s, StringComparison.Ordinal)));
        return codeish * 3 >= lines.Length;
    }

    /// <summary>SQL keywords (upper case, as usually written) and common shell commands.</summary>
    private static readonly string[] CodeLineStarts =
    [
        "SELECT ", "FROM ", "WHERE ", "GROUP BY ", "ORDER BY ", "JOIN ", "LEFT JOIN ", "INSERT ", "UPDATE ", "DELETE ", "VALUES", "LIMIT ",
        "$ ", "git ", "npm ", "pnpm ", "yarn ", "dotnet ", "docker ", "kubectl ", "pip ", "python ", "cd ", "curl ",
    ];
}
