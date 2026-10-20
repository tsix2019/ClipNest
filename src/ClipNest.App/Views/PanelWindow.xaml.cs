using ClipNest.App.ViewModels;
using ClipNest.Core;
using ClipNest.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using WindowActivatedEventArgs = Microsoft.UI.Xaml.WindowActivatedEventArgs;

namespace ClipNest.App.Views;

/// <summary>The bottom-docked card panel (Paste's main window).</summary>
public sealed partial class PanelWindow : Window
{
    private const double MinPanelHeight = 200;
    private const double MaxPanelHeight = 520;
    private const double EdgeMargin = 8;

    private readonly AppHost _host;
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _searchDebounce;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly DispatcherQueueTimer _unloadTimer;
    private readonly DispatcherQueueTimer _hideTimer;
    private ScrollViewer? _scroller;
    private bool _shown;
    private bool _hiding;
    private bool _showing;
    private bool _showCancelled;

    // resize-by-dragging state
    private bool _resizing;
    private int _resizeStartY;
    private double _resizeStartHeight;
    private double _scale = 1;
    private PixelRect _workArea;

    public PanelWindow(AppHost host)
    {
        _host = host;
        ViewModel = new PanelViewModel(host);
        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);
        ConfigureWindow();
        ThemeManager.Register(this);

        var ui = DispatcherQueue;
        _searchDebounce = ui.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(120);
        _searchDebounce.IsRepeating = false;
        _searchDebounce.Tick += (_, _) => _ = ViewModel.ReloadAsync();

        _clockTimer = ui.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(30);
        _clockTimer.Tick += (_, _) => ViewModel.RefreshTimes();

        // Release cards and trim memory once the panel has been hidden for a while.
        _unloadTimer = ui.CreateTimer();
        _unloadTimer.Interval = TimeSpan.FromSeconds(45);
        _unloadTimer.IsRepeating = false;
        _unloadTimer.Tick += (_, _) =>
        {
            if (_shown) return;
            ViewModel.Unload();
            MemoryTrimmer.Trim();
        };

        _hideTimer = ui.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(130);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) =>
        {
            if (_hiding) HideNow();
        };

        Activated += OnActivated;
        host.Ingested += ViewModel.OnIngested;
        host.ItemChanged += ViewModel.OnItemChanged;
        host.PauseChanged += UpdatePausedPill;
        ViewModel.ItemsReset += OnItemsReset;
        CardList.Loaded += (_, _) => HookScroller();
        CardList.SelectionChanged += (_, e) =>
        {
            foreach (var added in e.AddedItems.OfType<ClipCardViewModel>()) added.IsSelected = true;
            foreach (var removed in e.RemovedItems.OfType<ClipCardViewModel>()) removed.IsSelected = false;
            if (PreviewWindow.IsOpen && CardList.SelectedItem is ClipCardViewModel card) PreviewWindow.Follow(card.Item);
        };
        UpdatePausedPill();
    }

    public PanelViewModel ViewModel { get; }

    public bool IsShown => _shown;

    /// <summary>While true (preview open), losing activation does not hide the panel.</summary>
    public bool SuppressAutoHide { get; set; }

    private void ConfigureWindow()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ClipNest.ico"));

        WindowStyler.MakeToolWindow(_hwnd);
        WindowStyler.SetRoundedCorners(_hwnd);
        ApplyCaptureSetting();
        SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
    }

    public void ApplyCaptureSetting() => WindowStyler.SetCaptureExcluded(_hwnd, _host.Settings.HideFromCapture);

    // ---------- show / hide ----------

    public async void ShowPanel()
    {
        if (_showing) return; // already on its way (double hotkey press, tray + hotkey)
        if (_shown)
        {
            if (_hiding) CancelHide();
            Activate();
            WindowStyler.BringToFront(_hwnd);
            return;
        }

        _host.Injector.CaptureTarget();
        _unloadTimer.Stop();
        _showing = true;
        _showCancelled = false;
        try
        {
            await PrepareListAsync();
        }
        finally
        {
            _showing = false;
        }
        if (_showCancelled) return; // HideNow() ran while loading (e.g. Paste Stack opened)

        PositionOnCursorMonitor();
        Root.Opacity = 0;
        _shown = true;
        AppWindow.Show(true);
        Activate();
        if (!WindowStyler.BringToFront(_hwnd)) Log.Warn("Panel could not take the foreground");
        ShowStoryboard.Begin();
        _clockTimer.Start();
        // The list may realize its containers only now (first show): select + focus once layout ran.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (CardList.SelectedIndex < 0 && ViewModel.Items.Count > 0) CardList.SelectedIndex = 0;
            FocusSelectedCard();
        });
    }

    /// <summary>Every open starts from a clean search and the newest item.</summary>
    private async Task PrepareListAsync()
    {
        if (SearchBox.Text.Length > 0)
        {
            SearchBox.Text = "";
            ViewModel.SearchText = "";
            _searchDebounce.Stop();
            await ViewModel.ReloadAsync();
        }
        else if (!ViewModel.IsLoaded)
        {
            await ViewModel.ReloadAsync();
        }
        else
        {
            ViewModel.RefreshTimes();
            OnItemsReset();
        }
    }

    private void CancelHide()
    {
        _hiding = false;
        _hideTimer.Stop();
        HideStoryboard.Stop();
        Root.Opacity = 1;
        RootTranslate.Y = 0;
    }

    /// <summary>Animated hide (Esc, click outside, hotkey).</summary>
    public void HidePanel()
    {
        if (!_shown || _hiding) return;
        _hiding = true;
        HideStoryboard.Begin();
        // A timer rather than Storyboard.Completed: animations do not tick when nothing is
        // composed (locked session, display off), and the panel must still go away.
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>Immediate hide (before pasting, so focus can return to the target app quickly).</summary>
    public void HideNow()
    {
        if (_showing) _showCancelled = true;
        _hiding = false;
        _hideTimer.Stop();
        PreviewWindow.Close(this);
        if (!_shown) return;
        _shown = false;
        _clockTimer.Stop();
        ViewModel.SetBadgesVisible(false);
        AppWindow.Hide();
        _unloadTimer.Start();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && _shown && !_resizing && !SuppressAutoHide)
            HidePanel();
    }

    private void PositionOnCursorMonitor()
    {
        (_workArea, _scale) = DisplayHelper.GetCursorMonitor();
        ApplyHeight(_host.Settings.PanelHeight);
    }

    private void ApplyHeight(double heightDip)
    {
        var h = (int)Math.Round(heightDip * _scale);
        var m = (int)Math.Round(EdgeMargin * _scale);
        AppWindow.MoveAndResize(new RectInt32(_workArea.X + m, _workArea.Y + _workArea.Height - h - m, _workArea.Width - 2 * m, h));
        ViewModel.Metrics.Update(heightDip);
    }

    // ---------- list helpers ----------

    private void OnItemsReset()
    {
        if (ViewModel.Items.Count == 0) return;
        CardList.SelectedIndex = 0;
        CardList.ScrollIntoView(ViewModel.Items[0]);
        _scroller?.ChangeView(0, null, null, disableAnimation: true);
        // e.g. after clicking a tab: keep keyboard focus on the cards, not on the clicked button.
        if (_shown && !SearchFocused) FocusSelectedCard();
    }

    private void FocusSelectedCard()
    {
        var index = Math.Max(0, CardList.SelectedIndex);
        if (ViewModel.Items.Count == 0)
        {
            SearchBox.Focus(FocusState.Programmatic);
            return;
        }
        if (CardList.ContainerFromIndex(index) is ListViewItem container) container.Focus(FocusState.Programmatic);
        else CardList.Focus(FocusState.Programmatic);
    }

    private void Select(int index)
    {
        if (ViewModel.Items.Count == 0) return;
        index = Math.Clamp(index, 0, ViewModel.Items.Count - 1);
        CardList.SelectedIndex = index;
        CardList.ScrollIntoView(ViewModel.Items[index]);
    }

    /// <summary>Selected cards in on-screen order; falls back to the first card.</summary>
    private List<ClipCardViewModel> SelectedCards()
    {
        var selected = CardList.SelectedItems.OfType<ClipCardViewModel>()
            .OrderBy(ViewModel.Items.IndexOf)
            .ToList();
        if (selected.Count == 0 && ViewModel.Items.Count > 0) selected.Add(ViewModel.Items[0]);
        return selected;
    }

    private void HookScroller()
    {
        _scroller ??= FindChild<ScrollViewer>(CardList);
        if (_scroller is null) return;
        _scroller.ViewChanged += (_, _) =>
        {
            if (_scroller.HorizontalOffset > _scroller.ScrollableWidth - ViewModel.Metrics.Width * 3)
                _ = ViewModel.LoadMoreAsync();
        };
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    // ---------- actions ----------

    private Task PasteAsync(IReadOnlyList<ClipCardViewModel> cards, bool plainText) =>
        _host.PasteAsync(cards.Select(c => c.Item).ToList(), plainText);

    private void PasteSelected(bool plainText) => _ = PasteAsync(SelectedCards(), plainText);

    private void PasteAt(int index, bool plainText)
    {
        if (index < ViewModel.Items.Count) _ = PasteAsync([ViewModel.Items[index]], plainText);
    }

    private void CopySelected() =>
        _ = _host.PasteAsync(SelectedCards().Select(c => c.Item).ToList(), plainText: false, pasteIntoApp: false);

    private void DeleteSelected()
    {
        var cards = SelectedCards();
        if (cards.Count == 0) return;
        var index = ViewModel.Items.IndexOf(cards[0]);
        _host.Delete(cards.Select(c => c.Item));
        foreach (var card in cards) ViewModel.Remove(card);
        Select(index);
        FocusSelectedCard();
    }

    private async void UndoDelete()
    {
        var item = _host.Undo();
        if (item is null) return;
        await ViewModel.ReloadAsync();
        var vm = ViewModel.Items.FirstOrDefault(i => i.Id == item.Id);
        if (vm is not null) Select(ViewModel.Items.IndexOf(vm));
    }

    // ---------- keyboard ----------

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private bool SearchFocused => SearchBox.FocusState != FocusState.Unfocused;

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        var inSearch = SearchFocused;

        // A focused button (tab, filter, settings) handles its own Enter/Space.
        if (e.OriginalSource is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase && e.Key is VirtualKey.Enter or VirtualKey.Space) return;

        switch (e.Key)
        {
            case VirtualKey.Control:
                ViewModel.SetBadgesVisible(true);
                return;

            case VirtualKey.Escape:
                if (PreviewWindow.IsOpen) PreviewWindow.Close(this);
                else if (inSearch && SearchBox.Text.Length > 0) SearchBox.Text = "";
                else HidePanel();
                e.Handled = true;
                return;

            case VirtualKey.Enter:
                PasteSelected(plainText: shift);
                e.Handled = true;
                return;

            case >= VirtualKey.Number1 and <= VirtualKey.Number9 when ctrl:
                PasteAt(e.Key - VirtualKey.Number1, plainText: shift);
                e.Handled = true;
                return;

            case >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 when ctrl:
                PasteAt(e.Key - VirtualKey.NumberPad1, plainText: shift);
                e.Handled = true;
                return;

            case VirtualKey.Left or VirtualKey.Right when ctrl && (!inSearch || SearchBox.Text.Length == 0):
                ViewModel.SelectAdjacentTab(e.Key == VirtualKey.Left ? -1 : 1);
                e.Handled = true;
                return;

            case VirtualKey.Left or VirtualKey.Right when inSearch && SearchBox.Text.Length == 0:
            case VirtualKey.Left or VirtualKey.Right when inSearch && !shift && ViewModel.Items.Count > 0 && CaretAtEdge(e.Key):
                Select(CardList.SelectedIndex + (e.Key == VirtualKey.Left ? -1 : 1));
                e.Handled = true;
                return;

            case VirtualKey.Down when inSearch:
            case VirtualKey.Tab when inSearch && !shift:
                FocusSelectedCard();
                e.Handled = true;
                return;

            case VirtualKey.Up when !inSearch:
            case VirtualKey.Tab when !inSearch:
            case VirtualKey.F when ctrl:
                SearchBox.Focus(FocusState.Programmatic);
                SearchBox.SelectAll();
                e.Handled = true;
                return;
        }

        if (inSearch) return;

        switch (e.Key)
        {
            case VirtualKey.Delete or VirtualKey.Back:
                DeleteSelected();
                e.Handled = true;
                break;
            case VirtualKey.Z when ctrl:
                UndoDelete();
                e.Handled = true;
                break;
            case VirtualKey.C when ctrl:
                CopySelected();
                e.Handled = true;
                break;
            case VirtualKey.A when ctrl:
                CardList.SelectAll();
                e.Handled = true;
                break;
            case VirtualKey.Home or VirtualKey.Up when ctrl:
            case VirtualKey.Home:
                Select(0);
                FocusSelectedCard();
                e.Handled = true;
                break;
            case VirtualKey.End or VirtualKey.Down when ctrl:
            case VirtualKey.End:
                Select(ViewModel.Items.Count - 1);
                FocusSelectedCard();
                e.Handled = true;
                break;
            case VirtualKey.R when ctrl:
                if (SelectedCards().FirstOrDefault() is { } rename) ShowRenameFlyout(rename);
                e.Handled = true;
                break;
            case VirtualKey.P when ctrl:
                if (SelectedCards().Count > 0) ShowCardMenu(null, default);
                e.Handled = true;
                break;
            case VirtualKey.Space:
                if (SelectedCards().FirstOrDefault() is { } preview) PreviewWindow.Toggle(_host, preview.Item, this);
                e.Handled = true;
                break;
            case VirtualKey.E when ctrl:
                if (SelectedCards().FirstOrDefault() is { } edit) EditWindow.Open(_host, edit.Item, this);
                e.Handled = true;
                break;
        }
    }

    private bool CaretAtEdge(VirtualKey key) =>
        key == VirtualKey.Left ? SearchBox.SelectionStart == 0 && SearchBox.SelectionLength == 0 : false;

    private void Root_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Control) ViewModel.SetBadgesVisible(false);
    }

    /// <summary>Typing anywhere in the panel starts a search.</summary>
    private void Root_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (SearchFocused || char.IsControl(e.Character) || char.IsWhiteSpace(e.Character)) return;
        if (IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu)) return;
        SearchBox.Focus(FocusState.Programmatic);
        e.Handled = true;
        // Replay the key into the box, so a CJK IME composes the whole word ("nihao", not "n" + "ihao");
        // without an IME the key simply types the same character. Wait until the IME has followed the focus.
        var c = e.Character;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (PasteInjector.RetypeKey(c)) return;
            SearchBox.Text += c;
            SearchBox.SelectionStart = SearchBox.Text.Length;
        });
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ViewModel.SearchText == SearchBox.Text) return;
        ViewModel.SearchText = SearchBox.Text;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    // ---------- mouse ----------

    private void CardList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ClipCardViewModel card)
            _ = PasteAsync([card], plainText: IsDown(VirtualKey.Shift));
    }

    private void CardList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not ClipCardViewModel card) return;
        if (!CardList.SelectedItems.Contains(card))
        {
            CardList.SelectedItems.Clear();
            CardList.SelectedItems.Add(card);
        }
        ShowCardMenu(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
        e.Handled = true;
    }

    private void ShowCardMenu(FrameworkElement? anchor, Windows.Foundation.Point position)
    {
        var cards = SelectedCards();
        if (cards.Count == 0) return;
        var first = cards[0];
        var menu = new MenuFlyout();

        menu.Items.Add(Item("Menu_Paste", "\uE77F", () => _ = PasteAsync(cards, false), "Enter"));
        menu.Items.Add(Item("Menu_PastePlain", "\uE8E9", () => _ = PasteAsync(cards, true), "Shift+Enter"));
        menu.Items.Add(Item("Menu_Copy", "\uE8C8", CopySelected, "Ctrl+C"));
        if (cards.Count == 1 && BuildPasteAsMenu(first.Item) is { } pasteAs) menu.Items.Insert(2, pasteAs);
        menu.Items.Add(new MenuFlyoutSeparator());

        var pinTo = new MenuFlyoutSubItem { Text = Loc.Get("Menu_PinTo"), Icon = new FontIcon { Glyph = "\uE718" } };
        foreach (var tab in ViewModel.Tabs.Where(t => !t.IsHistory))
        {
            var board = tab.Pinboard!;
            var entry = new MenuFlyoutItem
            {
                Text = board.Name,
                Icon = new FontIcon { Glyph = "\uEA3B", Foreground = ClipCardViewModel.Brush(PinboardColors.Get(board.ColorIndex)) },
            };
            entry.Click += (_, _) => _host.SetPinboard(cards.Select(c => c.Item), board.Id);
            pinTo.Items.Add(entry);
        }
        if (pinTo.Items.Count > 0) pinTo.Items.Add(new MenuFlyoutSeparator());
        var create = new MenuFlyoutItem { Text = Loc.Get("Menu_NewPinboard"), Icon = new FontIcon { Glyph = "\uE710" } };
        create.Click += (_, _) => ShowPinboardEditor(NewPinboardButton, null, cards);
        pinTo.Items.Add(create);
        menu.Items.Add(pinTo);

        if (cards.Any(c => c.Item.IsPinned))
            menu.Items.Add(Item("Menu_Unpin", "\uE77A", () => _host.SetPinboard(cards.Select(c => c.Item), null)));

        if (cards.Count == 1)
        {
            menu.Items.Add(Item("Menu_Preview", "\uE8A1", () => PreviewWindow.Toggle(_host, first.Item, this), "Space"));
            if (first.Kind is ClipKind.Text or ClipKind.RichText or ClipKind.Link or ClipKind.Color)
                menu.Items.Add(Item("Menu_Edit", "\uE70F", () => EditWindow.Open(_host, first.Item, this), "Ctrl+E"));
            menu.Items.Add(Item("Menu_Rename", "\uE8AC", () => ShowRenameFlyout(first), "Ctrl+R"));

            if (first.Kind == ClipKind.File && first.Item.FilePaths.Count > 0)
                menu.Items.Add(Item("Menu_ShowInExplorer", "\uEC50", () => ShowInExplorer(first.Item.FilePaths[0])));
            if (first.Kind == ClipKind.Link)
                menu.Items.Add(Item("Menu_OpenLink", "\uE8A7", () => _ = Launcher.LaunchUriAsync(new Uri(first.Item.Text!))));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Menu_Delete", "\uE74D", DeleteSelected, "Delete"));

        if (anchor is null)
        {
            var container = CardList.ContainerFromItem(first) as FrameworkElement ?? CardList;
            menu.ShowAt(container, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Top });
        }
        else
        {
            menu.ShowAt(anchor, new FlyoutShowOptions { Position = position });
        }

        static MenuFlyoutItem Item(string key, string glyph, Action action, string? accelerator = null)
        {
            var item = new MenuFlyoutItem { Text = Loc.Get(key), Icon = new FontIcon { Glyph = glyph } };
            if (accelerator is not null) item.KeyboardAcceleratorTextOverride = accelerator;
            item.Click += (_, _) => action();
            return item;
        }
    }

    /// <summary>"Paste as…" choices for one item; null when nothing applies.</summary>
    private MenuFlyoutSubItem? BuildPasteAsMenu(ClipItem item)
    {
        var sub = new MenuFlyoutSubItem { Text = Loc.Get("Menu_PasteAs"), Icon = new FontIcon { Glyph = "" } };

        void Add(string label, string? result, string? hint = null)
        {
            var entry = new MenuFlyoutItem { Text = label, IsEnabled = !string.IsNullOrEmpty(result) };
            if (hint is not null) entry.KeyboardAcceleratorTextOverride = hint;
            if (result is not null) entry.Click += (_, _) => _ = _host.PasteTextAsync(result);
            sub.Items.Add(entry);
        }

        static string Preview(string s)
        {
            var oneLine = s.ReplaceLineEndings(" ");
            return oneLine.Length > 28 ? oneLine[..28] + "…" : oneLine;
        }

        switch (item.Kind)
        {
            case ClipKind.Color:
                foreach (var form in ColorFormats.All(item.Text ?? "")) Add(form, form);
                break;

            case ClipKind.Image:
                if (string.IsNullOrWhiteSpace(item.OcrText)) return null;
                Add(Loc.Get("Menu_PasteOcr"), item.OcrText, Preview(item.OcrText));
                break;

            case ClipKind.File:
                Add(Loc.Get("Menu_PastePath"), string.Join("\r\n", item.FilePaths));
                Add(Loc.Get("Menu_PasteNames"), string.Join("\r\n", item.FilePaths.Select(Path.GetFileName)));
                break;

            default:
                var text = item.Text ?? "";
                if (text.Length == 0) return null;
                var first = true;
                foreach (var group in TextTransforms.Groups)
                {
                    if (!first) sub.Items.Add(new MenuFlyoutSeparator());
                    first = false;
                    foreach (var t in group)
                    {
                        var result = text.Length <= 200_000 ? TextTransforms.Apply(t, text) : null;
                        Add(Loc.Get("Transform_" + t), result == text ? null : result, result is null || result == text ? null : Preview(result));
                    }
                }
                break;
        }
        return sub.Items.Count > 0 ? sub : null;
    }

    private static void ShowInExplorer(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    private void ShowRenameFlyout(ClipCardViewModel card)
    {
        var anchor = CardList.ContainerFromItem(card) as FrameworkElement ?? CardList;
        var box = new TextBox { Text = card.Item.Title ?? "", PlaceholderText = card.DisplayTitle, Width = 220 };
        var flyout = new Flyout { Content = box, Placement = FlyoutPlacementMode.Top };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                _host.Rename(card.Item, box.Text);
                flyout.Hide();
                e.Handled = true;
            }
        };
        flyout.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        flyout.Closed += (_, _) => FocusSelectedCard();
        flyout.ShowAt(anchor);
    }

    // ---------- pinboards ----------

    private void NewPinboard_Click(object sender, RoutedEventArgs e) => ShowPinboardEditor(NewPinboardButton, null, null);

    private void Tab_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PinboardTabViewModel { IsHistory: false } tab } element) return;
        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = Loc.Get("Pinboard_Edit"), Icon = new FontIcon { Glyph = "\uE70F" } };
        edit.Click += (_, _) => ShowPinboardEditor(element, tab.Pinboard, null);
        var delete = new MenuFlyoutItem { Text = Loc.Get("Pinboard_Delete"), Icon = new FontIcon { Glyph = "\uE74D" } };
        delete.Click += (_, _) => ConfirmDeletePinboard(element, tab.Pinboard!);
        menu.Items.Add(edit);
        menu.Items.Add(delete);
        menu.ShowAt(element);
        e.Handled = true;
    }

    /// <summary>Create (<paramref name="existing"/> null) or edit a pinboard; optionally pin <paramref name="pinCards"/> into the new one.</summary>
    private void ShowPinboardEditor(FrameworkElement anchor, Pinboard? existing, IReadOnlyList<ClipCardViewModel>? pinCards)
    {
        var colorIndex = existing?.ColorIndex ?? ViewModel.Tabs.Count % PinboardColors.Palette.Length;
        var name = new TextBox
        {
            Text = existing?.Name ?? "",
            PlaceholderText = Loc.Get("Pinboard_Name"),
            Width = 240,
        };

        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var swatchButtons = new List<Border>();
        for (var i = 0; i < PinboardColors.Palette.Length; i++)
        {
            var index = i;
            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = ClipCardViewModel.Brush(PinboardColors.Palette[i]),
                BorderThickness = new Thickness(2),
            };
            swatch.Tapped += (_, _) =>
            {
                colorIndex = index;
                UpdateSwatches();
            };
            swatchButtons.Add(swatch);
            swatches.Children.Add(swatch);
        }
        void UpdateSwatches()
        {
            for (var i = 0; i < swatchButtons.Count; i++)
                swatchButtons[i].BorderBrush = i == colorIndex
                    ? ThemeManager.Brush("TextFillColorPrimaryBrush")
                    : ClipCardViewModel.Brush(0x00000000);
        }
        UpdateSwatches();

        var save = new Button
        {
            Content = Loc.Get(existing is null ? "Pinboard_Create" : "Common_Save"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = Loc.Get(existing is null ? "Pinboard_NewTitle" : "Pinboard_EditTitle"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(name);
        panel.Children.Add(swatches);
        panel.Children.Add(save);
        var flyout = new Flyout { Content = panel, Placement = FlyoutPlacementMode.Bottom };

        void Commit()
        {
            var text = string.IsNullOrWhiteSpace(name.Text) ? Loc.Get("Pinboard_DefaultName") : name.Text.Trim();
            if (existing is null)
            {
                var id = _host.Repository.AddPinboard(text, colorIndex);
                if (pinCards is not null) _host.SetPinboard(pinCards.Select(c => c.Item), id);
            }
            else
            {
                _host.Repository.UpdatePinboard(existing with { Name = text, ColorIndex = colorIndex });
            }
            ViewModel.ReloadTabs();
            flyout.Hide();
        }

        save.Click += (_, _) => Commit();
        name.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            Commit();
            e.Handled = true;
        };
        flyout.Opened += (_, _) => name.Focus(FocusState.Programmatic);
        flyout.ShowAt(anchor);
    }

    private void ConfirmDeletePinboard(FrameworkElement anchor, Pinboard board)
    {
        var delete = new Button
        {
            Content = Loc.Get("Common_Delete"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var panel = new StackPanel { Spacing = 10, MaxWidth = 280 };
        panel.Children.Add(new TextBlock { Text = Loc.Format("Pinboard_DeleteTitle", board.Name), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = Loc.Get("Pinboard_DeleteBody"), TextWrapping = TextWrapping.Wrap, Foreground = ThemeManager.Brush("TextFillColorSecondaryBrush") });
        panel.Children.Add(delete);
        var flyout = new Flyout { Content = panel };
        delete.Click += async (_, _) =>
        {
            flyout.Hide();
            await Task.Run(() => _host.DeletePinboard(board.Id));
            ViewModel.ReloadTabs();
            _ = ViewModel.ReloadAsync();
        };
        flyout.ShowAt(anchor);
    }

    // ---------- drag out ----------

    private void CardList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var cards = e.Items.OfType<ClipCardViewModel>().ToList();
        if (cards.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        var paths = new List<string>();
        foreach (var card in cards)
        {
            switch (card.Kind)
            {
                case ClipKind.File:
                    paths.AddRange(card.Item.FilePaths);
                    break;
                case ClipKind.Image when card.Item.Blobs.HasFlag(BlobFlags.Png):
                    paths.Add(_host.Blobs.PathFor(card.Item.ContentHash, BlobFlags.Png));
                    break;
            }
        }

        var text = string.Join("\r\n", cards.Where(c => c.Kind != ClipKind.Image && c.Item.Text is not null).Select(c => c.Item.Text));
        if (text.Length > 0) e.Data.SetText(text);
        if (cards.Count == 1 && cards[0].Kind == ClipKind.Link) e.Data.SetWebLink(new Uri(cards[0].Item.Text!));

        if (paths.Count > 0)
        {
            e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
            {
                var deferral = request.GetDeferral();
                try
                {
                    var items = new List<IStorageItem>();
                    foreach (var path in paths)
                    {
                        if (Directory.Exists(path)) items.Add(await StorageFolder.GetFolderFromPathAsync(path));
                        else if (File.Exists(path)) items.Add(await StorageFile.GetFileFromPathAsync(path));
                    }
                    request.SetData(items);
                }
                finally
                {
                    deferral.Complete();
                }
            });
        }
        // In-app marker so cards can be dropped onto pinboard tabs.
        e.Data.Properties[CardIdsKey] = string.Join(',', cards.Select(c => c.Id));
        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Link;
    }

    private const string CardIdsKey = "ClipNest.CardIds";

    private void Tab_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Properties.ContainsKey(CardIdsKey) && sender is FrameworkElement { Tag: PinboardTabViewModel tab } && tab.PinboardId != ViewModel.SelectedTab.PinboardId)
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = tab.IsHistory ? Loc.Get("Menu_Unpin") : Loc.Format("Drag_PinTo", tab.Name);
            e.DragUIOverride.IsGlyphVisible = false;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void Tab_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PinboardTabViewModel tab }) return;
        if (!e.DataView.Properties.TryGetValue(CardIdsKey, out var raw) || raw is not string ids) return;
        var wanted = ids.Split(',').Select(long.Parse).ToHashSet();
        var items = ViewModel.Items.Where(c => wanted.Contains(c.Id)).Select(c => c.Item).ToList();
        _host.SetPinboard(items, tab.PinboardId);
    }

    // ---------- resize grip ----------

    private void ResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _resizing = ((UIElement)sender).CapturePointer(e.Pointer);
        _resizeStartY = DisplayHelper.CursorPosition().Y;
        _resizeStartHeight = _host.Settings.PanelHeight;
        e.Handled = true;
    }

    private void ResizeGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        var dy = (DisplayHelper.CursorPosition().Y - _resizeStartY) / _scale;
        var height = Math.Clamp(Math.Round(_resizeStartHeight - dy), MinPanelHeight, MaxPanelHeight);
        if (Math.Abs(height - _host.Settings.PanelHeight) < 1) return;
        _host.Settings.PanelHeight = height;
        ApplyHeight(height);
    }

    private void ResizeGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        EndResize();
    }

    private void ResizeGrip_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndResize();

    private void EndResize()
    {
        if (!_resizing) return;
        _resizing = false;
        _host.SaveSettings();
    }

    // ---------- search filters ----------

    private static readonly (string Token, ClipKind Kind)[] TypeFilters =
    [
        ("type:text", ClipKind.Text), ("type:rich", ClipKind.RichText), ("type:image", ClipKind.Image),
        ("type:link", ClipKind.Link), ("type:file", ClipKind.File), ("type:color", ClipKind.Color),
    ];

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };

        var types = new MenuFlyoutSubItem { Text = Loc.Get("Filter_Type"), Icon = new FontIcon { Glyph = "\uE8EC" } };
        foreach (var (token, kind) in TypeFilters) types.Items.Add(FilterItem(Loc.KindName(kind), token));
        menu.Items.Add(types);

        var apps = new MenuFlyoutSubItem { Text = Loc.Get("Filter_App"), Icon = new FontIcon { Glyph = "\uE71D" } };
        foreach (var name in _host.Repository.RecentAppNames(12))
        {
            apps.Items.Add(FilterItem(name, "app:" + SearchQuery.EncodeValue(name)));
        }
        if (apps.Items.Count > 0) menu.Items.Add(apps);

        var dates = new MenuFlyoutSubItem { Text = Loc.Get("Filter_Date"), Icon = new FontIcon { Glyph = "\uE787" } };
        dates.Items.Add(FilterItem(Loc.Get("Filter_Today"), "date:today"));
        dates.Items.Add(FilterItem(Loc.Get("Filter_Yesterday"), "date:yesterday"));
        dates.Items.Add(FilterItem(Loc.Get("Filter_7d"), "date:7d"));
        dates.Items.Add(FilterItem(Loc.Get("Filter_30d"), "date:30d"));
        menu.Items.Add(dates);

        if (ViewModel.Tabs.Count > 1)
        {
            var boards = new MenuFlyoutSubItem { Text = Loc.Get("Filter_Pinboard"), Icon = new FontIcon { Glyph = "\uE718" } };
            foreach (var tab in ViewModel.Tabs.Where(t => !t.IsHistory))
            {
                var item = new MenuFlyoutItem { Text = tab.Name };
                item.Click += (_, _) => ViewModel.SelectTab(tab);
                boards.Items.Add(item);
            }
            menu.Items.Add(boards);
        }

        if (SearchBox.Text.Contains(':'))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var clear = new MenuFlyoutItem { Text = Loc.Get("Filter_Clear"), Icon = new FontIcon { Glyph = "\uE894" } };
            clear.Click += (_, _) => SetSearch(string.Join(' ', SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !IsFilterToken(w))));
            menu.Items.Add(clear);
        }

        menu.ShowAt(FilterButton);
    }

    private MenuFlyoutItem FilterItem(string text, string token)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) =>
        {
            // One filter per key: replace an existing "type:"/"app:"/"date:" token.
            var key = token[..(token.IndexOf(':') + 1)];
            var words = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !w.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                .Prepend(token);
            SetSearch(string.Join(' ', words) + " ");
        };
        return item;
    }

    private static bool IsFilterToken(string word) =>
        word.StartsWith("type:", StringComparison.OrdinalIgnoreCase) || word.StartsWith("app:", StringComparison.OrdinalIgnoreCase)
        || word.StartsWith("date:", StringComparison.OrdinalIgnoreCase) || word.StartsWith("pin:", StringComparison.OrdinalIgnoreCase);

    private void SetSearch(string text)
    {
        SearchBox.Text = text;
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectionStart = SearchBox.Text.Length;
    }

    // ---------- top bar ----------

    private void Settings_Click(object sender, RoutedEventArgs e) => _host.OpenSettings();

    private void PausedPill_Click(object sender, RoutedEventArgs e) => _host.Resume();

    private void UpdatePausedPill() =>
        PausedPill.Visibility = _host.IsPaused ? Visibility.Visible : Visibility.Collapsed;
}
