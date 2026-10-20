using ClipNest.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace ClipNest.App.ViewModels;

/// <summary>Presentation state of one card. Created and used on the UI thread only.</summary>
public sealed partial class ClipCardViewModel : ObservableObject
{
    private static readonly Dictionary<uint, SolidColorBrush> BrushCache = [];
    private static readonly Dictionary<string, BitmapImage> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly AppHost _host;
    private ImageSource? _thumbnail;
    private bool _thumbnailRequested;
    private int _quickIndex;
    private bool _showBadge;
    private string _timeText = "";
    private bool _isSelected;

    public ClipCardViewModel(ClipItem item, AppHost host, CardMetrics metrics)
    {
        Item = item;
        _host = host;
        Metrics = metrics;
        RefreshTime(DateTimeOffset.Now);
    }

    public ClipItem Item { get; private set; }
    public CardMetrics Metrics { get; }
    public long Id => Item.Id;
    public ClipKind Kind => Item.Kind;

    // ---------- header ----------

    public string DisplayTitle => string.IsNullOrWhiteSpace(Item.Title) ? DefaultTitle : Item.Title!;

    private string DefaultTitle => Item.Kind == ClipKind.File && Item.FileCount > 1
        ? Loc.Get("Kind_Files")
        : Loc.KindName(Item.Kind);

    public string TimeText { get => _timeText; private set => SetProperty(ref _timeText, value); }

    private AppInfoView App => _host.AppView(Item.SourceAppPath);

    public SolidColorBrush HeaderBrush => Brush(App.Color);
    public SolidColorBrush HeaderForeground => Brush(ColorMath.PrefersDarkText(App.Color) ? 0xFF000000u : 0xFFFFFFFFu);

    public ImageSource? AppIcon
    {
        get
        {
            var file = App.IconFile;
            if (file is null) return null;
            if (!IconCache.TryGetValue(file, out var icon))
            {
                icon = new BitmapImage(new Uri(file)) { DecodePixelWidth = 56, DecodePixelType = DecodePixelType.Physical };
                IconCache[file] = icon;
            }
            return icon;
        }
    }

    public string AppName => App.Name;

    // ---------- body ----------

    /// <summary>First part of the text, enough to fill a card.</summary>
    public string PreviewText
    {
        get
        {
            var t = Item.Text ?? "";
            return t.Length > 600 ? t[..600] : t;
        }
    }

    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested && Item.Kind == ClipKind.Image)
            {
                _thumbnailRequested = true;
                var path = _host.ThumbnailPath(Item);
                if (path is not null)
                {
                    _thumbnail = new BitmapImage(new Uri(path))
                    {
                        DecodePixelHeight = (int)Math.Round(Metrics.Height * 1.5),
                        DecodePixelType = DecodePixelType.Logical,
                    };
                }
            }
            return _thumbnail;
        }
    }

    public string LinkHost => Uri.TryCreate(Item.Text, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase) : Item.Text ?? "";
    public string LinkTitle => Item.LinkTitle ?? "";
    public Visibility LinkTitleVisibility => string.IsNullOrEmpty(Item.LinkTitle) ? Visibility.Collapsed : Visibility.Visible;

    private uint ColorArgb
    {
        get
        {
            if (!ClipClassifier.TryParseColor(Item.Text ?? "", out var normalized)) return 0xFF808080; // never throw from a binding
            var hex = normalized.TrimStart('#');
            var rgb = Convert.ToUInt32(hex[..6], 16);
            var alpha = hex.Length == 8 ? Convert.ToUInt32(hex[6..8], 16) : 0xFF;
            return (alpha << 24) | rgb;
        }
    }

    public SolidColorBrush ColorBrush => Brush(ColorArgb);
    public SolidColorBrush ColorForeground => Brush(ColorMath.PrefersDarkText(ColorArgb | 0xFF000000) ? 0xD9000000u : 0xF2FFFFFFu);
    public string ColorText => Item.Text ?? "";

    public string FileName => Item.FilePaths is { Count: > 0 } paths ? Path.GetFileName(paths[0].TrimEnd('\\')) is { Length: > 0 } n ? n : paths[0] : "";
    public string FileDirectory => Item.FilePaths is { Count: > 0 } paths ? Path.GetDirectoryName(paths[0]) ?? "" : "";
    /// <summary>Segoe Fluent Icons glyph: folder, multiple documents, or a single document.</summary>
    public string FileGlyph => Item.FileCount > 1 ? "\uE8F1" : File1()?.IsDirectory == true ? "\uE8B7" : "\uE8A5";

    // ---------- footer ----------

    public string MetaText => Item.Kind switch
    {
        ClipKind.Text or ClipKind.RichText => Loc.Format("Meta_Chars", Item.CharCount),
        ClipKind.Image when Item.ImageWidth is { } w && Item.ImageHeight is { } h => Loc.Format("Meta_Image", w, h),
        ClipKind.Link => Item.Text ?? "",
        ClipKind.File when Item.FileCount > 1 => Loc.Format("Meta_Files", Item.FileCount),
        ClipKind.File => FileSizeText(),
        ClipKind.Color => RgbText(),
        _ => "",
    };

    private string FileSizeText() => File1() switch
    {
        null => "",
        { IsDirectory: true } => Loc.Get("Meta_Folder"),
        { Exists: false } => Loc.Get("Meta_Missing"),
        { } f => FormatBytes(f.Size),
    };

    private readonly record struct FileFacts(bool Exists, bool IsDirectory, long Size);

    private FileFacts? _fileFacts;
    private bool _fileFactsRequested;

    /// <summary>
    /// Facts about the first file, gathered off the UI thread: a path on an offline network share can
    /// block for seconds. Returns null until available, then refreshes the bound properties.
    /// </summary>
    private FileFacts? File1()
    {
        if (_fileFacts is not null || _fileFactsRequested) return _fileFacts;
        _fileFactsRequested = true;
        var path = Item.FilePaths.FirstOrDefault();
        if (path is null) return null;
        var ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _ = Task.Run(() =>
        {
            FileFacts facts;
            try
            {
                facts = Directory.Exists(path) ? new FileFacts(true, true, 0)
                    : new FileInfo(path) is { Exists: true } info ? new FileFacts(true, false, info.Length)
                    : new FileFacts(false, false, 0);
            }
            catch (Exception)
            {
                facts = new FileFacts(false, false, 0);
            }
            ui?.TryEnqueue(() =>
            {
                _fileFacts = facts;
                OnPropertyChanged(nameof(FileGlyph));
                OnPropertyChanged(nameof(MetaText));
            });
        });
        return null;
    }

    private string RgbText()
    {
        var c = ColorArgb;
        return $"RGB {(c >> 16) & 0xFF}, {(c >> 8) & 0xFF}, {c & 0xFF}";
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
    }

    // ---------- quick-paste badge ----------

    public int QuickIndex
    {
        get => _quickIndex;
        set
        {
            if (SetProperty(ref _quickIndex, value))
            {
                OnPropertyChanged(nameof(BadgeText));
                OnPropertyChanged(nameof(BadgeVisibility));
            }
        }
    }

    public bool ShowBadge
    {
        get => _showBadge;
        set
        {
            if (SetProperty(ref _showBadge, value)) OnPropertyChanged(nameof(BadgeVisibility));
        }
    }

    public string BadgeText => _quickIndex.ToString();
    public Visibility BadgeVisibility => _showBadge && _quickIndex is >= 1 and <= 9 ? Visibility.Visible : Visibility.Collapsed;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) OnPropertyChanged(nameof(SelectionVisibility));
        }
    }

    public Visibility SelectionVisibility => _isSelected ? Visibility.Visible : Visibility.Collapsed;

    // ---------- updates ----------

    public void RefreshTime(DateTimeOffset now) => TimeText = Loc.RelativeTime(Item.CreatedAt, now);

    /// <summary>Applies changes made to the underlying item (rename, OCR, touch, link title…).</summary>
    public void Update(ClipItem item)
    {
        var appChanged = !string.Equals(item.SourceAppPath, Item.SourceAppPath, StringComparison.OrdinalIgnoreCase);
        Item = item;
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(LinkTitle));
        OnPropertyChanged(nameof(LinkTitleVisibility));
        if (appChanged)
        {
            OnPropertyChanged(nameof(HeaderBrush));
            OnPropertyChanged(nameof(HeaderForeground));
            OnPropertyChanged(nameof(AppIcon));
        }
        RefreshTime(DateTimeOffset.Now);
    }

    public static SolidColorBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            BrushCache[argb] = brush;
        }
        return brush;
    }

    /// <summary>Drops cached icon bitmaps (called when the panel is unloaded to save memory).</summary>
    public static void ClearSharedCaches() => IconCache.Clear();
}
