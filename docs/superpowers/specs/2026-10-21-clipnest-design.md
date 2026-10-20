# ClipNest 设计文档

> ClipNest：Windows 原生剪贴板管理器。逻辑、交互、视觉参考 macOS 上的 Paste（pasteapp.io），调研见 `docs/research/2026-10-21-paste-mac-teardown.md`。
>
> 日期：2026-10-21 · 状态：已确认（用户授权按推荐项自主推进）

## 1. 目标与非目标

**目标**
- 只做 Windows（10 1809+，主打 Windows 11），中英双语界面。
- 体验对齐 Paste：屏幕底部卡片面板、即时搜索、回车粘贴回原应用、Pinboards、Paste Stack、预览与编辑、筛选搜索、图片 OCR、隐私控制。
- 非功能目标：
  - 安装体积小，冷启动快。
  - 面板隐藏时内存占用低，呼出即时。
  - 界面有质感：Acrylic 半透明、圆角卡片、流畅动画。
- 代码结构清晰，测试齐全，日后可以直接开源。仓库暂时私有。

**非目标（首版不做）**
- 跨设备同步、团队共享 Pinboard、AI 功能（Intelligent Clipboard、MCP）、付费授权。
- 列表模式，以及顶部 / 侧边面板位置（面板只停靠底部 + 紧凑模式）。
- 只给某一条目绑定专属快捷键、文本变换工具（留作后续差异化功能）。

## 2. 技术选型

| 项 | 选择 | 理由 |
|---|---|---|
| 语言 / 运行时 | C# 13 / .NET 10 (LTS) | 用户熟悉；LTS |
| UI | WinUI 3（Windows App SDK 2.3.x 组件包：WinUI + Foundation + InteractiveExperiences） | 原生 Acrylic、Fluent、Composition 动画。只引用组件包、不用元包（元包会拉进 AI / ML / Widgets） |
| 存储 | 系统自带 `winsqlite3.dll`（Win11 上为 3.51），自写薄封装，用 `LibraryImport` 调用 | 零依赖、零体积；FTS5 trigram 原生支持中文子串搜索 |
| Win32 互操作 | 手写 `LibraryImport` 声明 | 不引入 CsWin32，兼容 AOT |
| 托盘 | 原生 `Shell_NotifyIcon`，菜单用 `TrackPopupMenu` | WinUI 没有托盘控件；原生方式最轻 |
| 设置存储 | `settings.json`，用 System.Text.Json 源生成器读写 | 兼容 AOT，用户可读 |
| 测试 | xUnit（只测 Core / Data） | 开源项目的通行做法 |
| 发布 | GitHub Actions：Native AOT + WindowsAppSDKSelfContained，产出 zip（便携版） | 大体积依赖只在 CI 下载；用户机器上零依赖 |
| 本地开发 | 依赖已安装的 Windows App Runtime 2.3（本机已有）的框架依赖模式 | 省下载 |

**AOT 约束**：XAML 一律用 `x:Bind`，不用 `{Binding}`；不依赖反射；JSON 全部走源生成。CI 里 AOT 如果失败，就退回到“裁剪 + ReadyToRun”的发布方式。

## 3. 解决方案结构

```
ClipNest.sln
src/
  ClipNest.Core/        领域模型、分类与去重、搜索语法、保留期策略（纯 .NET，不碰 Windows API）
  ClipNest.Data/        SQLite 封装（winsqlite3）、表结构升级、仓储实现、Blob 存储
  ClipNest.Platform/    Win32/WinRT：剪贴板监听与读写、热键、粘贴注入、App 信息与图标主色、托盘、OCR、开机启动、单实例
  ClipNest.App/         WinUI 3：面板、卡片、搜索、Pinboard、Paste Stack、预览/编辑、设置、引导、多语言资源
tests/
  ClipNest.Core.Tests/
  ClipNest.Data.Tests/
docs/
```

依赖方向：`App → Platform, Data → Core`。Core 不依赖任何其他项目。

## 4. 领域模型（Core）

```
ClipKind: Text | RichText | Image | File | Link | Color

ClipItem
  Id: long
  Kind: ClipKind
  Title: string?          用户重命名后的标题；为空时 UI 显示类型名
  Text: string?           纯文本（文本 / 富文本的纯文本 / 链接 URL / 颜色 hex / 文件路径以换行连接）
  ContentHash: string     去重键（SHA-256，取主内容）
  SourceAppPath: string?  来源 exe 完整路径
  SourceAppName: string?
  CreatedAt: DateTimeOffset   最近一次复制时间（重复复制会刷新）
  PinboardId: long?       null = 只在历史里
  CharCount: int
  ImageWidth/ImageHeight: int?
  BlobKinds: flags        这一条在磁盘上有哪些附带格式：Rtf | Html | Png | Thumb
  OcrText: string?
  LinkTitle: string?
  FileCount: int?

Pinboard: Id, Name, ColorIndex(0..7), SortOrder
```

**分类规则**：按以下顺序判断，第一条命中即确定类型。
1. 剪贴板里有 `CF_HDROP` → File。
2. 有位图（`PNG` / `CF_DIBV5` / `CF_DIB`），且没有任何文本格式 → Image。浏览器复制图片时会同时带上 HTML 格式，这时按图片 + HTML 处理，类型记为 Image。
3. 有文本：
   - trim 后是一个完整的 `http(s)://` URL，没有空白字符 → Link。
   - 是颜色值 → Color。规则对齐 Paste：带 `#` 的 6 位 hex，或者不带 `#` 但含至少一个 A–F 字母的 6 位 hex。8 位带 alpha 的 hex 同样处理。
   - 有 `Rich Text Format` 或 `HTML Format` → RichText。
   - 其他 → Text。

**去重**：对主内容算 SHA-256。
- 文本：对规范化后的纯文本算（只统一换行符为 `\n`）。
- 图片：对 PNG 字节算。
- 文件：对排序后的路径列表算。

命中已有条目时，更新它的 `CreatedAt` 和来源 App，让它回到最前面，不新增条目。如果已有条目在 Pinboard 里，Pinboard 中的那份不动，同时把它在历史里排到最前（历史列表按 `CreatedAt` 排序，自然就在最前）。

**保留期**：1 天 / 1 周 / 1 月（默认）/ 1 年 / 永久。
- 已 Pin 的条目永不过期。
- 清理时机：启动时一次，之后每小时一次。
- 另设硬上限：历史超过 50,000 条时，删除最旧的、未 Pin 的条目。

**搜索语法**（M3 里 UI 会把筛选条件显示成 token，Core 的解析器在 M1 就做好）：
- 自由文本：在标题、正文、OCR 文本、链接标题里搜。
- `type:text|image|link|file|color|rich`
- `app:<名称片段>`
- `date:today|yesterday|7d|30d`
- `pin:<pinboard 名称>`

## 5. 存储（Data）

数据目录是 `%LocalAppData%\ClipNest\`：
- `clipnest.db`：SQLite，开启 WAL，设置 `synchronous=NORMAL`。
- `blobs\{hash}.png|.rtf|.html`：原始格式数据，粘贴回去时保证保真。
- `thumbs\{hash}.png`：缩略图，长边 ≤ 360 px，由 WIC 生成。卡片只加载缩略图。
- `icons\{appHash}.png`：App 图标缓存，同时缓存提取出的主色。
- `settings.json`

```sql
items(id INTEGER PRIMARY KEY, kind INT, title TEXT, text TEXT, hash TEXT UNIQUE,
      app_path TEXT, app_name TEXT, created_at INT, pinboard_id INT NULL,
      char_count INT, img_w INT, img_h INT, blobs INT, ocr_text TEXT, link_title TEXT, file_count INT)
CREATE INDEX ix_items_created ON items(created_at DESC);
CREATE INDEX ix_items_pin ON items(pinboard_id, created_at DESC);
pinboards(id INTEGER PRIMARY KEY, name TEXT, color INT, sort INT)
apps(path TEXT PRIMARY KEY, name TEXT, color INT, icon_file TEXT)
items_fts USING fts5(title, text, ocr_text, link_title, content='items', content_rowid='id', tokenize='trigram')
  + 触发器负责同步
schema_version(v INT)
```

**关于 Pin 的语义**：Paste 里，Pin 之后历史中“仍保留该条目”，同时“一个条目只属于一个 Pinboard”。这里的实现是：Pin 时直接设置这一行的 `pinboard_id`，历史视图显示所有条目（包括已 Pin 的），Pinboard 视图只显示属于它的条目。只用一行数据，不复制。

**搜索执行**：
- 查询少于 3 个字符时（trigram 的限制），对 `text` 和 `title` 用 `LIKE '%q%'` 并加上 LIMIT。
- 否则用 FTS5 `MATCH`，结果按 `created_at DESC` 排序。
- 筛选条件转成 WHERE 子句。
- 每页 200 条，滚动到边缘时追加下一页。

所有写操作在一条后台写线程里串行执行（Channel 队列）。读操作用单独的只读连接。

## 6. 平台层（Platform）

| 能力 | 实现 |
|---|---|
| 消息窗口 | 一个 `HWND_MESSAGE` 隐藏窗口，统一接收剪贴板更新、热键、托盘回调、单实例激活 |
| 剪贴板监听 | `AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE`；做 50 ms 去抖；用 `GetClipboardSequenceNumber` 判断是不是自己写入的 |
| 读取 | `OpenClipboard` 失败时最多重试 5 次、每次间隔 20 ms。读取的格式：`CF_UNICODETEXT`、`Rich Text Format`、`HTML Format`、`PNG`、`CF_DIBV5`/`CF_DIB`、`CF_HDROP`。单项上限 50 MB，超过的跳过 |
| 隐私过滤 | 存在以下任一格式就忽略这次复制：`ExcludeClipboardContentFromMonitorProcessing`、`Clipboard Viewer Ignore`；`CanIncludeInClipboardHistory` 的值为 0；`CanUploadToCloudClipboard` 的值为 0（视为敏感内容）。来源 exe 在排除列表里也忽略。处于“暂停采集”状态时全部忽略 |
| 来源 App | 依次尝试 `GetClipboardOwner` → `GetForegroundWindow` → `GetWindowThreadProcessId` → `QueryFullProcessImageName`；显示名取 `FileVersionInfo.FileDescription`，没有就用 exe 文件名 |
| App 主色 | 用 `SHGetFileInfo` 或 `PrivateExtractIcons` 取 48 px 图标 → 读出 BGRA 像素 → 跳过透明像素和低饱和度像素，按色相分桶取最多的颜色 → 调整亮度，保证配白字时对比度 ≥ 4.5，不满足就改用黑字 |
| 写回剪贴板 | 一次性写入所有保存过的格式，外加私有格式 `ClipNest.Internal`；记下这次写入后的 sequence number，监听器遇到时跳过 |
| 粘贴注入 | 呼出面板前用 `GetForegroundWindow` 记下目标窗口 → 隐藏面板 → `SetForegroundWindow(target)`（遇到焦点限制时用 `AttachThreadInput` 兜底）→ 等到目标窗口确实拿到焦点（最多 150 ms）→ 用 `SendInput` 先释放仍按着的修饰键，再发送 Ctrl+V。目标窗口以管理员权限运行（UIPI 拦截）时，只复制到剪贴板，并弹 toast 提示 |
| 全局热键 | `RegisterHotKey`。默认 `Ctrl+Shift+V` 呼出面板、`Ctrl+Shift+C` 打开 Paste Stack；设置里可以改；注册冲突时提示 |
| 托盘 | `Shell_NotifyIcon`，左键呼出面板，右键菜单：打开、暂停采集 ▸（15 分钟 / 1 小时 / 直到恢复）、设置、退出 |
| 录屏隐藏 | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`，由设置项控制（默认开启隐藏） |
| OCR | `Windows.Media.Ocr`，用用户的语言配置；后台低优先级队列处理，结果写入 `ocr_text` |
| 链接元数据 | 用 `HttpClient` 取页面前 256 KB，解析 `<title>` 和 `og:title`；favicon 和 og:image 先不做，省流量和体积。可以在设置里关闭 |
| 开机启动 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| 单实例 | 命名 Mutex；第二个实例启动时给已有实例发消息，然后自己退出 |
| 内存回收 | 面板隐藏 3 秒后，释放缩略图缓存 → `GC.Collect`（compacting）→ `SetProcessWorkingSetSize(-1,-1)` |

## 7. 界面（App）

### 7.1 面板窗口
- 无边框、置顶、不在任务栏显示（`WS_EX_TOOLWINDOW`）。
- 呼出时出现在鼠标所在显示器的工作区底部，横向铺满，左右和底部各留 8 px 边距，四角圆角。
- 默认高度 320 DIP。拖动顶部边缘可在 200–520 DIP 之间调整，高度会持久保存。高度低于 260 DIP 时进入**紧凑模式**：隐藏卡片底部的元信息栏和 App 图标，卡片变矮。
- 背景用 `DesktopAcrylicController`（Base 风格，跟随系统深浅色），在 Windows 10 和关闭透明效果时会自动降级。
- **动画**：
  - 呼出：内容根节点的 Translation Y 从 +40 移到 0，同时不透明度 0→1，时长 180 ms，ease-out。
  - 隐藏：120 ms 淡出。
  - 选中卡片：边框高亮，并有 1.0→1.03 的缩放弹性动画。
  - 新条目出现：从左侧滑入。
- 失去焦点（点击面板外）时隐藏；按 Esc 隐藏（搜索中按 Esc 先清空搜索）。

### 7.2 顶部栏
从左到右：
- 搜索框：面板一出现，直接打字就会进入搜索。
- Pinboard 标签：「剪贴板」+ 用户的各个 Pinboard，每个带色点；当前标签有下划线指示。
- `+` 新建 Pinboard。
- 右侧：暂停指示和设置按钮。

### 7.3 卡片
卡片是正方形，边长随面板高度变化，默认 236 DIP，圆角 12。从上到下三部分：
- **头部色带**（高 44）：
  - 背景是来源 App 的主色。
  - 左侧：标题（默认是类型名，如「文本」「图片」「链接」），下面一行是相对时间（如「3 分钟前」）。
  - 右侧：App 图标 28 px。
- **内容区**，按类型显示：
  - 文本：前 400 个字符，最多 8 行。
  - 富文本：纯文本预览，加一个格式标记。
  - 图片：缩略图，`UniformToFill`。
  - 链接：URL 主机名（大字号）+ 页面标题。
  - 颜色：整块填充该颜色，叠加显示 hex 文字。
  - 文件：文件图标 + 文件名；多个文件时显示「N 个文件」。
- **底部元信息**：
  - 文本：「128 个字符」；图片：「1920×1080」；链接：完整 URL；文件：总大小。
  - 按住 Ctrl 时，前 9 张卡片的右下角显示序号徽标 1–9。

列表用 `ListView` 横向排列（`ItemsStackPanel`，开启虚拟化）；DataTemplate 用 `x:Bind`，按类型切换模板（`DataTemplateSelector`）。

### 7.4 键盘与鼠标

| 操作 | 行为 |
|---|---|
| ← / → | 切换选中 |
| Home / End（或 Ctrl+↑ / Ctrl+↓） | 跳到第一张 / 最后一张 |
| Enter / 双击 | 粘贴到原应用 |
| Shift+Enter | 以纯文本粘贴 |
| Ctrl+1…9 | 快速粘贴第 N 张；加 Shift 为纯文本 |
| 单击 | 只选中 |
| 直接打字 / Ctrl+F | 进入搜索 |
| Tab | 在搜索框和卡片之间切换焦点 |
| Ctrl+← / → | 切换 Pinboard |
| Space | 预览（M2） |
| Delete | 删除（Ctrl+Z 撤销） |
| Ctrl+C | 只复制到剪贴板，不粘贴 |
| Ctrl+E / Ctrl+R | 编辑 / 重命名（M2） |
| Ctrl+P | 打开“Pin 到…”菜单（M2） |
| Shift / Ctrl+点击、Shift+← / →、Ctrl+A | 多选（M2）；多选后粘贴时按顺序用换行拼接文本（只有文本类会合并；混合类型时只粘贴第一项并提示） |
| 拖出 | 拖出卡片时提供文本 / 图片 / 文件格式（M2） |
| 右键菜单 | 粘贴、纯文本粘贴、复制、Pin 到 ▸、编辑、重命名、预览、在资源管理器中显示（文件）、打开链接、删除 |

### 7.5 Paste Stack（M3）
- 按 `Ctrl+Shift+C` 打开一个小浮窗，停在屏幕右下，宽 320，显示栈里的条目列表。
- 浮窗打开期间，每次复制的内容都追加进栈（同时照常记入历史）。
- 剪贴板始终放着栈里的下一项。用低级键盘钩子 `WH_KEYBOARD_LL` 检测用户在其他应用里松开 Ctrl+V，每检测到一次就弹出当前项，再把下一项写进剪贴板。
- 可以切换方向（先进先出 / 后进先出）、删除单项。栈空了或关闭浮窗时，钩子随之卸载。

### 7.6 预览与编辑（M2）
- **预览**：一个独立浮窗，居中显示，按 Space 或 Esc 关闭。内容可以是大图、全文、文件列表。链接只显示 URL 和标题，不内嵌浏览器（不引入 WebView2 运行时开销）。
- **编辑**：文本类条目打开一个编辑浮窗（`TextBox` 多行），保存后更新正文和 hash。富文本编辑后会降级成纯文本，界面上会有提示。

### 7.7 设置窗口
普通窗口，Mica 背景，左侧 `NavigationView` 分组：
- **通用**：
  - 语言：跟随系统 / 中文 / English，重启后生效。
  - 开机启动。
  - 保留期，加“立即清空历史”按钮。
  - 粘贴方式：粘贴到当前应用 / 仅复制到剪贴板。
  - 总是以纯文本粘贴。
  - 抓取链接标题。
- **快捷键**：呼出面板、Paste Stack，用录入框设置，可恢复默认。
- **隐私**：
  - 排除应用列表：可添加 exe 或从正在运行的进程中选，可移除。
  - 忽略敏感内容（默认开）。
  - 录屏时隐藏面板（默认开）。
  - 暂停采集。
- **关于**：版本号、数据目录（可打开）、开源许可证。

### 7.8 首次启动引导
一个小窗口，三步：
1. 欢迎页，展示呼出快捷键。
2. 选择保留期。
3. 是否开机启动。

### 7.9 多语言
- 用 `Strings/en-US/Resources.resw` 和 `Strings/zh-CN/Resources.resw`，XAML 用 `x:Uid`，代码里统一走 `Loc.Get(key)`。
- 启动时根据设置项设置 `PrimaryLanguageOverride`。
- 相对时间、数量单位都走资源文件。

## 8. 性能预算

| 指标 | 目标 |
|---|---|
| 面板呼出（热键到可见） | < 120 ms（窗口常驻，只是隐藏） |
| 文本采集处理（读取 → 入库 → UI 更新） | < 20 ms |
| 历史 10k 条时搜索 | < 50 ms |
| 空闲内存（面板隐藏，整理后） | 私有工作集 < 60 MB |
| 发布体积（zip） | < 40 MB（AOT + 自包含 WinAppSDK） |
| 空闲 CPU | 0%（不轮询；完全由事件驱动） |

## 9. 错误处理

- 剪贴板被占用：重试；重试仍失败就放弃这次采集，只记日志。
- 数据库损坏：启动时运行 `PRAGMA quick_check`；失败就把旧库改名为 `clipnest.corrupt-<ts>.db`，新建一个，并通过 toast 通知用户。
- 热键注册失败：设置页标红，并弹出 toast。
- 粘贴注入失败（UIPI 拦截或焦点没拿到）：内容已经在剪贴板里，提示用户手动按 Ctrl+V。
- 未处理异常：写入 `%LocalAppData%\ClipNest\logs\`（滚动日志，只保留 5 个文件，每个 ≤ 1 MB），不上传任何数据。

## 10. 测试策略

- **Core 单元测试**：分类器（包括颜色和 URL 的边界情况）、hash 规范化、搜索语法解析、保留期截止时间计算、相对时间格式化。
- **Data 测试**：建表、插入和去重、FTS 中英文搜索（包括少于 3 个字符的回退路径）、Pin 和 Pinboard 的删除级联、保留期清理（不删已 Pin 的条目）、分页。
- **Platform**：剪贴板读写往返做手动或集成测试，不放进 CI。
- **UI**：手动验收清单，见实施计划。

## 11. 里程碑

- **M1 核心**：
  - 采集：六种类型、去重、来源 App 及主色、隐私过滤。
  - 底部面板：Acrylic、动画、卡片三段式布局；即时搜索。
  - 粘贴：Enter / 双击粘贴、Ctrl+1–9、Shift 纯文本、Ctrl+C 复制、删除和撤销。
  - 系统集成：托盘、全局热键、设置窗口（通用 / 快捷键 / 隐私 / 关于）、保留期清理、单实例、开机启动。
  - 双语。
  - CI 构建。
- **M2 组织**：Pinboards（新建、改名、改色、排序、删除，Pin 到…，Ctrl+← / →）、多选粘贴、预览、编辑和重命名、拖出、右键菜单完整化、面板高度调节与紧凑模式、首次启动引导。
- **M3 进阶**：Paste Stack、搜索筛选 token（UI 显示和自动补全）、图片 OCR 及搜索、链接标题抓取、录屏隐藏开关、暂停采集（定时）。
