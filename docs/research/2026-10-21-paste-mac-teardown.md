# Paste（pasteapp.io）产品拆解：为做 Windows 克隆准备

调研时间 2026-10。主要依据官网帮助中心、Updates、Blog 原文，第三方评测作补充。标 **不确定 / 未核实** 的条目需实测确认。

## 0. 版本脉络
- 2015：全屏形态。
- 2016：改为底部面板，引入 Pinboard。
- 2020：Paste Stack、链接预览。
- 2023：Paste 4，窗口可调高度，加入 Compact Mode。
- 2025.5：Paste 5，共享 Pinboard，重写同步引擎。
- 2025.9：Paste 6，Liquid Glass 重设计；同月推出 Teams。
- 2025.10：录屏/屏幕共享隐私开关。
- 2025.11：接入 Apple Intelligence 写作工具。
- 2025.12：Power Search（筛选 + 图片 OCR）。
- 2026.3：按 Pinboard 筛选。
- 2026.4：Jump to History。
- 2026.6：Paste MCP。
- 2026.9：Intelligent Clipboard。

## 1. 核心功能
- **采集类型**：文本、富文本、链接、图片、文件、颜色。
  - 链接抓网页元数据生成缩略图。
  - 颜色识别：单独复制的 6 位 hex，带 # 或至少含一个 A–F 字母才算 Color；纯数字、#FFF、rgb()、颜色名按文本存。
  - 代码语法高亮：**不确定**。
- **保留期**：1 天 / 1 周 / 1 月（默认）/ 1 年 / 永久。Pin 的条目不过期。调低保留期会确认删除旧条目。有 Erase History（保留 Pin）。
- **搜索（Power Search）**：
  - 打开面板直接打字即搜索；容错拼写。
  - 筛选：类型、来源 App、设备、日期范围、Pinboard（多选），以 token 形式内联在搜索框。
  - 图片 OCR 并高亮命中位置。
  - Cmd+G 从搜索结果跳回原位置。
- **Pinboards**：名称 + 颜色；拖拽排序；删除 Pinboard 连带删除条目；一个条目只属于一个 Pinboard；Pin 后历史中仍保留；可批量 Pin；可共享。
- **Paste Stack**：Shift+Cmd+C 打开小窗，之后每次复制按顺序入栈；每次 Cmd+V 出一项并移除；方向可切换。
- **预览与编辑**：空格 Quick Look；Cmd+E 原地编辑；Cmd+R 重命名标题；Cmd+N 新建文本条目；Cmd+O 打开链接/文件。
- **纯文本粘贴**：Shift+Return、右键菜单、或全局“总是纯文本”。修饰键可改。
- **多选粘贴**：Cmd/Shift 点选、Shift+←/→、Cmd+A，一并粘贴。
- **同步**：仅用户私有 iCloud。
- **隐私**：排除指定 App；忽略 transient / concealed 内容；Cmd+T 暂停采集（可选时长）；录屏/共享时隐藏面板。已知缺口：密码管理器浏览器扩展复制的内容可能漏检。
- **去重**：复制同一内容不再产生重复条目（是否移到最前未官方说明，推测是）。

## 2. 交互逻辑
- **呼出**：Shift+Cmd+V 或菜单栏图标；面板在当前显示器底部滑出。
- **导航**：←/→ 切换条目；Cmd+↑/↓ 首/尾；Cmd+←/→ 切换 Pinboard；Tab 在搜索框和结果间切换。
- **粘贴到原应用**：Return / 双击 / 拖拽；Cmd+1~9 Quick Paste，Shift+Cmd+1~9 纯文本 Quick Paste。
  - 原理：写入系统剪贴板 → 关闭面板 → 向原前台应用发送 Cmd+V。
  - 设置项 Paste Items：To active app / To clipboard。
- **单击只选中**，不复制不粘贴。
- **其他**：Delete 删除、Cmd+Z 撤销、Cmd+C 复制选中项、Esc 关闭（搜索中先退出搜索）。
- **右键菜单**：Paste、Paste as Plain Text、Pin to…、Edit、Rename、Preview、Share、Delete 等。

## 3. 视觉样式
- **布局**：屏幕底部通栏，横向滚动的方形卡片，最新在最左。拖动面板上沿可调高度；较矮时切 Compact Mode。
- **卡片结构**：
  - 头部色带：来源 App 图标 + 标题 + 时间，色带取 App 图标主色（倾向此说法，官方未公布算法）。
  - 中部：内容预览（文本摘录 / 图片缩略图 / 链接缩略图 + favicon / 颜色块 + hex）。
  - 底部：元信息（字符数、URL、尺寸等）。
- **顶部栏**：搜索、Pinboard 标签（各带颜色）、+ 按钮、Suggestions。
- **材质**：Paste 6 为 Liquid Glass 半透明 + 圆角卡片，跟随深浅色与强调色。
- **布局选项**：未发现顶部/侧边/列表式布局 —— 用户有明确需求，可做差异化。

## 4. 设置项（Mac）
- **General**：开机启动、Keep History、Erase History、iCloud sync、Paste Items、Always paste as Plain Text、隐藏托盘图标（推测）。
- **Shortcuts**：Activate Paste、Activate Paste Stack、Next/Previous Pinboard、Quick Paste 修饰键、Plain Text 修饰键、重置。
- **Privacy**：Ignore Applications、Ignore transient、Ignore confidential、Show during screen sharing。
- **Intelligence**：Intelligent Clipboard、MCP & AI tools。
- 首次启动引导：呼出方式、保留期、系统集成权限。

## 5. 商业模式
- 月付 $3.99 / 年付 $29.99 / 终身 $99.99；7 天试用；家庭版、Teams。
- 仅 Apple 平台，**没有 Windows 版**。

## 6. 用户抱怨（= 我们的机会）
1. 订阅制贵。
2. 底部大卡片在大屏上笨重；想要列表模式、顶部/侧边位置；翻找旧条目效率低。
3. 稳定性：Paste Stack、纯文本设置、搜索、同步偶有问题。
4. 密码管理器内容漏检。
5. 不能给单个片段绑定快捷键；缺 JSON 格式化、URL 解码、大小写转换等文本变换。
6. “永久保留”占空间。

## 7. Windows 对应实现要点（初步）
| Mac 机制 | Windows 对应 |
|---|---|
| NSPasteboard 轮询 changeCount | `AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE` |
| transient / concealed 类型 | `ExcludeClipboardContentFromMonitorProcessing`、`CanIncludeInClipboardHistory`=0、`Clipboard Viewer Ignore` 格式 |
| 辅助功能权限 + 发送 Cmd+V | 记住前台 HWND → `SetForegroundWindow` → `SendInput` Ctrl+V（需处理焦点窃取限制、UIPI 对管理员窗口的限制） |
| 全局快捷键 | `RegisterHotKey`；默认 Win+V 被系统占用，建议 Ctrl+Shift+V 或可配置 |
| 菜单栏图标 | 系统托盘 `Shell_NotifyIcon` |
| Liquid Glass | Mica / Acrylic（DWM `DWMWA_SYSTEMBACKDROP_TYPE`）+ 圆角 |
| 录屏隐藏 | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` |
| 图片 OCR | `Windows.Media.Ocr` |
| Quick Look | 自建预览窗口（或调用 Preview Handler） |
| iCloud 同步 | 首版不做 / 后续可选文件夹同步（OneDrive 等） |

## 来源
- 官网：https://pasteapp.io/ ；https://pasteapp.io/pricing ；https://pasteapp.io/updates
- 帮助中心：https://pasteapp.io/help/paste-on-mac 、/keyboard-shortcuts 、/using-paste-stack 、/paste-directly-to-other-applications 、/organize-with-pinboards 、/search-and-filters 、/what-paste-captures 、/exclude-apps 、/control-history-retention 、/paste-as-plain-text 、/quick-paste-with-numbered-shortcuts 、/edit-items-before-pasting 、/intelligent-clipboard 、/paste-mcp
- 博客：https://pasteapp.io/blog/paste-in-liquid-glass 、/paste-4-0-your-workflow-amplified-once-again 、/paste-power-search 、/paste-evolution-through-the-years
- 第三方：MacStories、Product Hunt reviews、Zapier、DrBuho、App Store 页面、开源仿品 Clipbara / Pesty
