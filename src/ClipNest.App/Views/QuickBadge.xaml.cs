using ClipNest.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClipNest.App.Views;

/// <summary>"1"–"9" quick-paste badge shown while Ctrl is held.</summary>
public sealed partial class QuickBadge : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(ClipCardViewModel), typeof(QuickBadge), new PropertyMetadata(null));

    public QuickBadge() => InitializeComponent();

    public ClipCardViewModel? Item
    {
        get => (ClipCardViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }
}
