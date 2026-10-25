using System.Runtime.InteropServices;

namespace GigaRest.Windows;

/// <summary>
/// Слежение за чужими окнами: свернули или закрыли (SetWinEventHook, вне процесса).
/// Обработчик быстрый — только пересылает событие; ставится в UI-потоке, снимается в Dispose.
/// </summary>
public sealed class WindowEvents : IDisposable
{
    private const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

    private readonly WinEventProc _proc; // держим делегат, иначе его соберёт GC
    private readonly nint _minimizeHook, _destroyHook;
    private readonly HashSet<long> _watched = [];

    public event Action<long>? Minimized;
    public event Action<long>? Destroyed;

    public WindowEvents()
    {
        _proc = OnEvent;
        _minimizeHook = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART, 0, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _destroyHook = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY, 0, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public void Watch(long hwnd) => _watched.Add(hwnd);
    public void Clear() => _watched.Clear();
    public bool IsWatched(long hwnd) => _watched.Contains(hwnd);

    private void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || !_watched.Contains(hwnd)) return; // только сами окна, только наши плитки
        if (ev == EVENT_SYSTEM_MINIMIZESTART) Minimized?.Invoke(hwnd);
        else if (ev == EVENT_OBJECT_DESTROY) Destroyed?.Invoke(hwnd);
    }

    public void Dispose()
    {
        if (_minimizeHook != 0) UnhookWinEvent(_minimizeHook);
        if (_destroyHook != 0) UnhookWinEvent(_destroyHook);
        _watched.Clear();
    }

    private delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
