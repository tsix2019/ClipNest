# ClipNest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build ClipNest — a native Windows clipboard manager whose logic, interaction and look follow macOS Paste — through milestones M1 (core), M2 (organize), M3 (advanced).

**Architecture:** Four projects with one-way dependencies: `App (WinUI 3) → Platform (Win32/WinRT) + Data (winsqlite3) → Core (pure .NET)`. Clipboard events arrive on a message-only window, are read into a `ClipboardSnapshot`, ingested by `ClipIngestor` (classify → hash → dedupe → persist blobs → insert/touch), and surfaced to a bottom-docked acrylic panel of horizontally virtualized cards. Pasting writes the item back to the clipboard (tagged so the monitor skips it), restores the previously focused window and injects Ctrl+V.

**Tech Stack:** .NET 10, C# 13, WinUI 3 (Windows App SDK 2.3 component packages: WinUI, Foundation, InteractiveExperiences), system `winsqlite3.dll` (FTS5 trigram), `LibraryImport` P/Invoke, System.Text.Json source generation, xUnit v3.

## Global Constraints

- Target `net10.0` (Core, Data, tests) and `net10.0-windows10.0.19041.0` (Platform, App); `SupportedOSPlatformVersion` 10.0.17763.0.
- NuGet packages allowed: `Microsoft.WindowsAppSDK.WinUI`, `Microsoft.WindowsAppSDK.Foundation`, `Microsoft.WindowsAppSDK.InteractiveExperiences` (+ their transitive deps), `xunit.v3` (tests only). Nothing else — user is on a slow, metered network.
- No reflection-based XAML binding (`{Binding}`); use `x:Bind`. No reflection JSON — use `JsonSerializerContext`. Keep code trimming/AOT-safe (`IsAotCompatible=true` on libraries).
- All Win32 interop via `[LibraryImport]` in `ClipNest.Platform/Native/*`.
- Data folder: `%LocalAppData%\ClipNest\` (`clipnest.db`, `blobs\`, `thumbs\`, `icons\`, `logs\`, `settings.json`).
- Default hotkeys: panel `Ctrl+Shift+V`, Paste Stack `Ctrl+Shift+C`.
- Retention options: Day / Week / Month (default) / Year / Forever; pinned items never expire; hard cap 50,000 unpinned items.
- UI strings only via resources (`Strings/en-US`, `Strings/zh-CN`), never hard-coded.
- Performance budgets (spec §8): show < 120 ms, ingest text < 20 ms, search 10k < 50 ms, idle private WS < 60 MB, zip < 40 MB, idle CPU 0%.
- Local build uses framework-dependent Windows App Runtime 2.3 (installed). Release (CI) uses `WindowsAppSDKSelfContained=true` + `PublishAot=true`.
- Toolchain on this machine: `Y:\devtools\dotnet\dotnet.exe` (set `DOTNET_ROOT`, `NUGET_PACKAGES=Y:\devtools\nuget`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`).

## File Structure

```
ClipNest.slnx
Directory.Build.props                 shared: LangVersion, Nullable, ImplicitUsings, TreatWarningsAsErrors(false), version
global.json                           SDK 10.0.402, rollForward latestFeature
src/ClipNest.Core/
  ClipKind.cs                          enum ClipKind, [Flags] BlobFlags
  ClipItem.cs                          mutable entity
  Pinboard.cs                          record
  AppInfo.cs                           record (Path, Name, Color ARGB, IconFile)
  ClipboardSnapshot.cs                 raw captured data
  ClipClassifier.cs                    kind detection, IsColor, IsUrl
  ContentHasher.cs                     SHA-256 dedupe key
  RetentionPolicy.cs                   RetentionPeriod enum + cutoff
  SearchQuery.cs                       parse "type:image app:chrome foo"
  RelativeTime.cs                      (resourceKey, n) for "3 minutes ago"
  HotkeyGesture.cs                     Ctrl/Shift/Alt/Win + VK, parse/format
  Settings/AppSettings.cs              settings model
  Settings/SettingsStore.cs            load/save JSON (source-gen context)
  Abstractions/IClipRepository.cs      repository contract
  Abstractions/IImageProcessor.cs      thumbnail contract (implemented in Platform)
src/ClipNest.Data/
  Sqlite/Sqlite3Native.cs              LibraryImport → winsqlite3.dll
  Sqlite/SqliteConnection.cs           open/exec/prepare/transaction
  Sqlite/SqliteStatement.cs            bind/step/columns
  Sqlite/SqliteException.cs
  ClipDatabase.cs                      open + migrations + quick_check/corruption recovery
  ClipRepository.cs                    IClipRepository implementation
  BlobStore.cs                         blobs/ thumbs/ files keyed by hash
  ClipIngestor.cs                      snapshot → item (classify/hash/dedupe/blobs)
src/ClipNest.Platform/
  Native/User32.cs Kernel32.cs Shell32.cs Gdi32.cs Dwmapi.cs Advapi32.cs
  MessageWindow.cs                     HWND_MESSAGE window + WndProc dispatch
  ClipboardMonitor.cs                  WM_CLIPBOARDUPDATE → debounced Changed
  ClipboardReader.cs                   formats → ClipboardSnapshot (privacy filtering)
  ClipboardWriter.cs                   ClipItem → clipboard (all formats + marker)
  ImageProcessor.cs                    WinRT imaging: BGRA→PNG, thumbnail, PNG→DIB
  AppInfoProvider.cs                   exe → name, icon PNG, dominant color
  DominantColor.cs                     pure pixel math (tested via Core-like test in Platform? → lives in Core: ColorMath.cs)
  HotkeyManager.cs                     RegisterHotKey wrapper
  PasteInjector.cs                     capture target, restore focus, SendInput
  TrayIcon.cs                          Shell_NotifyIcon + TrackPopupMenu
  StartupRegistration.cs               HKCU Run key
  SingleInstance.cs                    mutex + activation message
  DisplayHelper.cs                     cursor monitor work area + DPI
  WindowStyler.cs                      toolwindow, rounded corners, capture exclusion
  MemoryTrimmer.cs                     GC compact + SetProcessWorkingSetSize
  OcrService.cs                        (M3) Windows.Media.Ocr
  KeyboardHook.cs                      (M3) WH_KEYBOARD_LL for Paste Stack
  LinkTitleFetcher.cs                  (M3) title/og:title
src/ClipNest.App/
  App.xaml(.cs)  Program.cs            startup/composition root
  AppHost.cs                           singletons wiring
  Loc.cs                               ResourceLoader helper
  Strings/en-US/Resources.resw  Strings/zh-CN/Resources.resw
  Assets/ClipNest.ico
  ViewModels/ObservableObject.cs PanelViewModel.cs ClipCardViewModel.cs PinboardTabViewModel.cs
  Views/PanelWindow.xaml(.cs)          bottom panel
  Views/CardTemplates.xaml             DataTemplates per kind + selector
  Views/KindTemplateSelector.cs
  Views/SettingsWindow.xaml(.cs)       + Pages/GeneralPage, ShortcutsPage, PrivacyPage, AboutPage
  Views/PreviewWindow.xaml(.cs)        (M2)
  Views/EditWindow.xaml(.cs)           (M2)
  Views/PinboardDialog.xaml(.cs)       (M2)
  Views/OnboardingWindow.xaml(.cs)     (M2)
  Views/StackWindow.xaml(.cs)          (M3)
  Controls/HotkeyBox.cs                hotkey recorder
tests/ClipNest.Core.Tests/  tests/ClipNest.Data.Tests/
.github/workflows/ci.yml
README.md (bilingual)
```

(`DominantColor` math lives in Core as `ColorMath.cs` so it is unit-testable; Platform only extracts pixels.)

---

## Milestone M1 — Core

### Task 1: Toolchain + solution skeleton

**Files:** Create `global.json`, `Directory.Build.props`, `ClipNest.slnx`, the four `src/*/*.csproj`, two `tests/*/*.csproj`, `tools/env.sh`.

**Produces:** buildable empty solution; `tools/env.sh` exporting `DOTNET_ROOT`, `PATH`, `NUGET_PACKAGES`, `DOTNET_CLI_TELEMETRY_OPTOUT`, `DOTNET_NOLOGO`.

- [ ] Extract SDK zip to `Y:\devtools\dotnet`; `dotnet --version` → `10.0.402`.
- [ ] Write `Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>0.1.0</Version>
    <Authors>ClipNest contributors</Authors>
    <SatelliteResourceLanguages>en;zh-CN</SatelliteResourceLanguages>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```
- [ ] Core/Data csproj: `net10.0`, `IsAotCompatible=true`, `AllowUnsafeBlocks=true` (Data). Platform: `net10.0-windows10.0.19041.0`, `SupportedOSPlatformVersion=10.0.17763.0`, `IsAotCompatible`, `AllowUnsafeBlocks`. App: WinUI exe (see Task 9). Tests: `OutputType Exe`, `xunit.v3` package.
- [ ] `dotnet build ClipNest.slnx` (excluding App until Task 9) → succeeds.
- [ ] Commit `chore: solution skeleton`.

### Task 2: Core model, classifier, hasher

**Files:** `src/ClipNest.Core/{ClipKind,ClipItem,Pinboard,AppInfo,ClipboardSnapshot,ClipClassifier,ContentHasher}.cs`; test `tests/ClipNest.Core.Tests/ClipClassifierTests.cs`, `ContentHasherTests.cs`.

**Produces:**
```csharp
public enum ClipKind { Text = 0, RichText = 1, Image = 2, File = 3, Link = 4, Color = 5 }
[Flags] public enum BlobFlags { None = 0, Rtf = 1, Html = 2, Png = 4, Thumb = 8 }
public sealed class ClipboardSnapshot { string? Text; string? Rtf; string? Html; byte[]? Png; int ImageWidth; int ImageHeight; IReadOnlyList<string>? Files; string? SourceAppPath; }
public static class ClipClassifier {
  ClipKind Classify(ClipboardSnapshot s);
  bool TryParseColor(string text, out string hex);   // hex = "#RRGGBB" or "#RRGGBBAA", upper-case
  bool IsUrl(string text);
}
public static class ContentHasher { string Compute(ClipKind kind, ClipboardSnapshot s); string NormalizeText(string s); }
```
- [ ] Write tests (all must fail first):
```csharp
[Theory]
[InlineData("#ff8800", true, "#FF8800")]
[InlineData("ff88aa", true, "#FF88AA")]
[InlineData("#FF8800CC", true, "#FF8800CC")]
[InlineData("235442", false, null)]      // digits only, no '#'
[InlineData("#FFF", false, null)]
[InlineData("rgb(1,2,3)", false, null)]
[InlineData("red", false, null)]
[InlineData(" #00aa00 ", true, "#00AA00")]
public void TryParseColor(string input, bool ok, string? hex)
{ Assert.Equal(ok, ClipClassifier.TryParseColor(input, out var h)); if (ok) Assert.Equal(hex, h); }

[Theory]
[InlineData("https://example.com/a?b=1", true)]
[InlineData("http://x.cn", true)]
[InlineData("see https://example.com", false)]
[InlineData("ftp://x", false)]
[InlineData("https://exa mple.com", false)]
public void IsUrl(string s, bool ok) => Assert.Equal(ok, ClipClassifier.IsUrl(s));

[Fact] public void Files_win() => Assert.Equal(ClipKind.File, ClipClassifier.Classify(new() { Files = ["C:\\a.txt"], Text = "x" }));
[Fact] public void Image_with_html_is_image() => Assert.Equal(ClipKind.Image, ClipClassifier.Classify(new() { Png = [1], Html = "<img>" }));
[Fact] public void Image_with_text_is_text() => Assert.Equal(ClipKind.RichText, ClipClassifier.Classify(new() { Png = [1], Text = "hello", Rtf = "{\\rtf1}" }));
[Fact] public void Link() => Assert.Equal(ClipKind.Link, ClipClassifier.Classify(new() { Text = " https://a.com ", Html = "<a>" }));
[Fact] public void Color() => Assert.Equal(ClipKind.Color, ClipClassifier.Classify(new() { Text = "#AABBCC" }));
[Fact] public void Rich() => Assert.Equal(ClipKind.RichText, ClipClassifier.Classify(new() { Text = "a", Html = "<b>a</b>" }));
[Fact] public void Plain() => Assert.Equal(ClipKind.Text, ClipClassifier.Classify(new() { Text = "a" }));

// ContentHasherTests
[Fact] public void Crlf_and_lf_hash_equal() =>
  Assert.Equal(ContentHasher.Compute(ClipKind.Text, new() { Text = "a\r\nb" }), ContentHasher.Compute(ClipKind.Text, new() { Text = "a\nb" }));
[Fact] public void File_order_irrelevant() =>
  Assert.Equal(ContentHasher.Compute(ClipKind.File, new() { Files = ["b", "a"] }), ContentHasher.Compute(ClipKind.File, new() { Files = ["A", "B"] }));
[Fact] public void Image_hash_uses_png() =>
  Assert.NotEqual(ContentHasher.Compute(ClipKind.Image, new() { Png = [1, 2] }), ContentHasher.Compute(ClipKind.Image, new() { Png = [1, 3] }));
[Fact] public void Hash_is_64_hex() => Assert.Matches("^[0-9a-f]{64}$", ContentHasher.Compute(ClipKind.Text, new() { Text = "x" }));
```
- [ ] Run `dotnet run --project tests/ClipNest.Core.Tests` → FAIL (types missing).
- [ ] Implement. Rules: classify order File → Image (when no `Text`) → Link → Color → RichText (Rtf/Html present) → Text; note image+text+rtf → RichText (Office copies render a bitmap too). Hash: SHA-256 hex lower of `kind:` + payload (text normalized `\r\n`/`\r` → `\n`; files lower-cased, sorted ordinal, joined `\n`; image PNG bytes).
- [ ] Run tests → PASS. Commit `feat(core): classifier and content hasher`.

### Task 3: Core retention, search query, relative time, hotkey gesture, color math, settings

**Files:** `RetentionPolicy.cs`, `SearchQuery.cs`, `RelativeTime.cs`, `HotkeyGesture.cs`, `ColorMath.cs`, `Settings/AppSettings.cs`, `Settings/SettingsStore.cs`; tests for each.

**Produces:**
```csharp
public enum RetentionPeriod { Day, Week, Month, Year, Forever }
public static class RetentionPolicy { const int HardCap = 50_000; DateTimeOffset? Cutoff(RetentionPeriod p, DateTimeOffset now); }
public sealed record SearchQuery(string Text, ClipKind? Kind, string? App, DateTimeOffset? Since, string? Pinboard)
  { static SearchQuery Parse(string input, DateTimeOffset now); bool IsEmpty { get; } static readonly SearchQuery Empty; }
public readonly record struct RelativeTimeValue(string Key, int Value);   // Keys: Time_JustNow, Time_Minutes, Time_Hours, Time_Yesterday, Time_Days, Time_Date
public static class RelativeTime { RelativeTimeValue Describe(DateTimeOffset t, DateTimeOffset now); }
[Flags] public enum HotkeyModifiers { None=0, Alt=1, Ctrl=2, Shift=4, Win=8 }
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int Key) { static bool TryParse(string s, out HotkeyGesture g); override string ToString(); }
public static class ColorMath { uint DominantColor(ReadOnlySpan<byte> bgra); bool PrefersDarkText(uint argb); uint EnsureHeaderContrast(uint argb); }
public sealed class AppSettings { string Language="system"; bool LaunchAtStartup=true; RetentionPeriod Retention=Month; bool PasteToActiveApp=true; bool AlwaysPlainText; bool FetchLinkTitles=true; string PanelHotkey="Ctrl+Shift+V"; string StackHotkey="Ctrl+Shift+C"; List<string> IgnoredApps; bool IgnoreConfidential=true; bool HideFromCapture=true; double PanelHeight=320; bool OnboardingDone; }
public sealed class SettingsStore { SettingsStore(string path); AppSettings Load(); void Save(AppSettings s); }
```
- [ ] Tests:
```csharp
[Fact] public void Month_cutoff() { var now = new DateTimeOffset(2026,10,21,0,0,0,TimeSpan.Zero); Assert.Equal(now.AddMonths(-1), RetentionPolicy.Cutoff(RetentionPeriod.Month, now)); }
[Fact] public void Forever_null() => Assert.Null(RetentionPolicy.Cutoff(RetentionPeriod.Forever, DateTimeOffset.Now));
[Fact] public void Parse_tokens() {
  var now = new DateTimeOffset(2026,10,21,15,0,0,TimeSpan.Zero);
  var q = SearchQuery.Parse("type:image app:chrome  hello world date:7d", now);
  Assert.Equal("hello world", q.Text); Assert.Equal(ClipKind.Image, q.Kind); Assert.Equal("chrome", q.App); Assert.Equal(now.AddDays(-7), q.Since); }
[Fact] public void Parse_today_is_local_midnight() { var now = new DateTimeOffset(2026,10,21,15,0,0,TimeSpan.FromHours(8)); Assert.Equal(new DateTimeOffset(2026,10,21,0,0,0,TimeSpan.FromHours(8)), SearchQuery.Parse("date:today", now).Since); }
[Fact] public void Unknown_token_is_text() => Assert.Equal("foo:bar", SearchQuery.Parse("foo:bar", DateTimeOffset.Now).Text);
[Fact] public void Chinese_type_alias() => Assert.Equal(ClipKind.Image, SearchQuery.Parse("type:图片", DateTimeOffset.Now).Kind);
[Fact] public void Empty() => Assert.True(SearchQuery.Parse("   ", DateTimeOffset.Now).IsEmpty);
[Theory]
[InlineData(30, "Time_JustNow", 0)] [InlineData(150, "Time_Minutes", 2)] [InlineData(7200, "Time_Hours", 2)]
public void Relative(int secondsAgo, string key, int n) { var now = DateTimeOffset.Now; Assert.Equal(new RelativeTimeValue(key, n), RelativeTime.Describe(now.AddSeconds(-secondsAgo), now)); }
[Fact] public void Hotkey_roundtrip() { Assert.True(HotkeyGesture.TryParse("ctrl + shift + v", out var g)); Assert.Equal("Ctrl+Shift+V", g.ToString()); Assert.Equal(0x56, g.Key); }
[Fact] public void Hotkey_requires_modifier() => Assert.False(HotkeyGesture.TryParse("V", out _));
[Fact] public void Dominant_ignores_transparent() { var px = new byte[]{ 0,0,255,255,  0,0,255,255,  255,0,0,0 }; Assert.Equal(0xFFFF0000u, ColorMath.DominantColor(px)); }   // two opaque red px (BGRA), one transparent blue
[Fact] public void Yellow_prefers_dark_text() => Assert.True(ColorMath.PrefersDarkText(0xFFFFEB3Bu));
[Fact] public void Navy_prefers_light_text() => Assert.False(ColorMath.PrefersDarkText(0xFF1A237Eu));
[Fact] public void Settings_roundtrip() { var p = Path.GetTempFileName(); var st = new SettingsStore(p); var s = st.Load(); s.AlwaysPlainText = true; s.IgnoredApps.Add("x.exe"); st.Save(s); var r = new SettingsStore(p).Load(); Assert.True(r.AlwaysPlainText); Assert.Contains("x.exe", r.IgnoredApps); }
[Fact] public void Settings_corrupt_file_gives_defaults() { var p = Path.GetTempFileName(); File.WriteAllText(p, "{nope"); Assert.Equal(RetentionPeriod.Month, new SettingsStore(p).Load().Retention); }
```
- [ ] Fail → implement → pass. `DominantColor`: skip alpha < 128, skip saturation < 0.15 or value < 0.15 (fallback: average of all opaque pixels; if none, `0xFF5A5A5A`), bucket hue into 24 buckets weighted by saturation, return mean RGB of the heaviest bucket. `EnsureHeaderContrast`: if chosen text color contrast < 4.5, darken/lighten in HSL until ≥ 4.5 or 10 steps. Type aliases: text/文本, rich/富文本, image/图片, link/链接, file/文件, color/颜色.
- [ ] Commit `feat(core): retention, search syntax, relative time, hotkeys, color math, settings`.

### Task 4: SQLite wrapper over winsqlite3

**Files:** `src/ClipNest.Data/Sqlite/*`; test `tests/ClipNest.Data.Tests/SqliteTests.cs`.

**Produces:**
```csharp
public sealed class SqliteConnection : IDisposable {
  static SqliteConnection Open(string path, bool readOnly = false);
  void Execute(string sql, params object?[] args);          // multi-statement allowed when args empty
  T? Scalar<T>(string sql, params object?[] args);
  SqliteStatement Prepare(string sql);
  long LastInsertRowId { get; } int Changes { get; }
  void InTransaction(Action body);
  static string LibVersion { get; }
}
public sealed class SqliteStatement : IDisposable {
  SqliteStatement Bind(params object?[] args);  // long,int,string,double,byte[],bool,null,DateTimeOffset(→unix ms)
  bool Step(); void Reset();
  long GetInt64(int i); int GetInt32(int i); string? GetString(int i); double GetDouble(int i); bool IsNull(int i);
}
public sealed class SqliteException(int code, string message) : Exception(message) { int Code; }
```
- [ ] Tests: open temp db, create table, insert with params (string with Chinese and emoji, null, long), select round-trips; `Scalar<long>("select count(*)…")`; constraint violation throws `SqliteException` with code 19; `InTransaction` rolls back on exception; FTS5 trigram table matches `'剪贴板'` substring inside `'这是剪贴板历史'`.
- [ ] Implement with `[LibraryImport("winsqlite3.dll")]`, UTF-8 (`sqlite3_prepare_v2`, `sqlite3_bind_text` with `SQLITE_TRANSIENT = -1`), `sqlite3_open_v2` flags `READWRITE|CREATE|FULLMUTEX` or `READONLY`; set `busy_timeout=3000`.
- [ ] Pass; commit `feat(data): winsqlite3 wrapper`.

### Task 5: Schema, repository, blob store

**Files:** `ClipDatabase.cs`, `ClipRepository.cs`, `BlobStore.cs`, `src/ClipNest.Core/Abstractions/IClipRepository.cs`; tests `RepositoryTests.cs`, `BlobStoreTests.cs`.

**Produces:**
```csharp
public sealed record ClipQuery(SearchQuery Search, long? PinboardId, int Offset = 0, int Limit = 200);
public interface IClipRepository {
  ClipItem? Get(long id); ClipItem? FindByHash(string hash);
  long Insert(ClipItem item);                         // sets item.Id
  void Restore(ClipItem item);                        // re-insert with same Id (undo delete)
  void Touch(long id, DateTimeOffset at, string? appPath, string? appName);
  IReadOnlyList<ClipItem> Query(ClipQuery q);
  void Delete(long id);
  void Rename(long id, string? title);
  void UpdateText(long id, string text, string hash, int charCount);
  void SetPinboard(long id, long? pinboardId);
  void SetOcrText(long id, string text); void SetLinkTitle(long id, string title);
  IReadOnlyList<string> PurgeOlderThan(DateTimeOffset cutoff);   // returns hashes removed (unpinned only)
  IReadOnlyList<string> EnforceCap(int max);
  IReadOnlyList<string> ClearHistory();               // unpinned only
  IReadOnlyList<Pinboard> GetPinboards(); long AddPinboard(string name, int color);
  void UpdatePinboard(Pinboard p); IReadOnlyList<string> DeletePinboard(long id); void ReorderPinboards(IReadOnlyList<long> ids);
  AppInfo? GetApp(string path); void UpsertApp(AppInfo app);
  bool IsHashReferenced(string hash);
}
public sealed class BlobStore {
  BlobStore(string root);
  string PathFor(string hash, BlobFlags kind);  // .png/.rtf/.html → blobs\, Thumb → thumbs\{hash}.png
  void Write(string hash, BlobFlags kind, ReadOnlySpan<byte> data); byte[]? Read(string hash, BlobFlags kind);
  string? ReadText(string hash, BlobFlags kind); void WriteText(string hash, BlobFlags kind, string text);
  void DeleteAll(string hash);
}
```
- [ ] Tests: insert/get round-trip of every column; `FindByHash`; `Touch` moves item to first in `Query`; search FTS English ≥3 chars, Chinese ≥3 chars, 2-char Chinese falls back to LIKE; `Kind` filter; `App` filter (LIKE on app_name); `Since` filter; pinboard view only shows its items while history shows all; `PurgeOlderThan` keeps pinned; `DeletePinboard` deletes its items and returns hashes; `EnforceCap(2)` keeps newest 2 unpinned; paging offset/limit; `Restore` reinstates same id; schema_version = 1 after open; reopening existing db is idempotent.
- [ ] Implement schema from spec §5 (+ FTS triggers `items_ai/ad/au`). `ClipDatabase.Open(path)`: WAL, `synchronous=NORMAL`, `foreign_keys=ON`, `quick_check` → on failure rename to `clipnest.corrupt-{ts}.db` and recreate, exposes `bool RecoveredFromCorruption`.
- [ ] Pass; commit `feat(data): schema, repository, blob store`.

### Task 6: ClipIngestor

**Files:** `src/ClipNest.Data/ClipIngestor.cs`, `src/ClipNest.Core/Abstractions/IImageProcessor.cs`; test `IngestorTests.cs`.

**Produces:**
```csharp
public interface IImageProcessor { byte[] CreateThumbnailPng(byte[] png, int maxSide); }
public sealed record IngestResult(ClipItem Item, bool IsNew);
public sealed class ClipIngestor {
  ClipIngestor(IClipRepository repo, BlobStore blobs, IImageProcessor images, Func<string, AppInfo?> appResolver);
  IngestResult? Ingest(ClipboardSnapshot s, DateTimeOffset now);   // null when nothing usable
}
```
- [ ] Tests (fake image processor returns `[9]`): text new → IsNew, CharCount = text length (grapheme-agnostic `string.Length` minus `\r`); same text again → IsNew=false, same Id, CreatedAt updated; image writes `.png` + thumb blobs and sets ImageWidth/Height; rich text writes rtf/html blobs and BlobFlags; file item `Text` = paths joined by `\n`, FileCount; empty snapshot → null; whitespace-only text → null; color item `Text` = normalized hex; app name comes from resolver.
- [ ] Implement; pass; commit `feat(data): clip ingestor`.

### Task 7: Platform native layer + message window + clipboard read/write

**Files:** `src/ClipNest.Platform/Native/*.cs`, `MessageWindow.cs`, `ClipboardMonitor.cs`, `ClipboardReader.cs`, `ClipboardWriter.cs`, `ImageProcessor.cs`.

**Produces:**
```csharp
public sealed class MessageWindow : IDisposable { MessageWindow(string className); nint Handle; event MessageHandler? Message; } // delegate bool MessageHandler(uint msg, nint w, nint l, out nint result)
public sealed class ClipboardMonitor : IDisposable { ClipboardMonitor(MessageWindow w); event Action? Changed; void IgnoreSequence(uint seq); bool Paused; }
public sealed class ClipboardReader { ClipboardReader(Func<bool> ignoreConfidential); ClipboardSnapshot? Read(); } // null when ignored/empty
public sealed class ClipboardWriter { ClipboardWriter(BlobStore blobs, ImageProcessor img); uint Write(ClipItem item, bool plainText); uint WriteItems(IReadOnlyList<ClipItem> items, bool plainText); }
public sealed class ImageProcessor : IImageProcessor { byte[] EncodePng(ReadOnlySpan<byte> bgraTopDown, int w, int h); byte[] CreateThumbnailPng(byte[] png, int maxSide); (byte[] bgra, int w, int h) DecodePng(byte[] png); }
```
- [ ] Reader: retry `OpenClipboard(0)` 5×20 ms; check privacy formats (`ExcludeClipboardContentFromMonitorProcessing`, `Clipboard Viewer Ignore`, `CanIncludeInClipboardHistory`==0, `CanUploadToCloudClipboard`==0 when ignoreConfidential); `ClipNest.Internal` → null; read `CF_HDROP` (DragQueryFileW), `CF_UNICODETEXT`, `Rich Text Format`, `HTML Format` (UTF-8 CF_HTML; keep the raw string), `PNG` (raw bytes) else `CF_BITMAP` via `GetDIBits` 32bpp top-down → BGRA with alpha forced 255 → `EncodePng`; size cap 50 MB per format. Source app: owner window → process path; fallback foreground window.
- [ ] Writer: `EmptyClipboard`; set `CF_UNICODETEXT`; unless plainText: `Rich Text Format`, `HTML Format`, `PNG` + `CF_DIB` (decoded), `CF_HDROP` (DROPFILES struct); always `ClipNest.Internal` (1 byte); return `GetClipboardSequenceNumber()` after close.
- [ ] ImageProcessor via `Windows.Graphics.Imaging` (`BitmapEncoder`/`BitmapDecoder`/`BitmapTransform`, `InMemoryRandomAccessStream`), synchronous wrappers (`.AsTask().GetAwaiter().GetResult()` on background threads only).
- [ ] Manual verification harness: none yet (exercised by App in Task 10). Build must pass with zero warnings in Platform.
- [ ] Commit `feat(platform): clipboard monitor, reader, writer, imaging`.

### Task 8: Platform services: app info, hotkeys, paste injection, tray, startup, single instance, display, window styling, memory trim

**Files:** `AppInfoProvider.cs`, `HotkeyManager.cs`, `PasteInjector.cs`, `TrayIcon.cs`, `StartupRegistration.cs`, `SingleInstance.cs`, `DisplayHelper.cs`, `WindowStyler.cs`, `MemoryTrimmer.cs`.

**Produces:**
```csharp
public sealed class AppInfoProvider { AppInfoProvider(string iconDir, IClipRepository repo, ImageProcessor img); AppInfo? Resolve(string? exePath); }
public sealed class HotkeyManager : IDisposable { HotkeyManager(MessageWindow w); bool Register(int id, HotkeyGesture g); void Unregister(int id); event Action<int>? Pressed; }
public sealed class PasteInjector { void CaptureTarget(); nint Target; Task<bool> PasteAsync(); }   // false → UIPI/focus failure
public sealed class TrayIcon : IDisposable { TrayIcon(MessageWindow w, nint hIcon, string tooltip); event Action? Clicked; Func<IReadOnlyList<TrayMenuItem>>? MenuFactory; }
public sealed record TrayMenuItem(string Text, Action? Invoke, bool Checked = false, bool Separator = false, IReadOnlyList<TrayMenuItem>? Children = null);
public static class StartupRegistration { bool IsEnabled(); void SetEnabled(bool on, string exePath); }
public sealed class SingleInstance : IDisposable { static SingleInstance? TryAcquire(string name); static void SignalExisting(string className); }
public static class DisplayHelper { (RectInt32 workArea, double scale) GetCursorMonitor(); }
public static class WindowStyler { void MakeToolWindow(nint hwnd); void SetRoundedCorners(nint hwnd); void SetCaptureExcluded(nint hwnd, bool exclude); }
public static class MemoryTrimmer { void Trim(); }
```
- [ ] PasteInjector algorithm (spec §6): release Ctrl/Shift/Alt/Win via `SendInput` key-ups if `GetAsyncKeyState` shows them down; `SetForegroundWindow(target)`; if it fails, `AttachThreadInput` + `BringWindowToTop` + `SetForegroundWindow`; poll `GetForegroundWindow()==target` every 10 ms up to 150 ms; then send Ctrl down, V down, V up, Ctrl up. Detect UIPI: `SendInput` returns < 4 or target process elevated (`OpenProcessToken`/`TokenElevation`) while we are not → return false.
- [ ] Build passes; commit `feat(platform): system services`.

### Task 9: WinUI app shell: startup, host, tray, hotkey, localization

**Files:** `src/ClipNest.App/ClipNest.App.csproj`, `App.xaml(.cs)`, `AppHost.cs`, `Loc.cs`, `Strings/*/Resources.resw`, `Assets/ClipNest.ico`, `app.manifest`.

csproj essentials:
```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
<SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
<UseWinUI>true</UseWinUI>
<WindowsPackageType>None</WindowsPackageType>
<EnableMsixTooling>true</EnableMsixTooling>
<Platforms>x64;ARM64</Platforms> <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
<ApplicationManifest>app.manifest</ApplicationManifest>
<ApplicationIcon>Assets\ClipNest.ico</ApplicationIcon>
<PublishTrimmed Condition="'$(Configuration)'=='Release'">true</PublishTrimmed>
```
- [ ] `App.OnLaunched`: `SingleInstance.TryAcquire("ClipNest.Instance")` → else signal existing & exit; load settings; set `ApplicationLanguages.PrimaryLanguageOverride` if not "system"; build `AppHost` (db, repo, blobs, ingestor, monitor, reader, writer, hotkeys, injector, tray); register panel hotkey (toast-free fallback: tray balloon on failure); start retention timer (startup + hourly); create `PanelWindow` hidden.
- [ ] Monitor `Changed` → `Task.Run` read+ingest → `DispatcherQueue.TryEnqueue` → `PanelViewModel.OnIngested(result)`.
- [ ] Resources: all M1 strings in en-US and zh-CN (kind names, time phrases, meta formats, menus, settings labels).
- [ ] Run the app: tray icon appears, copying text logs to DB (inspect with a debug tray item "Open data folder").
- [ ] Commit `feat(app): shell, host wiring, tray, localization`.

### Task 10: Bottom panel window + cards

**Files:** `Views/PanelWindow.xaml(.cs)`, `Views/CardTemplates.xaml`, `Views/KindTemplateSelector.cs`, `ViewModels/*`.

- [ ] Window: `AppWindow` presenter borderless (`OverlappedPresenter.SetBorderAndTitleBar(false,false)`, `IsResizable=false`, `IsAlwaysOnTop=true`), `IsShownInSwitchers=false`, `WindowStyler.MakeToolWindow/SetRoundedCorners/SetCaptureExcluded`, `SystemBackdrop = new DesktopAcrylicBackdrop()`.
- [ ] `ShowPanel()`: `PasteInjector.CaptureTarget()`, position to cursor monitor work area bottom (margin 8 DIP, height = settings.PanelHeight × scale), refresh first page if stale, `Show`+`Activate`, select first card, run slide/fade storyboard (TranslateY 40→0, Opacity 0→1, 180 ms, `CubicEase EaseOut`). `HidePanel()`: 120 ms fade then `AppWindow.Hide()`; schedule 3 s trim timer (clear `Items`, `MemoryTrimmer.Trim()`).
- [ ] Deactivated → hide (unless `SuppressAutoHide`).
- [ ] Top bar: `AutoSuggestBox`-free plain `TextBox` with search glyph, pinboard tabs `ItemsRepeater` (M1: only "Clipboard"), settings button.
- [ ] Cards per spec §7.3 using `ListView` horizontal `ItemsStackPanel`, `SelectionMode=Extended` (M2 uses multi), `IsItemClickEnabled`, `DoubleTapped` → paste. Templates: Text, RichText, Image, Link, Color, File. Header color brush from `AppInfo.Color` (cached `SolidColorBrush` per ARGB), foreground via `ColorMath.PrefersDarkText`. Thumbnail lazy-loaded via `BitmapImage.SetSourceAsync` with `DecodePixelHeight` = card px.
- [ ] Keyboard per spec §7.4 M1 rows; typing → focus search and append; search debounced 120 ms; Ctrl held → badges 1–9.
- [ ] Paste flow: `ClipboardWriter.Write` → `monitor.IgnoreSequence(seq)` → `HidePanel()` (immediate, no fade) → if `PasteToActiveApp` → `injector.PasteAsync()`; on false show tray balloon "Copied — press Ctrl+V". Item touched (move to front).
- [ ] Delete with undo stack (Ctrl+Z restores last 20 deletes; blob deletion deferred until app exit or stack eviction).
- [ ] Manual acceptance: copy text/image/link/color/file from various apps → cards appear with right template & colors; Ctrl+Shift+V opens on the monitor under the cursor; typing filters; Enter pastes into Notepad, Word (rich), Paint (image), Explorer (file); Shift+Enter pastes plain; Ctrl+3 pastes 3rd; Esc closes; click outside closes.
- [ ] Commit `feat(app): bottom panel, cards, search, paste`.

### Task 11: Settings window (General / Shortcuts / Privacy / About)

**Files:** `Views/SettingsWindow.xaml(.cs)`, `Views/Pages/*.xaml(.cs)`, `Controls/HotkeyBox.cs`.

- [ ] Mica backdrop, `NavigationView` left. General: language (ComboBox, "restart to apply" InfoBar), launch at startup (ToggleSwitch → `StartupRegistration`), retention (ComboBox; shrinking prompts ContentDialog confirm, then purge), clear history (confirm), paste mode, always plain text, fetch link titles. Shortcuts: `HotkeyBox` for panel & stack hotkeys (re-register live; red InfoBar on failure), reset. Privacy: ignored apps ListView + "Add…" (FileOpenPicker for .exe, initialized with window handle) + "From running apps" (MenuFlyout listing distinct process paths with windows) + remove; ignore confidential; hide from capture; pause. About: version, data folder button, license.
- [ ] Settings save on change (debounced 300 ms).
- [ ] Commit `feat(app): settings window`.

### Task 12: CI + README + push

**Files:** `.github/workflows/ci.yml`, `README.md`, `LICENSE` (MIT, prepared for future open-sourcing).

- [ ] CI on `windows-latest`: setup .NET 10; `dotnet run --project tests/...` (both); `dotnet publish src/ClipNest.App -c Release -r win-x64 -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:PublishAot=true` (fallback job flag `-p:PublishAot=false -p:PublishReadyToRun=true` if AOT fails); zip `publish/` → artifact; on tag `v*` attach to release.
- [ ] README bilingual (中文 / English): features, shortcuts, build instructions, privacy statement.
- [ ] `gh repo create tsix2019/ClipNest --private --source . --push`.
- [ ] Commit `ci: build, test, publish`.

---

## Milestone M2 — Organize

### Task 13: Pinboards
- Repository already supports pinboards (Task 5). UI: tabs for "Clipboard" + pinboards (color dot, name), `+` → `PinboardDialog` (name TextBox, 8 color swatches), right-click tab → rename/recolor/delete (confirm, cascade), drag to reorder tabs (`ReorderPinboards`), Ctrl+←/→ switch; card context "Pin to ▸" list + "New pinboard…"; Ctrl+P opens it; "Unpin" when in pinboard.
- Acceptance: pinned items survive `ClearHistory` and retention purge; deleting pinboard removes its items.

### Task 14: Multi-select, context menu, drag-out
- Extended selection (Ctrl/Shift click, Shift+←/→, Ctrl+A). Enter on multiple text items → joined with `\r\n` pasted; mixed kinds → paste first + tray notice.
- Full `MenuFlyout` per spec §7.4. "Show in Explorer" (`explorer.exe /select,`), "Open link" (`Launcher`/`ShellExecute`).
- Drag out: `CanDragItems=true`, `DragItemsStarting` sets `DataPackage` (text / bitmap via `RandomAccessStreamReference` / `StorageFile`s for files & image blob).

### Task 15: Preview, edit, rename
- `PreviewWindow` (Space toggles; Esc closes): large image (fit), scrollable text (`TextBlock` with `IsTextSelectionEnabled`), file list, color swatch with RGB/HSL values, link URL + title.
- `EditWindow` (Ctrl+E): multiline TextBox, Save → `UpdateText` + rehash (merge if collides with an existing hash: keep edited one, delete the other), RichText → becomes Text with notice.
- Rename (Ctrl+R): inline TextBox over card title; empty → default kind name.
- Panel `SuppressAutoHide` while these windows are open.

### Task 16: Resizable height + compact mode + onboarding
- Top-edge 6 px drag handle (pointer capture, `SetCursor` SizeNS) adjusts height within 200–520 DIP, persisted.
- Compact mode (< 260 DIP): hide footer and app icon; card side = available height.
- `OnboardingWindow` on first run: welcome + hotkey, retention choice, launch at startup; sets `OnboardingDone`.

## Milestone M3 — Advanced

### Task 17: Paste Stack
- `KeyboardHook` (`WH_KEYBOARD_LL`) installed only while stack window open; detects Ctrl+V key-up in other processes.
- `StackWindow` (320 wide, bottom-right, topmost, acrylic): list, direction toggle (FIFO/LIFO), remove item, clear, close. While open, every ingested copy (not our own writes) appends. Clipboard always holds the next item; on Ctrl+V up → pop + write next (ignore-sequence) after 60 ms delay.
- Hotkey `Ctrl+Shift+C` toggles.

### Task 18: Filter tokens UI
- Search box shows parsed filters as removable chips (`ItemsRepeater` of `Button`s) to the left of the text; typing `type:`/`app:`/`date:`/`pin:` opens a suggestion flyout (kinds, distinct recent apps, date presets, pinboards). Repository `Query` already filters.

### Task 19: OCR + link titles
- `OcrService`: `OcrEngine.TryCreateFromUserProfileLanguages()`; background queue (one at a time, `BelowNormal` priority) for new images ≤ 4096 px side (downscale otherwise); `SetOcrText`. Backfill un-OCR'd images at idle.
- `LinkTitleFetcher`: `HttpClient` (5 s timeout, 256 KB cap, `User-Agent: ClipNest`), regex `<title>` / `og:title`, HTML-decode; only when `FetchLinkTitles`; `SetLinkTitle`.

### Task 20: Capture exclusion toggle + timed pause
- `HideFromCapture` setting applied live to panel/preview/stack windows.
- Pause: tray submenu 15 min / 1 h / until resumed; panel top bar shows "Paused" pill (click to resume); `monitor.Paused`.

---

## Self-review notes
- Spec §1–§11 mapped: model/classify/hash (T2), retention/search/settings (T3), storage (T4–T6), platform (T7–T8, T17, T19–T20), panel/cards/keys (T10, T13–T16), settings (T11), onboarding (T16), i18n (T9), CI/release (T12), errors (T5 corruption, T8 UIPI, T11 hotkey failure, logging in T9 `AppHost`).
- UI tasks (T9–T20) specify structure, interfaces and acceptance checks rather than full listings; XAML is iterated against the running app.
