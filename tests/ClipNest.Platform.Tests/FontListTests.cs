using ClipNest.Platform;

namespace ClipNest.Platform.Tests;

public class FontListTests
{
    [Fact]
    public void Lists_installed_families()
    {
        var families = FontList.SystemFamilies();
        Assert.Contains("Segoe UI", families);
        Assert.DoesNotContain(families, f => f.StartsWith('@'));
        Assert.Equal(families.Count, families.Distinct().Count());
    }
}
