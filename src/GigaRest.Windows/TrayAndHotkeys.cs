using System.Runtime.InteropServices;
using GigaRest.Core.Input;
using GigaRest.Core.Logging;
using GigaRest.Core.Text;

namespace GigaRest.Windows;

public sealed record TrayMenuItem(int Id, string Text, bool Enabled = true, bool Bold = false, bool Checked = false);

/// <summary>
/// Скрытое окно для значка в трее и горячих клавиш (RegisterHotKey). Создаётся в UI-потоке:
/// его сообщения разбирает тот же цикл. Обработчики быстрые — тяжёлое делает подписчик в другом месте.
/// Всё снимается в Dispose.
/// </summary>
public sealed class TrayAndHotkeys : IDisposable
{
    private const int WM_APP_TRAY = 0x8001;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int NIN_BALLOONUSERCLICK = 0x0405;
    private const int WM_NULL = 0;
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    private const int MOD_NOREPEAT = 0x4000;
    private const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    private const uint MF_STRING = 0, MF_GRAYED = 1, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    private readonly WndProc _proc; // держим делегат, иначе его соберёт GC
    private readonly nint _hwnd;
    private readonly string _className = "GigaRest.Tray." + Guid.NewGuid().ToString("N");
    private readonly int _taskbarCreated;
    private nint _icon;
    private string _tip = "GigaRest";
    private bool _iconAdded;
    private readonly HashSet<int> _hotkeys = [];

    public event Action<int>? HotkeyPressed;
    public event Action? TrayClicked;
    public event Action<int>? MenuCommand;
    /// <summary>Щёлкнули по всплывающему уведомлению.</summary>
    public event Action? BalloonClicked;
    /// <summary>Пункты меню по правому щелчку — спрашиваем каждый раз, они зависят от режима.</summary>
    public Func<IReadOnlyList<TrayMenuItem?>>? MenuItems { get; set; }

    public TrayAndHotkeys()
    {
        _proc = Proc;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = _className,
        };
        if (RegisterClassExW(ref wc) == 0)
            throw new InvalidOperationException("RegisterClassEx: " + Marshal.GetLastWin32Error());
        _hwnd = CreateWindowExW(0, _className, "GigaRest", 0, 0, 0, 0, 0, 0, 0, wc.hInstance, 0);
        if (_hwnd == 0)
            throw new InvalidOperationException("CreateWindowEx: " + Marshal.GetLastWin32Error());
        // Проводник перезапустился — значок надо добавить заново.
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    }

    /// <summary>Показать значок. iconPath — .ico-файл.</summary>
    public void ShowIcon(string iconPath, string tip)
    {
        var size = GetSystemMetrics(49 /* SM_CXSMICON */);
        _icon = LoadImageW(0, iconPath, 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
        if (_icon == 0) Log.Warn("Значок трея не загрузился: " + Marshal.GetLastWin32Error());
        _tip = tip;
        AddIcon();
    }

    private void AddIcon()
    {
        var data = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _iconAdded = Shell_NotifyIconW(NIM_ADD, ref data);
        if (!_iconAdded) Log.Warn("Значок в трее не добавился");
    }

    public void SetTip(string tip)
    {
        _tip = tip;
        if (!_iconAdded) return;
        var data = Data(NIF_TIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    /// <summary>Всплывающее сообщение у значка (итог перехода, если окно закрыто).</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_iconAdded) return;
        var data = Data(NIF_INFO);
        data.szInfoTitle = Trim(title, 63);
        data.szInfo = Trim(text, 255);
        data.dwInfoFlags = 0x10; // NIIF_NOSOUND
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>Зарегистрировать горячую клавишу. null — успех, иначе понятная причина.</summary>
    public string? RegisterHotkey(int id, Hotkey hotkey)
    {
        if (_hotkeys.Remove(id)) UnregisterHotKey(_hwnd, id);
        if (RegisterHotKey(_hwnd, id, (int)hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKey))
        {
            _hotkeys.Add(id);
            return null;
        }
        var error = Marshal.GetLastWin32Error();
        return error == ERROR_HOTKEY_ALREADY_REGISTERED
            ? L.T($"{hotkey} уже занята другой программой — выберите другую", $"{hotkey} is already used by another program — pick a different one")
            : L.T($"{hotkey} не удалось назначить (код {error})", $"Couldn't assign {hotkey} (code {error})");
    }

    public void UnregisterHotkey(int id)
    {
        if (_hotkeys.Remove(id)) UnregisterHotKey(_hwnd, id);
    }

    private nint Proc(nint hwnd, int msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == WM_HOTKEY)
            {
                HotkeyPressed?.Invoke((int)wParam);
                return 0;
            }
            if (msg == WM_APP_TRAY)
            {
                var ev = (int)(lParam & 0xFFFF);
                if (ev == WM_LBUTTONUP) TrayClicked?.Invoke();
                else if (ev is WM_RBUTTONUP or WM_CONTEXTMENU) ShowMenu();
                else if (ev == NIN_BALLOONUSERCLICK) BalloonClicked?.Invoke();
                return 0;
            }
            if (msg == _taskbarCreated && _taskbarCreated != 0)
            {
                AddIcon();
                return 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка в обработчике трея", ex);
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var items = MenuItems?.Invoke();
        if (items is null || items.Count == 0) return;
        var menu = CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item is null) AppendMenuW(menu, MF_SEPARATOR, 0, null);
                else AppendMenuW(menu, (item.Enabled ? MF_STRING : MF_GRAYED) | (item.Checked ? MF_CHECKED : 0), (nuint)item.Id, item.Text);
            }
            var bold = items.FirstOrDefault(i => i?.Bold == true);
            if (bold is not null) SetMenuDefaultItem(menu, (uint)bold.Id, 0);
            GetCursorPos(out var pt);
            // Без этого меню не закрывается щелчком мимо (известная особенность трея).
            SetForegroundWindow(_hwnd);
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, _hwnd, 0);
            PostMessageW(_hwnd, WM_NULL, 0, 0);
            if (cmd != 0) MenuCommand?.Invoke(cmd);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private NOTIFYICONDATA Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
        szTip = Trim(_tip, 127),
        szInfo = "",
        szInfoTitle = "",
    };

    public void Dispose()
    {
        foreach (var id in _hotkeys) UnregisterHotKey(_hwnd, id);
        _hotkeys.Clear();
        if (_iconAdded)
        {
            var data = Data(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _iconAdded = false;
        }
        if (_icon != 0) { DestroyIcon(_icon); _icon = 0; }
        if (_hwnd != 0) DestroyWindow(_hwnd);
        UnregisterClassW(_className, GetModuleHandleW(null));
    }

    // ---------- Win32 ----------

    private delegate nint WndProc(nint hwnd, int msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClassW(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(int exStyle, string cls, string name, int style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProcW(nint hwnd, int msg, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessageW(string name);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, int modifiers, int vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern bool SetMenuDefaultItem(nint menu, uint item, uint byPos);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Native.POINT pt);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint hwnd, int msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImageW(nint instance, string name, int type, int cx, int cy, int flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(int msg, ref NOTIFYICONDATA data);
}
