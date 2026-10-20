using ClipNest.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClipNest.App.Views;

/// <summary>Colored card header: title, relative time and source-app icon.</summary>
public sealed partial class CardHeader : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(ClipCardViewModel), typeof(CardHeader), new PropertyMetadata(null));

    public CardHeader() => InitializeComponent();

    public ClipCardViewModel? Item
    {
        get => (ClipCardViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }
}
