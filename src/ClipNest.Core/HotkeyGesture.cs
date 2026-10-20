using System.Text;

namespace ClipNest.Core;

/// <summary>Bit values match Win32 <c>RegisterHotKey</c> MOD_* flags.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A global hotkey: modifiers plus a Win32 virtual-key code. Text form: "Ctrl+Shift+V".</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int Key)
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Esc"] = 0x1B, ["Insert"] = 0x2D, ["Delete"] = 0x2E,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["`"] = 0xC0, ["-"] = 0xBD, ["="] = 0xBB, ["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xDC,
        [";"] = 0xBA, ["'"] = 0xDE, [","] = 0xBC, ["."] = 0xBE, ["/"] = 0xBF,
    };

    public bool IsValid => Modifiers != HotkeyModifiers.None && Key != 0;

    /// <summary>
    /// Safe to register system-wide: needs Ctrl, Alt or Win (Shift alone would swallow typing, e.g. Shift+A),
    /// and must not hijack window-management shortcuts.
    /// </summary>
    public bool IsSafeGlobal
    {
        get
        {
            if (!IsValid || (Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0) return false;
            var altOnly = (Modifiers & ~HotkeyModifiers.Shift) == HotkeyModifiers.Alt;
            return !(altOnly && Key is 0x73 or 0x09 or 0x1B or 0x20); // Alt+F4, Alt+Tab, Alt+Esc, Alt+Space
        }
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var mods = HotkeyModifiers.None;
        var key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; continue;
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "win" or "windows" or "meta": mods |= HotkeyModifiers.Win; continue;
            }
            if (key != 0) return false;
            key = KeyFromName(raw);
            if (key == 0) return false;
        }

        if (mods == HotkeyModifiers.None || key == 0) return false;
        gesture = new HotkeyGesture(mods, key);
        return true;
    }

    public static int KeyFromName(string name)
    {
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out var f) && f is >= 1 and <= 24)
            return 0x70 + f - 1;
        return NamedKeys.TryGetValue(name, out var vk) ? vk : 0;
    }

    public static string KeyName(int vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
        foreach (var (name, code) in NamedKeys)
        {
            if (code == vk) return name;
        }
        return $"0x{vk:X2}";
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) sb.Append("Win+");
        sb.Append(KeyName(Key));
        return sb.ToString();
    }
}
