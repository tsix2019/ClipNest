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
        if (_shown)
        {
            Activate();
            WindowStyler.BringToFront(_hwnd);
            return;
        }

        _host.Injector.CaptureTarget();
        _unloadTimer.Stop();
        _hiding = false;
        HideStoryboard.Stop();

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

        PositionOnCursorMonitor();
        Root.Opacity = 0;
        _shown = true;
        AppWindow.Show(true);
        Activate();
        WindowStyler.BringToFront(_hwnd);
        ShowStoryboard.Begin();
        _clockTimer.Start();
        // The list may realize its containers only now (first show): select + focus once layout ran.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (CardList.SelectedIndex < 0 && ViewModel.Items.Count > 0) CardList.SelectedIndex = 0;
            FocusSelectedCard();
        });
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

            case VirtualKey.Left or VirtualKey.Right when ctrl:
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
        SearchBox.Text += e.Character;
        SearchBox.SelectionStart = SearchBox.Text.Length;
        e.Handled = true;
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
        delete.Click += (_, _) =>
        {
            foreach (var hash in _host.Repository.DeletePinboard(board.Id)) _host.Blobs.DeleteAll(hash);
            flyout.Hide();
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
        e.Data.RequestedOperation = DataPackageOperation.Copy;
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

    // ---------- top bar ----------

    private void Settings_Click(object sender, RoutedEventArgs e) => _host.OpenSettings();

    private void PausedPill_Click(object sender, RoutedEventArgs e) => _host.Resume();

    private void UpdatePausedPill() =>
        PausedPill.Visibility = _host.IsPaused ? Visibility.Visible : Visibility.Collapsed;
}
