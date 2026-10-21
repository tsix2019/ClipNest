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
    public void New_1_1_settings_roundtrip_and_default_for_old_files()
    {
        File.WriteAllText(_path, """{ "Theme": "dark" }""");
        var old = new SettingsStore(_path).Load();
        Assert.Equal(PanelEdge.Bottom, old.PanelEdge);
        Assert.False(old.UseWinV);
        Assert.Null(old.FontFamily);

        old.PanelEdge = PanelEdge.Right;
        old.PanelWidth = 400;
        old.UseWinV = true;
        old.FontFamily = "微软雅黑";
        new SettingsStore(_path).Save(old);

        var r = new SettingsStore(_path).Load();
        Assert.Equal(PanelEdge.Right, r.PanelEdge);
        Assert.Equal(400, r.PanelWidth);
        Assert.True(r.UseWinV);
        Assert.Equal("微软雅黑", r.FontFamily);
        Assert.Contains("\"Right\"", File.ReadAllText(_path));
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
