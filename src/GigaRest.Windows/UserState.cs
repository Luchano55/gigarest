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

    /// <summary>true — лучше не беспокоить (игра, фильм на весь экран, презентация, ПК заблокирован).</summary>
    public static bool ShouldNotDisturb()
    {
        if (SHQueryUserNotificationState(out var state) != 0) return false;
        return state is QUERY_USER_NOTIFICATION_STATE.NotPresent or QUERY_USER_NOTIFICATION_STATE.Busy
            or QUERY_USER_NOTIFICATION_STATE.RunningD3DFullScreen or QUERY_USER_NOTIFICATION_STATE.PresentationMode;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE state);
}
