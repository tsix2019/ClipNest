using ClipNest.Core;

namespace ClipNest.Core.Tests;

public class ContentHasherTests
{
    [Fact]
    public void Crlf_and_lf_hash_equal() =>
        Assert.Equal(
            ContentHasher.Compute(ClipKind.Text, new ClipboardSnapshot { Text = "a\r\nb" }),
            ContentHasher.Compute(ClipKind.Text, new ClipboardSnapshot { Text = "a\nb" }));

    [Fact]
    public void Kind_is_part_of_hash() =>
        Assert.NotEqual(
            ContentHasher.Compute(ClipKind.Text, new ClipboardSnapshot { Text = "a" }),
            ContentHasher.Compute(ClipKind.RichText, new ClipboardSnapshot { Text = "a" }));

    [Fact]
    public void File_order_and_case_irrelevant() =>
        Assert.Equal(
            ContentHasher.Compute(ClipKind.File, new ClipboardSnapshot { Files = ["b", "a"] }),
            ContentHasher.Compute(ClipKind.File, new ClipboardSnapshot { Files = ["A", "B"] }));

    [Fact]
    public void Image_hash_uses_png() =>
        Assert.NotEqual(
            ContentHasher.Compute(ClipKind.Image, new ClipboardSnapshot { Png = [1, 2] }),
            ContentHasher.Compute(ClipKind.Image, new ClipboardSnapshot { Png = [1, 3] }));

    [Fact]
    public void Hash_is_64_lower_hex() =>
        Assert.Matches("^[0-9a-f]{64}$", ContentHasher.Compute(ClipKind.Text, new ClipboardSnapshot { Text = "x" }));

    [Fact]
    public void Text_hash_helper_matches_compute() =>
        Assert.Equal(
            ContentHasher.Compute(ClipKind.Text, new ClipboardSnapshot { Text = "héllo\r\n" }),
            ContentHasher.ForText(ClipKind.Text, "héllo\n"));
}
