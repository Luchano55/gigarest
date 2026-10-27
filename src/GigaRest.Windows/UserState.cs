using System.Runtime.InteropServices;

namespace GigaRest.Windows;

/// <summary>
/// Можно ли сейчас что-то предлагать: не идёт ли игра на весь экран, презентация,
/// не заблокирован ли компьютер (SHQueryUserNotificationState, решение D-PAUSE).
/// </summary>
public static class UserState
{
    private enum QUERY_USER_NOTIFICATION_STATE
    {
        NotPresent = 1, Busy = 2, RunningD3DFullScreen = 3, PresentationMode = 4, AcceptsNotifications = 5, QuietTime = 6, App = 7,
    }

    /// <summary>Для журнала: что Windows говорит об уведомлениях (5 — принимает, 6 — «тихие часы»).</summary>
    public static int NotificationState() => SHQueryUserNotificationState(out var state) == 0 ? (int)state : -1;

    /// <summary>true — лучше не беспокоить (игра, фильм на весь экран, презентация, ПК заблокирован).</summary>
    public static bool ShouldNotDisturb()
    {
        // Наш «Тихий» экран тоже на весь экран — это не игра и не помеха.
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (pid == Environment.ProcessId) return false;
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state is QUERY_USER_NOTIFICATION_STATE.NotPresent or QUERY_USER_NOTIFICATION_STATE.Busy
            or QUERY_USER_NOTIFICATION_STATE.RunningD3DFullScreen or QUERY_USER_NOTIFICATION_STATE.PresentationMode;
    }

    /// <summary>
    /// Сцену ставим на паузу, только если её закрывает чужое окно на весь этот монитор (игра, фильм)
    /// или игра в эксклюзивном полноэкранном режиме. Развёрнутое окно на другом мониторе — не повод.
    /// </summary>
    public static bool CoversMonitor(GigaRest.Core.Desktop.MonitorInfo monitor)
    {
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out var pid);
        if (hwnd == 0 || pid == Environment.ProcessId) return false;
        if (SHQueryUserNotificationState(out var state) == 0 && state == QUERY_USER_NOTIFICATION_STATE.RunningD3DFullScreen) return true;
        if (Native.IsIconic(hwnd) || !Native.IsWindowVisible(hwnd)) return false;
        using var dpi = new Native.DpiScope();
        if (!Native.GetWindowRect(hwnd, out var r)) return false;
        var b = monitor.Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out int pid);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state);
}
