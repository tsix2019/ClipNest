using ClipNest.App.ViewModels;
using ClipNest.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClipNest.App.Views;

/// <summary>Picks the card template for an item kind; each template gets its own recycle pool.</summary>
public sealed partial class KindTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? ImageTemplate { get; set; }
    public DataTemplate? LinkTemplate { get; set; }
    public DataTemplate? ColorTemplate { get; set; }
    public DataTemplate? FileTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => (item as ClipCardViewModel)?.Kind switch
    {
        ClipKind.Image => ImageTemplate,
        ClipKind.Link => LinkTemplate,
        ClipKind.Color => ColorTemplate,
        ClipKind.File => FileTemplate,
        _ => TextTemplate,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
