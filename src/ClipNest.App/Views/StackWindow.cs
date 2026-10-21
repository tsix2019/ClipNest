using System.Collections.Specialized;
using ClipNest.App.Services;
using ClipNest.App.ViewModels;
using ClipNest.Core;
using ClipNest.Platform;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using WinRT.Interop;

namespace ClipNest.App.Views;

/// <summary>Floating Paste Stack window (bottom-right, never takes focus from the app you work in).</summary>
public sealed partial class StackWindow : Window
{
    private readonly AppHost _host;
    private readonly PasteStack _stack;
    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly TextBlock _count = new() { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(4, 8, 4, 8) };
    private readonly Button _direction = new() { Padding = new Thickness(8, 4, 8, 4) };

    public StackWindow(AppHost host, PasteStack stack)
    {
        _host = host;
        _stack = stack;
        var hwnd = WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        WindowStyler.MakeToolWindow(hwnd);
        WindowStyler.MakeNoActivate(hwnd);
        WindowStyler.SetRoundedCorners(hwnd);
        WindowStyler.RemoveFrame(hwnd);
        WindowStyler.SetCaptureExcluded(hwnd, host.Settings.HideFromCapture);
        SystemBackdrop = new DesktopAcrylicBackdrop();

        var title = new TextBlock { Text = Loc.Get("Stack_Title"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var close = IconButton("\uE711", Loc.Get("Stack_Close"), () => _stack.Close());
        var clear = IconButton("\uE894", Loc.Get("Stack_Clear"), () => _stack.Clear());
        _direction.Click += (_, _) =>
        {
            _stack.NewestFirst = !_stack.NewestFirst;
            UpdateDirection();
        };

        var header = new Grid { ColumnSpacing = 6, Padding = new Thickness(14, 10, 8, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titles.Children.Add(title);
        titles.Children.Add(_count);
        header.Children.Add(titles);
        Grid.SetColumn(_direction, 2);
        Grid.SetColumn(clear, 3);
        Grid.SetColumn(close, 4);
        header.Children.Add(_direction);
        header.Children.Add(clear);
        header.Children.Add(close);

        var scroll = new ScrollViewer { Content = new StackPanel { Children = { _list, _hint } }, Padding = new Thickness(10, 0, 10, 10) };
        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
        root.Children.Add(header);
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
        ThemeManager.Register(this);
        ThemeManager.Changed += Rebuild;
        Closed += (_, _) => ThemeManager.Changed -= Rebuild;

        _stack.Items.CollectionChanged += OnItemsChanged;
        Closed += (_, _) => _stack.Items.CollectionChanged -= OnItemsChanged;
        UpdateDirection();
        Rebuild();
    }

    public void ShowStack()
    {
        var (work, scale) = DisplayHelper.GetCursorMonitor();
        var w = (int)(320 * scale);
        var h = (int)(Math.Min(460, work.Height / scale * 0.6) * scale);
        var m = (int)(12 * scale);
        AppWindow.MoveAndResize(new RectInt32(work.X + work.Width - w - m, work.Y + work.Height - h - m, w, h));
        WindowStyler.RemoveFrame(WinRT.Interop.WindowNative.GetWindowHandle(this)); // the presenter may restore its frame styles
        AppWindow.Show(false);
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void UpdateDirection()
    {
        _direction.Content = new FontIcon { Glyph = _stack.NewestFirst ? "\uE74A" : "\uE74B", FontSize = 14 };
        ToolTipService.SetToolTip(_direction, Loc.Get(_stack.NewestFirst ? "Stack_NewestFirst" : "Stack_OldestFirst"));
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var index = 0;
        foreach (var item in _stack.Items) _list.Children.Add(Row(item, index++ == 0));
        _count.Text = _stack.Items.Count > 0 ? Loc.Format("Stack_Count", _stack.Items.Count) : "";
        _hint.Text = Loc.Format(_stack.Items.Count == 0 ? "Stack_EmptyHint" : "Stack_NextHint", "Ctrl+V");
    }

    private UIElement Row(ClipItem item, bool isNext)
    {
        var app = _host.AppView(item.SourceAppPath);
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(10, 8, 6, 8),
            CornerRadius = new CornerRadius(8),
            Background = ThemeManager.Brush(isNext ? "TabSelectedBrush" : "TabHoverBrush"),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new Border { Background = ClipCardViewModel.Brush(app.Color), CornerRadius = new CornerRadius(2) });

        UIElement content;
        if (item.Kind == ClipKind.Image && _host.ThumbnailPath(item) is { } thumb)
        {
            content = new Image { Source = new BitmapImage(new Uri(thumb)) { DecodePixelHeight = 96 }, Height = 48, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform };
        }
        else
        {
            var text = item.Kind == ClipKind.File ? string.Join(", ", item.FilePaths.Select(Path.GetFileName)) : item.Text ?? "";
            content = new TextBlock { Text = text.Length > 300 ? text[..300] : text, MaxLines = 2, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
        }
        Grid.SetColumn((FrameworkElement)content, 1);
        row.Children.Add(content);

        var remove = IconButton("\uE711", Loc.Get("Menu_Delete"), () => _stack.Remove(item));
        remove.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);
        return row;
    }

    private static Button IconButton(string glyph, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Padding = new Thickness(7),
            Background = ClipCardViewModel.Brush(0),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }
}
