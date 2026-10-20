using System.Collections.ObjectModel;
using ClipNest.Core;
using ClipNest.Core.Abstractions;
using ClipNest.Data;
using Microsoft.UI.Xaml;

namespace ClipNest.App.ViewModels;

/// <summary>One tab in the panel's top bar: the history ("Clipboard") or a pinboard.</summary>
public sealed partial class PinboardTabViewModel(PanelViewModel owner, Pinboard? pinboard) : ObservableObject
{
    private bool _isSelected;

    public Pinboard? Pinboard { get; private set; } = pinboard;
    public long? PinboardId => Pinboard?.Id;
    public bool IsHistory => Pinboard is null;
    public string Name => Pinboard?.Name ?? Loc.Get("Panel_History");

    public Microsoft.UI.Xaml.Media.SolidColorBrush DotBrush =>
        ClipCardViewModel.Brush(Pinboard is null ? 0x00000000u : PinboardColors.Get(Pinboard.ColorIndex));

    public Visibility DotVisibility => IsHistory ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HistoryIconVisibility => IsHistory ? Visibility.Visible : Visibility.Collapsed;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) OnPropertyChanged(nameof(Background));
        }
    }

    public Microsoft.UI.Xaml.Media.Brush Background => IsSelected
        ? ThemeManager.Brush("TabSelectedBrush")
        : ClipCardViewModel.Brush(0x00000000);

    public void Select() => owner.SelectTab(this);

    public void RefreshTheme() => OnPropertyChanged(nameof(Background));

    public void Update(Pinboard pinboard)
    {
        Pinboard = pinboard;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DotBrush));
    }
}

/// <summary>State of the bottom panel: tabs, search, and the card list.</summary>
public sealed partial class PanelViewModel : ObservableObject
{
    private const int PageSize = 60;

    private readonly AppHost _host;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _ui;
    private string _searchText = "";
    private PinboardTabViewModel _selectedTab;
    private int _loadVersion;
    private bool _hasMore;
    private bool _loadingMore;
    private bool _loaded;

    public PanelViewModel(AppHost host)
    {
        _host = host;
        _ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _selectedTab = new PinboardTabViewModel(this, null) { IsSelected = true };
        Tabs.Add(_selectedTab);
        ReloadTabs();
        ThemeManager.Changed += () =>
        {
            foreach (var tab in Tabs) tab.RefreshTheme();
        };
    }

    public ObservableCollection<ClipCardViewModel> Items { get; } = [];
    public ObservableCollection<PinboardTabViewModel> Tabs { get; } = [];
    public CardMetrics Metrics { get; } = new();

    public PinboardTabViewModel SelectedTab => _selectedTab;

    /// <summary>Raised after the item list was replaced (not appended); the view resets selection/scroll.</summary>
    public event Action? ItemsReset;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) OnPropertyChanged(nameof(IsSearching));
        }
    }

    public bool IsSearching => !string.IsNullOrWhiteSpace(_searchText);

    public Visibility EmptyVisibility => Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string EmptyText => IsSearching
        ? Loc.Get("Panel_NoResults")
        : _selectedTab.IsHistory ? Loc.Get("Panel_Empty") : Loc.Get("Panel_EmptyPinboard");

    public bool IsLoaded => _loaded;

    // ---------- tabs ----------

    public void ReloadTabs()
    {
        var boards = _host.Repository.GetPinboards();
        var selectedId = _selectedTab.PinboardId;
        while (Tabs.Count > 1) Tabs.RemoveAt(Tabs.Count - 1);
        foreach (var b in boards) Tabs.Add(new PinboardTabViewModel(this, b));

        var selected = Tabs.FirstOrDefault(t => t.PinboardId == selectedId) ?? Tabs[0];
        if (!ReferenceEquals(selected, _selectedTab)) SelectTab(selected);
        else selected.IsSelected = true;
    }

    public void SelectTab(PinboardTabViewModel tab)
    {
        if (ReferenceEquals(tab, _selectedTab) && _loaded) return;
        _selectedTab.IsSelected = false;
        _selectedTab = tab;
        tab.IsSelected = true;
        OnPropertyChanged(nameof(SelectedTab));
        _ = ReloadAsync();
    }

    public void SelectAdjacentTab(int delta)
    {
        var index = Tabs.IndexOf(_selectedTab);
        var next = (index + delta + Tabs.Count) % Tabs.Count;
        SelectTab(Tabs[next]);
    }

    // ---------- loading ----------

    private ClipQuery BuildQuery(int offset) =>
        new(SearchQuery.Parse(_searchText, DateTimeOffset.Now), _selectedTab.PinboardId, offset, PageSize);

    /// <summary>Replaces the list with the first page of the current tab/search.</summary>
    public async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        var query = BuildQuery(0);
        var items = await Task.Run(() => _host.Repository.Query(query));
        if (version != _loadVersion) return; // a newer search started meanwhile

        Items.Clear();
        foreach (var item in items) Items.Add(new ClipCardViewModel(item, _host, Metrics));
        _hasMore = items.Count == PageSize;
        _loaded = true;
        RenumberBadges();
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(EmptyText));
        ItemsReset?.Invoke();
    }

    /// <summary>Appends the next page; called when scrolling near the end.</summary>
    public async Task LoadMoreAsync()
    {
        if (!_hasMore || _loadingMore) return;
        _loadingMore = true;
        try
        {
            var version = _loadVersion;
            var query = BuildQuery(Items.Count);
            var items = await Task.Run(() => _host.Repository.Query(query));
            if (version != _loadVersion) return;
            var known = Items.Select(i => i.Id).ToHashSet();
            foreach (var item in items.Where(i => !known.Contains(i.Id))) Items.Add(new ClipCardViewModel(item, _host, Metrics));
            _hasMore = items.Count == PageSize;
        }
        finally
        {
            _loadingMore = false;
        }
    }

    /// <summary>Releases cards (and their bitmaps) while the panel is hidden for a while.</summary>
    public void Unload()
    {
        _loadVersion++;
        Items.Clear();
        _loaded = false;
        ClipCardViewModel.ClearSharedCaches();
    }

    // ---------- live updates ----------

    /// <summary>A copy was recorded (new or moved to front).</summary>
    public void OnIngested(IngestResult result)
    {
        if (!_loaded) return;
        if (IsSearching)
        {
            _ = ReloadAsync();
            return;
        }

        var existing = Items.FirstOrDefault(i => i.Id == result.Item.Id);
        var belongs = _selectedTab.IsHistory || result.Item.PinboardId == _selectedTab.PinboardId;

        if (existing is not null)
        {
            existing.Update(result.Item);
            var index = Items.IndexOf(existing);
            if (index > 0) Items.Move(index, 0);
        }
        else if (belongs)
        {
            Items.Insert(0, new ClipCardViewModel(result.Item, _host, Metrics));
        }
        RenumberBadges();
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    public void OnItemChanged(ClipItem item)
    {
        var vm = Items.FirstOrDefault(i => i.Id == item.Id);
        if (vm is null)
        {
            // pinned into the pinboard currently shown
            if (_loaded && !IsSearching && !_selectedTab.IsHistory && item.PinboardId == _selectedTab.PinboardId) _ = ReloadAsync();
            return;
        }
        if (!_selectedTab.IsHistory && item.PinboardId != _selectedTab.PinboardId)
        {
            Remove(vm);
            return;
        }
        vm.Update(item);

        // Used again (pasted): history is ordered by recency, so it moves to the front.
        if (!IsSearching && Items.Count > 0 && Items[0] != vm && item.CreatedAt >= Items[0].Item.CreatedAt) MoveToFront(vm);
    }

    public void MoveToFront(ClipCardViewModel vm)
    {
        var index = Items.IndexOf(vm);
        if (index > 0) Items.Move(index, 0);
        RenumberBadges();
    }

    public void Remove(ClipCardViewModel vm)
    {
        Items.Remove(vm);
        RenumberBadges();
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(EmptyText));
    }

    public void RefreshTimes()
    {
        var now = DateTimeOffset.Now;
        foreach (var item in Items) item.RefreshTime(now);
    }

    public void SetBadgesVisible(bool visible)
    {
        for (var i = 0; i < Math.Min(9, Items.Count); i++) Items[i].ShowBadge = visible;
    }

    private void RenumberBadges()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].QuickIndex = i < 9 ? i + 1 : 0;
            if (i >= 9) Items[i].ShowBadge = false;
        }
    }
}
