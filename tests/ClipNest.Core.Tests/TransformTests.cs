using System.Globalization;

namespace ClipNest.Core.Tests;

public class TextTransformTests
{
    [Theory]
    [InlineData(TextTransform.Upper, "Hello wörld", "HELLO WÖRLD")]
    [InlineData(TextTransform.Lower, "Hello", "hello")]
    [InlineData(TextTransform.Title, "hello WORLD", "Hello World")]
    [InlineData(TextTransform.Sentence, "HELLO. how ARE you? fine", "Hello. How are you? Fine")]
    [InlineData(TextTransform.Camel, "hello world_foo-bar", "helloWorldFooBar")]
    [InlineData(TextTransform.Pascal, "user id", "UserId")]
    [InlineData(TextTransform.Snake, "parseHTTPResponse", "parse_http_response")]
    [InlineData(TextTransform.Kebab, "MyComponentName", "my-component-name")]
    [InlineData(TextTransform.Trim, "  a  \r\n b  \n\n", "a\n b")]
    [InlineData(TextTransform.SingleLine, "a\r\n  b\tc", "a b c")]
    [InlineData(TextTransform.UniqueLines, "a\nb\na", "a\nb")]
    [InlineData(TextTransform.JsonMinify, "{ \"a\" : [1, 2] }", "{\"a\":[1,2]}")]
    [InlineData(TextTransform.UrlEncode, "a b&c=中", "a%20b%26c%3D%E4%B8%AD")]
    [InlineData(TextTransform.UrlDecode, "a%20b+c%E4%B8%AD", "a b c中")]
    [InlineData(TextTransform.Base64Encode, "剪贴板", "5Ymq6LS05p2/")]
    [InlineData(TextTransform.Base64Decode, "5Ymq6LS05p2_", "剪贴板")]
    [InlineData(TextTransform.HtmlDecode, "&lt;b&gt; &amp; &#20013;", "<b> & 中")]
    public void Applies(TextTransform t, string input, string expected) => Assert.Equal(expected, TextTransforms.Apply(t, input));

    [Fact]
    public void Json_pretty_keeps_unicode_and_indents()
    {
        var result = TextTransforms.Apply(TextTransform.JsonPretty, "{\"名\":\"值\",\"n\":[1]}")!;
        Assert.Contains("\"名\": \"值\"", result);
        Assert.Contains("\n", result);
    }

    [Fact] public void Invalid_json_is_null() => Assert.Null(TextTransforms.Apply(TextTransform.JsonPretty, "{nope"));
    [Fact] public void Invalid_base64_is_null() => Assert.Null(TextTransforms.Apply(TextTransform.Base64Decode, "%%%"));
    [Fact] public void Binary_base64_is_null() => Assert.Null(TextTransforms.Apply(TextTransform.Base64Decode, "/w=="));

    [Fact]
    public void Sort_lines()
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("apple\nbanana\ncherry", TextTransforms.Apply(TextTransform.SortLines, "cherry\r\napple\nbanana"));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    [Fact]
    public void Every_transform_is_in_a_menu_group() =>
        Assert.Equal(Enum.GetValues<TextTransform>().Order(), TextTransforms.Groups.SelectMany(g => g).Order());
}

public class SecretDetectorTests
{
    [Theory]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nabc\n-----END OPENSSH PRIVATE KEY-----")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("token: ghp_aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789")]
    [InlineData("sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789ABCD")]
    [InlineData("xoxb-1234567890-abcdefghij")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
    public void Detects(string text) => Assert.True(SecretDetector.LooksSecret(text));

    [Theory]
    [InlineData("hello world")]
    [InlineData("https://github.com/tsix2019/ClipNest")]
    [InlineData("sk-short")]
    [InlineData("AKIA is an acronym")]
    [InlineData("")]
    public void Ignores(string text) => Assert.False(SecretDetector.LooksSecret(text));
}

public class PlaceholderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 5, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Expands_known_placeholders()
    {
        var s = Placeholders.Expand("Date {date}, {time}; {DATETIME}; {date:yyyyMMdd} | {clipboard}", Now, () => "CLIP", CultureInfo.InvariantCulture);
        Assert.Equal("Date 2026-10-21, 09:05; 2026-10-21 09:05; 20261021 | CLIP", s);
    }

    [Fact]
    public void Unknown_and_escaped_are_literal()
    {
        Assert.Equal("{name} {date}", Placeholders.Expand("{name} {{date}}", Now, () => null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Clipboard_read_lazily_once()
    {
        var calls = 0;
        Placeholders.Expand("{clipboard}{clipboard}", Now, () => { calls++; return "x"; });
        Assert.Equal(1, calls);
        Placeholders.Expand("no placeholders", Now, () => { calls++; return "x"; });
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Detects_presence()
    {
        Assert.True(Placeholders.HasAny("Hi {date}"));
        Assert.False(Placeholders.HasAny("Hi {name}"));
        Assert.False(Placeholders.HasAny(null));
    }
}

public class ColorFormatTests
{
    [Fact]
    public void All_forms()
    {
        var forms = ColorFormats.All("#FF8000");
        Assert.Equal(["#FF8000", "#ff8000", "rgb(255, 128, 0)", "hsl(30, 100%, 50%)"], forms);
    }

    [Fact]
    public void Translucent_adds_rgba() => Assert.Contains("rgba(0, 0, 255, 0.5)", ColorFormats.All("#0000FF80"));

    [Fact]
    public void Invalid_is_empty() => Assert.Empty(ColorFormats.All("nope"));
}
