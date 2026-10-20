using ClipNest.Core;
using ClipNest.Platform;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace ClipNest.App.Views;

/// <summary>Small editor for text-like items (Ctrl+E). Saving re-hashes the item; rich formatting is dropped.</summary>
public sealed partial class EditWindow : Window
{
    private readonly AppHost _host;
    private readonly ClipItem _item;
    private readonly TextBox _editor;

    private EditWindow(AppHost host, ClipItem item)
    {
        _host = host;
        _item = item;
        Title = Loc.Get("Edit_Title");
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ClipNest.ico"));
        WindowStyler.SetCaptureExcluded(Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id), host.Settings.HideFromCapture);

        _editor = new TextBox
        {
            Text = item.Text ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsSpellCheckEnabled = false,
            FontFamily = PreviewWindow.LooksLikeCode(item.Text) ? new FontFamily("Cascadia Mono, Consolas") : FontFamily.XamlAutoFontFamily,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_editor, ScrollBarVisibility.Auto);
        _editor.KeyDown += (_, e) =>
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl && e.Key == VirtualKey.S)
            {
                Save();
                e.Handled = true;
            }
        };

        var save = new Button { Content = Loc.Get("Common_Save"), Style = (Style)Application.Current.Resources["AccentButtonStyle"], MinWidth = 96 };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = Loc.Get("Common_Cancel"), MinWidth = 96 };
        cancel.Click += (_, _) => Close();

        var notice = new TextBlock
        {
            Text = item.Kind == ClipKind.RichText ? Loc.Get("Edit_RichNotice") : "",
            Foreground = ThemeManager.Brush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var buttons = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } } };
        buttons.Children.Add(notice);
        Grid.SetColumn(cancel, 1);
        Grid.SetColumn(save, 2);
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);

        var title = new TextBlock { Text = Loc.Get("Edit_Title"), Margin = new Thickness(16, 10, 0, 0), FontSize = 12 };
        var root = new Grid
        {
            RowSpacing = 12,
            Padding = new Thickness(16, 8, 16, 16),
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition(), new RowDefinition { Height = GridLength.Auto } },
        };
        root.Children.Add(title);
        Grid.SetRow(_editor, 1);
        Grid.SetRow(buttons, 2);
        root.Children.Add(_editor);
        root.Children.Add(buttons);
        Content = root;
        SetTitleBar(title);
        ThemeManager.Register(this);

        var (work, scale) = DisplayHelper.GetCursorMonitor();
        var w = (int)(640 * scale);
        var h = (int)(460 * scale);
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 3, w, h));
        Activated += (_, _) => _editor.Focus(FocusState.Programmatic);
    }

    public static void Open(AppHost host, ClipItem item, PanelWindow panel)
    {
        if (item.Kind is ClipKind.Image or ClipKind.File) return;
        PreviewWindow.Close(panel);
        panel.HideNow();
        new EditWindow(host, item).Activate();
    }

    private void Save()
    {
        var text = _editor.Text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Length > 0 && text != _item.Text)
        {
            var kind = ClipClassifier.TryClassify(new ClipboardSnapshot { Text = text }) ?? ClipKind.Text;
            _host.UpdateText(_item, kind, text);
        }
        Close();
    }
}
