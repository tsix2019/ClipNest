using ClipNest.Core;
using ClipNest.Platform.Native;

namespace ClipNest.Platform;

/// <summary>System-wide hotkeys via <c>RegisterHotKey</c>. <see cref="Pressed"/> fires on the UI thread.</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly HashSet<int> _registered = [];

    public event Action<int>? Pressed;

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.AddHandler(OnMessage);
    }

    /// <summary>Registers (or re-registers) <paramref name="id"/>. False when another app owns the combination.</summary>
    public bool Register(int id, HotkeyGesture gesture)
    {
        Unregister(id);
        if (!gesture.IsValid) return false;
        var ok = User32.RegisterHotKey(_window.Handle, id, (uint)gesture.Modifiers | User32.MOD_NOREPEAT, (uint)gesture.Key);
        if (ok) _registered.Add(id);
        else Log.Warn($"RegisterHotKey failed for {gesture}");
        return ok;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id)) User32.UnregisterHotKey(_window.Handle, id);
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg != User32.WM_HOTKEY) return false;
        Pressed?.Invoke((int)wParam);
        return true;
    }

    public void Dispose()
    {
        foreach (var id in _registered.ToArray()) Unregister(id);
        _window.RemoveHandler(OnMessage);
    }
}
