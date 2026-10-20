using ClipNest.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace ClipNest.App.Views;

/// <summary>Click, then press a key combination to record a global hotkey. Esc cancels.</summary>
public sealed partial class HotkeyBox : Button
{
    private HotkeyGesture _gesture;
    private bool _recording;

    /// <summary>Raised when the user records a new valid gesture.</summary>
    public event EventHandler<HotkeyGesture>? GestureChanged;

    /// <summary>Raised when recording starts (true) or ends (false) — global hotkeys must be paused meanwhile.</summary>
    public static event Action<bool>? RecordingChanged;

    public HotkeyBox()
    {
        MinWidth = 160;
        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        Click += (_, _) => StartRecording();
        LostFocus += (_, _) => StopRecording();
        PreviewKeyDown += OnPreviewKeyDown;
        UpdateText();
    }

    public HotkeyGesture Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value;
            UpdateText();
        }
    }

    private void StartRecording()
    {
        if (_recording) return;
        _recording = true;
        RecordingChanged?.Invoke(true);
        UpdateText();
    }

    private void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        RecordingChanged?.Invoke(false);
        UpdateText();
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;

        if (e.Key == VirtualKey.Escape)
        {
            StopRecording();
            return;
        }
        if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.LeftMenu or VirtualKey.RightMenu)
        {
            return; // wait for the actual key
        }

        var mods = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control)) mods |= HotkeyModifiers.Ctrl;
        if (IsDown(VirtualKey.Menu)) mods |= HotkeyModifiers.Alt;
        if (IsDown(VirtualKey.Shift)) mods |= HotkeyModifiers.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) mods |= HotkeyModifiers.Win;

        var gesture = new HotkeyGesture(mods, (int)e.Key);
        if (!gesture.IsValid) return; // a modifier is required

        _gesture = gesture;
        StopRecording();
        GestureChanged?.Invoke(this, gesture);
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void UpdateText() =>
        Content = _recording ? Loc.Get("Settings_HotkeyRecording") : _gesture.IsValid ? _gesture.ToString().Replace("+", " + ") : "—";
}
