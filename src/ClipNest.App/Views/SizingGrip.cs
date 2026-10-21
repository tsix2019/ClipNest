using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace ClipNest.App.Views;

/// <summary>Thin strip along the panel's inner edge that shows a resize cursor.</summary>
public sealed partial class SizingGrip : Grid
{
    public SizingGrip() => SetDirection(horizontal: false);

    /// <summary>True: resizes left/right (panel on a side edge); false: up/down.</summary>
    public void SetDirection(bool horizontal) =>
        ProtectedCursor = InputSystemCursor.Create(horizontal ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth);
}
