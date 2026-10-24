namespace GigaRest.Core.Input;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>Горячая клавиша вида «Ctrl+Alt+Home»: модификаторы и код клавиши Windows (VK).</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Пробел"] = 0x20,
        ["PageUp"] = 0x21, ["PgUp"] = 0x21, ["PageDown"] = 0x22, ["PgDn"] = 0x22,
        ["End"] = 0x23, ["Home"] = 0x24,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["Insert"] = 0x2D, ["Ins"] = 0x2D, ["Delete"] = 0x2E, ["Del"] = 0x2E,
        ["Pause"] = 0x13, ["ScrollLock"] = 0x91,
        ["Backspace"] = 0x08, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Esc"] = 0x1B, ["Escape"] = 0x1B,
    };

    // Обратное имя — первое из синонимов.
    private static readonly Dictionary<int, string> Names = Named
        .Where(kv => kv.Key.All(char.IsAscii))
        .GroupBy(kv => kv.Value)
        .ToDictionary(g => g.Key, g => g.First().Key);

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false; // без модификатора — слишком легко нажать случайно

        var mods = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var m = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };
            if (m == HotkeyModifiers.None || mods.HasFlag(m)) return false;
            mods |= m;
        }

        var key = parts[^1];
        int vk;
        if (Named.TryGetValue(key, out var named)) vk = named;
        else if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) vk = char.ToUpperInvariant(key[0]);
        else if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24)
            vk = 0x70 + f - 1;
        else return false;

        hotkey = new Hotkey(mods, vk);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(VirtualKey switch
        {
            >= 0x70 and <= 0x87 => "F" + (VirtualKey - 0x70 + 1),
            >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)VirtualKey).ToString(),
            _ => Names.GetValueOrDefault(VirtualKey, $"0x{VirtualKey:X2}"),
        });
        return string.Join("+", parts);
    }
}
