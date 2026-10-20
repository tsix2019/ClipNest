using ClipNest.Platform.Native;
using static ClipNest.Platform.Native.Shell32;
using static ClipNest.Platform.Native.User32;

namespace ClipNest.Platform;

/// <summary>A tray menu entry. <see cref="Children"/> makes it a submenu; <see cref="IsSeparator"/> a divider.</summary>
public sealed record TrayMenuItem(string Text, Action? Invoke = null, bool Checked = false, bool Enabled = true, IReadOnlyList<TrayMenuItem>? Children = null)
{
    public bool IsSeparator => Text.Length == 0;
    public static readonly TrayMenuItem Separator = new("");
}

/// <summary>Notification-area icon with a native context menu. Survives Explorer restarts.</summary>
public sealed unsafe class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WM_APP + 0x10;
    private const uint IconId = 1;

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    private nint _icon;
    private string _tooltip;
    private bool _added;

    /// <summary>Left click / keyboard select.</summary>
    public event Action? Clicked;

    /// <summary>Builds the context menu each time it opens.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuFactory { get; set; }

    public TrayIcon(MessageWindow window, nint icon, string tooltip)
    {
        _window = window;
        _icon = icon;
        _tooltip = tooltip;
        _window.AddHandler(OnMessage);
        Add();
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = Data(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void SetIcon(nint icon)
    {
        _icon = icon;
        var data = Data(NIF_ICON);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    /// <summary>Shows a balloon / toast-style notification from the tray icon.</summary>
    public void ShowNotification(string title, string message)
    {
        var data = Data(NIF_INFO);
        Copy(title, data.szInfoTitle, 64);
        Copy(message, data.szInfo, 256);
        data.dwInfoFlags = NIIF_INFO | NIIF_NOSOUND;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void Add()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
        data.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    private NOTIFYICONDATAW Data(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _window.Handle,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        Copy(_tooltip, data.szTip, 128);
        return data;
    }

    private static void Copy(string s, char* dest, int capacity)
    {
        var n = Math.Min(s.Length, capacity - 1);
        s.AsSpan(0, n).CopyTo(new Span<char>(dest, capacity));
        dest[n] = '\0';
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg == _taskbarCreated)
        {
            Add(); // Explorer restarted
            return false;
        }
        if (msg != CallbackMessage) return false;

        var evt = (uint)(lParam & 0xFFFF);
        switch (evt)
        {
            case NIN_SELECT or NIN_KEYSELECT or WM_LBUTTONUP:
                Clicked?.Invoke();
                break;
            case WM_CONTEXTMENU or WM_RBUTTONUP:
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                break;
        }
        return true;
    }

    private void ShowMenu(int x, int y)
    {
        var items = MenuFactory?.Invoke();
        if (items is null || items.Count == 0) return;

        var actions = new List<Action?>();
        var menu = Build(items, actions);
        try
        {
            SetForegroundWindow(_window.Handle); // required so the menu closes when clicking elsewhere
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY | TPM_BOTTOMALIGN, x, y, _window.Handle, 0);
            PostMessageW(_window.Handle, WM_NULL, 0, 0);
            if (cmd > 0 && cmd <= actions.Count) actions[cmd - 1]?.Invoke();
        }
        finally
        {
            DestroyMenu(menu); // also destroys submenus
        }
    }

    private static nint Build(IReadOnlyList<TrayMenuItem> items, List<Action?> actions)
    {
        var menu = CreatePopupMenu();
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                AppendMenuW(menu, MF_SEPARATOR, 0, null);
            }
            else if (item.Children is { Count: > 0 } children)
            {
                AppendMenuW(menu, MF_POPUP | (item.Enabled ? 0 : MF_GRAYED), Build(children, actions), item.Text);
            }
            else
            {
                actions.Add(item.Invoke);
                var flags = MF_STRING | (item.Checked ? MF_CHECKED : 0) | (item.Enabled ? 0 : MF_GRAYED);
                AppendMenuW(menu, flags, actions.Count, item.Text);
            }
        }
        return menu;
    }

    public void Dispose()
    {
        _window.RemoveHandler(OnMessage);
        if (_added)
        {
            var data = Data(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
    }
}
