using System;

namespace Memoria.App.Windows;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,      // MOD_ALT
    Control = 0x0002,  // MOD_CONTROL
    Shift = 0x0004,    // MOD_SHIFT
    Win = 0x0008,      // MOD_WIN
}

public readonly record struct ParsedHotkey(HotkeyModifiers Modifiers, uint VirtualKey);

public static class HotkeyParser
{
    public const uint ModNoRepeat = 0x4000; // MOD_NOREPEAT

    public static bool TryParse(string? input, out ParsedHotkey result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var parts = input.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        var mods = HotkeyModifiers.None;
        uint vk = 0;
        bool keySet = false;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    mods |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    mods |= HotkeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                    mods |= HotkeyModifiers.Win;
                    break;
                default:
                    if (keySet)
                        return false; // 키는 하나만 허용
                    if (!TryMapKey(part, out vk))
                        return false;
                    keySet = true;
                    break;
            }
        }

        if (!keySet || mods == HotkeyModifiers.None)
            return false;

        result = new ParsedHotkey(mods, vk);
        return true;
    }

    private static bool TryMapKey(string key, out uint vk)
    {
        vk = 0;
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z') { vk = c; return true; } // VK_A..VK_Z == 'A'..'Z'
            if (c is >= '0' and <= '9') { vk = c; return true; } // VK_0..VK_9 == '0'..'9'
            return false;
        }

        if ((key[0] is 'F' or 'f') && uint.TryParse(key.AsSpan(1), out var fn) && fn is >= 1 and <= 24)
        {
            vk = 0x70 + (fn - 1); // VK_F1 == 0x70
            return true;
        }
        return false;
    }
}
