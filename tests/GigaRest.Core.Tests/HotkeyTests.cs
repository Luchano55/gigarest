using GigaRest.Core.Input;

namespace GigaRest.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Home", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x24, "Ctrl+Alt+Home")]
    [InlineData("ctrl + alt + space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20, "Ctrl+Alt+Space")]
    [InlineData("Ctrl+Alt+Пробел", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20, "Ctrl+Alt+Space")]
    [InlineData("Win+Shift+R", HotkeyModifiers.Win | HotkeyModifiers.Shift, 'R', "Shift+Win+R")]
    [InlineData("Alt+F12", HotkeyModifiers.Alt, 0x7B, "Alt+F12")]
    [InlineData("Ctrl+7", HotkeyModifiers.Ctrl, '7', "Ctrl+7")]
    public void Parses(string text, HotkeyModifiers mods, int vk, string normalized)
    {
        Assert.True(Hotkey.TryParse(text, out var h));
        Assert.Equal(mods, h.Modifiers);
        Assert.Equal(vk, h.VirtualKey);
        Assert.Equal(normalized, h.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Home")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+Meta+A")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Ж")]
    public void RejectsBad(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }
}
