using ClipNest.Core.Settings;

namespace ClipNest.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"clipnest-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = new SettingsStore(_path).Load();
        Assert.Equal(RetentionPeriod.Month, s.Retention);
        Assert.Equal("Ctrl+Shift+V", s.PanelHotkey);
        Assert.True(s.IgnoreConfidential);
    }

    [Fact]
    public void Roundtrip()
    {
        var store = new SettingsStore(_path);
        var s = store.Load();
        s.AlwaysPlainText = true;
        s.Retention = RetentionPeriod.Year;
        s.IgnoredApps.Add(@"C:\x.exe");
        store.Save(s);

        var r = new SettingsStore(_path).Load();
        Assert.True(r.AlwaysPlainText);
        Assert.Equal(RetentionPeriod.Year, r.Retention);
        Assert.True(r.IsIgnored(@"c:\X.EXE"));
        Assert.Contains("\"Year\"", File.ReadAllText(_path));
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        File.WriteAllText(_path, "{nope");
        Assert.Equal(RetentionPeriod.Month, new SettingsStore(_path).Load().Retention);
    }

    [Fact]
    public void Out_of_range_values_are_sanitized()
    {
        File.WriteAllText(_path, """{ "PanelHeight": 9999, "PanelHotkey": "V", "IgnoredApps": null }""");
        var s = new SettingsStore(_path).Load();
        Assert.Equal(520, s.PanelHeight);
        Assert.Equal("Ctrl+Shift+V", s.PanelHotkey);
        Assert.NotNull(s.IgnoredApps);
    }
}
