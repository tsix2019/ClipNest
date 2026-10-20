namespace ClipNest.Core;

/// <summary>A named, colored collection of pinned items. <see cref="ColorIndex"/> indexes <see cref="PinboardColors.Palette"/>.</summary>
public sealed record Pinboard(long Id, string Name, int ColorIndex, int SortOrder);

public static class PinboardColors
{
    /// <summary>ARGB palette for pinboard dots (red, orange, yellow, green, teal, blue, purple, gray).</summary>
    public static readonly uint[] Palette =
    [
        0xFFFF5F57, 0xFFFF9F0A, 0xFFFFD60A, 0xFF32D74B,
        0xFF40C8E0, 0xFF0A84FF, 0xFFBF5AF2, 0xFF8E8E93,
    ];

    public static uint Get(int index) => Palette[(uint)index % (uint)Palette.Length];
}
