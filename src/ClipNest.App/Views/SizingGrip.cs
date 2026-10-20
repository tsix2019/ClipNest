using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace ClipNest.App.Views;

/// <summary>Thin strip along the panel's top edge that shows a resize cursor.</summary>
public sealed partial class SizingGrip : Border
{
    public SizingGrip() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
}
