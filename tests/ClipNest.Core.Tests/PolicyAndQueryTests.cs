using ClipNest.Core;

namespace ClipNest.Core.Tests;

public class RetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact] public void Month_cutoff() => Assert.Equal(Now.AddMonths(-1), RetentionPolicy.Cutoff(RetentionPeriod.Month, Now));
    [Fact] public void Day_cutoff() => Assert.Equal(Now.AddDays(-1), RetentionPolicy.Cutoff(RetentionPeriod.Day, Now));
    [Fact] public void Forever_null() => Assert.Null(RetentionPolicy.Cutoff(RetentionPeriod.Forever, Now));

    [Theory]
    [InlineData(RetentionPeriod.Week, RetentionPeriod.Day, true)]
    [InlineData(RetentionPeriod.Forever, RetentionPeriod.Year, true)]
    [InlineData(RetentionPeriod.Day, RetentionPeriod.Week, false)]
    [InlineData(RetentionPeriod.Month, RetentionPeriod.Month, false)]
    public void Shrinking_detected(RetentionPeriod from, RetentionPeriod to, bool shrinks) =>
        Assert.Equal(shrinks, RetentionPolicy.IsShrinking(from, to));
}

public class SearchQueryTests
{
    [Fact]
    public void Parse_tokens()
    {
        var now = new DateTimeOffset(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);
        var q = SearchQuery.Parse("type:image app:chrome  hello world date:7d", now);
        Assert.Equal("hello world", q.Text);
        Assert.Equal(ClipKind.Image, q.Kind);
        Assert.Equal("chrome", q.App);
        Assert.Equal(now.AddDays(-7), q.Since);
    }

    [Fact]
    public void Parse_today_is_local_midnight()
    {
        var now = new DateTimeOffset(2026, 10, 21, 15, 0, 0, TimeSpan.FromHours(8));
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.FromHours(8)), SearchQuery.Parse("date:today", now).Since);
    }

    [Fact]
    public void Parse_yesterday_has_until()
    {
        var now = new DateTimeOffset(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);
        var q = SearchQuery.Parse("date:yesterday", now);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero), q.Since);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero), q.Until);
    }

    [Fact] public void Unknown_token_is_text() => Assert.Equal("foo:bar", SearchQuery.Parse("foo:bar", DateTimeOffset.Now).Text);
    [Fact] public void Bad_type_value_is_text() => Assert.Equal("type:banana", SearchQuery.Parse("type:banana", DateTimeOffset.Now).Text);
    [Fact] public void Chinese_type_alias() => Assert.Equal(ClipKind.Image, SearchQuery.Parse("type:图片", DateTimeOffset.Now).Kind);
    [Fact] public void Pin_token() => Assert.Equal("work", SearchQuery.Parse("pin:work", DateTimeOffset.Now).Pinboard);
    [Fact] public void Empty() => Assert.True(SearchQuery.Parse("   ", DateTimeOffset.Now).IsEmpty);
    [Fact] public void Not_empty_with_filter_only() => Assert.False(SearchQuery.Parse("type:link", DateTimeOffset.Now).IsEmpty);
}

public class RelativeTimeTests
{
    [Theory]
    [InlineData(30, "Time_JustNow", 0)]
    [InlineData(150, "Time_Minutes", 2)]
    [InlineData(7200, "Time_Hours", 2)]
    public void Relative(int secondsAgo, string key, int n)
    {
        var now = new DateTimeOffset(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(new RelativeTimeValue(key, n), RelativeTime.Describe(now.AddSeconds(-secondsAgo), now));
    }

    [Fact]
    public void Yesterday()
    {
        var now = new DateTimeOffset(2026, 10, 21, 1, 0, 0, TimeSpan.Zero);
        Assert.Equal("Time_Yesterday", RelativeTime.Describe(now.AddHours(-5), now).Key);
    }

    [Fact]
    public void Days_then_date()
    {
        var now = new DateTimeOffset(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal(new RelativeTimeValue("Time_Days", 3), RelativeTime.Describe(now.AddDays(-3), now));
        Assert.Equal("Time_Date", RelativeTime.Describe(now.AddDays(-30), now).Key);
    }
}

public class HotkeyGestureTests
{
    [Fact]
    public void Roundtrip()
    {
        Assert.True(HotkeyGesture.TryParse("ctrl + shift + v", out var g));
        Assert.Equal("Ctrl+Shift+V", g.ToString());
        Assert.Equal(0x56, g.Key);
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, g.Modifiers);
    }

    [Fact] public void Requires_modifier() => Assert.False(HotkeyGesture.TryParse("V", out _));
    [Fact] public void Function_keys() { Assert.True(HotkeyGesture.TryParse("Alt+F9", out var g)); Assert.Equal(0x78, g.Key); Assert.Equal("Alt+F9", g.ToString()); }
    [Fact] public void Digits_and_space() { Assert.True(HotkeyGesture.TryParse("Win+Space", out var g)); Assert.Equal(0x20, g.Key); Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+1", out var d)); Assert.Equal(0x31, d.Key); }
    [Fact] public void Modifier_order_is_canonical() { Assert.True(HotkeyGesture.TryParse("Shift+Win+Alt+Ctrl+K", out var g)); Assert.Equal("Ctrl+Alt+Shift+Win+K", g.ToString()); }
}

public class ColorMathTests
{
    [Fact]
    public void Dominant_ignores_transparent()
    {
        // BGRA: two opaque red pixels, one fully transparent blue pixel
        byte[] px = [0, 0, 255, 255, 0, 0, 255, 255, 255, 0, 0, 0];
        Assert.Equal(0xFFFF0000u, ColorMath.DominantColor(px));
    }

    [Fact]
    public void Dominant_prefers_saturated_bucket()
    {
        var px = new List<byte>();
        for (var i = 0; i < 10; i++) px.AddRange([0, 200, 0, 255]);      // green
        for (var i = 0; i < 30; i++) px.AddRange([240, 240, 240, 255]);  // near-white (unsaturated)
        Assert.Equal(0xFF00C800u, ColorMath.DominantColor(px.ToArray()));
    }

    [Fact]
    public void All_gray_falls_back_to_average()
    {
        byte[] px = [100, 100, 100, 255, 120, 120, 120, 255];
        Assert.Equal(0xFF6E6E6Eu, ColorMath.DominantColor(px));
    }

    [Fact] public void Empty_is_neutral() => Assert.Equal(ColorMath.NeutralHeader, ColorMath.DominantColor([]));
    [Fact] public void Yellow_prefers_dark_text() => Assert.True(ColorMath.PrefersDarkText(0xFFFFEB3Bu));
    [Fact] public void Navy_prefers_light_text() => Assert.False(ColorMath.PrefersDarkText(0xFF1A237Eu));

    [Theory]
    [InlineData(0xFFFFEB3Bu)]
    [InlineData(0xFF1A237Eu)]
    [InlineData(0xFF808080u)]
    [InlineData(0xFF00C800u)]
    public void Header_contrast_at_least_4_5(uint argb)
    {
        var c = ColorMath.EnsureHeaderContrast(argb);
        var text = ColorMath.PrefersDarkText(c) ? 0xFF000000u : 0xFFFFFFFFu;
        Assert.True(ColorMath.Contrast(c, text) >= 4.5, $"{c:X8}");
    }
}

public class FilterEncodingTests
{
    [Fact]
    public void App_value_with_spaces_roundtrips()
    {
        var token = "app:" + SearchQuery.EncodeValue("Microsoft Edge");
        Assert.Equal("app:Microsoft_Edge", token);
        Assert.Equal("Microsoft Edge", SearchQuery.Parse(token + " hello", DateTimeOffset.Now).App);
    }

    [Theory]
    [InlineData("Ctrl+Shift+V", true)]
    [InlineData("Win+V", true)]
    [InlineData("Alt+Shift+C", true)]
    [InlineData("Shift+A", false)]
    [InlineData("Alt+F4", false)]
    [InlineData("Alt+Tab", false)]
    [InlineData("Alt+Space", false)]
    [InlineData("Ctrl+Alt+F4", true)]
    public void Safe_global_hotkeys(string text, bool safe)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(safe, g.IsSafeGlobal);
    }
}
