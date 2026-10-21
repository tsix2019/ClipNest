using Microsoft.UI.Xaml;

namespace ClipNest.App.ViewModels;

/// <summary>Card dimensions shared by every card; derived from the panel height.</summary>
public sealed partial class CardMetrics : ObservableObject
{
    public const double TopBarHeight = 52;
    public const double VerticalPadding = 32; // list padding (8 + 14) + selection ring room (2 × 3) + slack
    public const double CompactThreshold = 260;

    private double _width = 236;
    private double _height = 236;
    private bool _compact;

    public double Width { get => _width; private set => SetProperty(ref _width, value); }
    public double Height { get => _height; private set => SetProperty(ref _height, value); }

    public bool Compact
    {
        get => _compact;
        private set
        {
            if (SetProperty(ref _compact, value))
            {
                OnPropertyChanged(nameof(FooterVisibility));
                OnPropertyChanged(nameof(IconVisibility));
                OnPropertyChanged(nameof(BodyCornerRadius));
                OnPropertyChanged(nameof(TextMaxLines));
            }
        }
    }

    public Visibility FooterVisibility => Compact ? Visibility.Collapsed : Visibility.Visible;
    public Visibility IconVisibility => Compact ? Visibility.Collapsed : Visibility.Visible;
    public CornerRadius BodyCornerRadius => Compact ? new CornerRadius(0, 0, 12, 12) : new CornerRadius(0);
    public int TextMaxLines => Compact ? 4 : 9;

    /// <summary>Space the card list takes around each card along the scrolling axis (container margin + ring room).</summary>
    public const double HorizontalStride = 20; // 7 + 7 margin, 3 + 3 presenter padding
    public const double VerticalStride = 16;   // 5 + 5 margin, 3 + 3 presenter padding

    /// <summary>Card list padding inside a left/right panel, left + right (see PanelWindow.ApplyEdge).</summary>
    public const double VerticalListPadding = 28;

    /// <summary>Recomputes card size for a left/right panel <paramref name="panelWidth"/> DIPs wide.</summary>
    public void UpdateVertical(double panelWidth)
    {
        Compact = false;
        Width = Math.Max(160, panelWidth - VerticalListPadding - 6 - 2);
        Height = Math.Clamp(Math.Round(Width * 0.62), 150, 240);
    }

    /// <summary>Recomputes card size for a panel of <paramref name="panelHeight"/> DIPs.</summary>
    public void Update(double panelHeight)
    {
        Compact = panelHeight < CompactThreshold;
        var side = Math.Max(120, panelHeight - TopBarHeight - VerticalPadding);
        Height = side;
        Width = Compact ? Math.Round(side * 1.25) : side;
    }
}
