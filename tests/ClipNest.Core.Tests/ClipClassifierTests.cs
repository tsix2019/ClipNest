using ClipNest.Core;

namespace ClipNest.Core.Tests;

public class ClipClassifierTests
{
    [Theory]
    [InlineData("#ff8800", true, "#FF8800")]
    [InlineData("ff88aa", true, "#FF88AA")]
    [InlineData("#FF8800CC", true, "#FF8800CC")]
    [InlineData("235442", false, null)]
    [InlineData("#235442", true, "#235442")]
    [InlineData("#FFF", false, null)]
    [InlineData("rgb(1,2,3)", false, null)]
    [InlineData("red", false, null)]
    [InlineData(" #00aa00 ", true, "#00AA00")]
    [InlineData("#00aa0", false, null)]
    [InlineData("deadbeefcafe", false, null)]
    public void TryParseColor(string input, bool ok, string? hex)
    {
        Assert.Equal(ok, ClipClassifier.TryParseColor(input, out var h));
        if (ok) Assert.Equal(hex, h);
    }

    [Theory]
    [InlineData("https://example.com/a?b=1", true)]
    [InlineData("http://x.cn", true)]
    [InlineData("  https://example.com  ", true)]
    [InlineData("see https://example.com", false)]
    [InlineData("ftp://x", false)]
    [InlineData("https://exa mple.com", false)]
    [InlineData("https://", false)]
    public void IsUrl(string s, bool ok) => Assert.Equal(ok, ClipClassifier.IsUrl(s));

    [Fact]
    public void Files_win_over_text() =>
        Assert.Equal(ClipKind.File, ClipClassifier.Classify(new ClipboardSnapshot { Files = ["C:\\a.txt"], Text = "x" }));

    [Fact]
    public void Image_with_html_only_is_image() =>
        Assert.Equal(ClipKind.Image, ClipClassifier.Classify(new ClipboardSnapshot { Png = [1], Html = "<img>" }));

    [Fact]
    public void Image_with_text_and_rtf_is_rich_text() =>
        Assert.Equal(ClipKind.RichText, ClipClassifier.Classify(new ClipboardSnapshot { Png = [1], Text = "hello", Rtf = "{\\rtf1}" }));

    [Fact]
    public void Link_even_with_html() =>
        Assert.Equal(ClipKind.Link, ClipClassifier.Classify(new ClipboardSnapshot { Text = " https://a.com ", Html = "<a>" }));

    [Fact]
    public void Color() =>
        Assert.Equal(ClipKind.Color, ClipClassifier.Classify(new ClipboardSnapshot { Text = "#AABBCC" }));

    [Fact]
    public void Rich() =>
        Assert.Equal(ClipKind.RichText, ClipClassifier.Classify(new ClipboardSnapshot { Text = "a", Html = "<b>a</b>" }));

    [Fact]
    public void Plain() =>
        Assert.Equal(ClipKind.Text, ClipClassifier.Classify(new ClipboardSnapshot { Text = "a" }));

    [Fact]
    public void Empty_snapshot_is_null() =>
        Assert.Null(ClipClassifier.TryClassify(new ClipboardSnapshot()));

    [Fact]
    public void Whitespace_text_is_null() =>
        Assert.Null(ClipClassifier.TryClassify(new ClipboardSnapshot { Text = "  \r\n " }));
}
