using Microsoft.Win32;

namespace GigaRest.Windows;

/// <summary>Автозапуск через HKCU\...\Run — без прав администратора. Стартуем тихо, в трее.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "GigaRest";

    public static string Command(string exePath) => $"\"{exePath}\" --tray";

    public static bool IsEnabled(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string value && string.Equals(value, Command(exePath), StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(string exePath, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(Name, Command(exePath));
        else if (key.GetValue(Name) is not null) key.DeleteValue(Name);
    }
}
