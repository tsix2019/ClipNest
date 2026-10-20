namespace ClipNest.Core;

/// <summary>Pixel math for card header colors. Colors are ARGB <see cref="uint"/>; pixel buffers are BGRA.</summary>
public static class ColorMath
{
    public const uint NeutralHeader = 0xFF5A5A5A;
    private const int HueBuckets = 24;

    /// <summary>Picks the most prominent saturated color of an icon; falls back to the average of opaque pixels.</summary>
    public static uint DominantColor(ReadOnlySpan<byte> bgra)
    {
        Span<double> weight = stackalloc double[HueBuckets];
        Span<double> sumR = stackalloc double[HueBuckets];
        Span<double> sumG = stackalloc double[HueBuckets];
        Span<double> sumB = stackalloc double[HueBuckets];
        weight.Clear(); sumR.Clear(); sumG.Clear(); sumB.Clear();
        long avgR = 0, avgG = 0, avgB = 0, opaque = 0;

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128) continue;
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            avgR += r; avgG += g; avgB += b; opaque++;

            var (h, s, v) = ToHsv(r, g, b);
            if (s < 0.15 || v < 0.15) continue;
            var bucket = Math.Min(HueBuckets - 1, (int)(h / 360.0 * HueBuckets));
            var w = s * v;
            weight[bucket] += w;
            sumR[bucket] += r * w; sumG[bucket] += g * w; sumB[bucket] += b * w;
        }

        if (opaque == 0) return NeutralHeader;

        var best = -1;
        for (var i = 0; i < HueBuckets; i++)
        {
            if (weight[i] > 0 && (best < 0 || weight[i] > weight[best])) best = i;
        }

        if (best < 0)
            return Argb((byte)(avgR / opaque), (byte)(avgG / opaque), (byte)(avgB / opaque));

        return Argb(
            (byte)Math.Round(sumR[best] / weight[best]),
            (byte)Math.Round(sumG[best] / weight[best]),
            (byte)Math.Round(sumB[best] / weight[best]));
    }

    /// <summary>True when black text reads better than white text on <paramref name="argb"/>.</summary>
    public static bool PrefersDarkText(uint argb) =>
        Contrast(argb, 0xFF000000) > Contrast(argb, 0xFFFFFFFF);

    /// <summary>Darkens or lightens a header color until its best text color reaches WCAG 4.5:1.</summary>
    public static uint EnsureHeaderContrast(uint argb)
    {
        var c = argb | 0xFF000000;
        for (var step = 0; step < 20; step++)
        {
            var dark = PrefersDarkText(c);
            if (Contrast(c, dark ? 0xFF000000 : 0xFFFFFFFF) >= 4.5) return c;
            c = dark ? Scale(c, 1.08) : Scale(c, 0.9);
        }
        return c;
    }

    public static double Contrast(uint a, uint b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static double Luminance(uint argb)
    {
        static double Lin(uint c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin((argb >> 16) & 0xFF) + 0.7152 * Lin((argb >> 8) & 0xFF) + 0.0722 * Lin(argb & 0xFF);
    }

    public static uint Argb(byte r, byte g, byte b, byte a = 0xFF) =>
        ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    private static uint Scale(uint argb, double factor)
    {
        static byte Ch(uint v, double f) => (byte)Math.Clamp(Math.Round(f >= 1 ? v + (255 - v) * (f - 1) * 2 : v * f), 0, 255);
        return Argb(Ch((argb >> 16) & 0xFF, factor), Ch((argb >> 8) & 0xFF, factor), Ch(argb & 0xFF, factor));
    }

    private static (double h, double s, double v) ToHsv(int r, int g, int b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max / 255.0);
    }
}
