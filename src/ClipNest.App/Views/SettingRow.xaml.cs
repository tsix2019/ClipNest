using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ClipNest.App.Views;

/// <summary>A settings card: icon, header, optional description and a control on the right.</summary>
[ContentProperty(Name = nameof(Action))]
public sealed partial class SettingRow : UserControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata("", OnDescriptionChanged));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    public static readonly DependencyProperty ActionProperty =
        DependencyProperty.Register(nameof(Action), typeof(object), typeof(SettingRow), new PropertyMetadata(null));

    public SettingRow()
    {
        InitializeComponent();
        UpdateDescriptionVisibility();
    }

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SettingRow)d).UpdateDescriptionVisibility();

    private void UpdateDescriptionVisibility()
    {
        if (DescriptionText is not null)
            DescriptionText.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
    }
}
