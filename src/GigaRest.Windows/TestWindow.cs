using System.Runtime.InteropServices;

namespace GigaRest.Windows;

/// <summary>
/// Тестовое окно для пробного режима (GigaRest.exe --test-window): обычное окно Windows,
/// которое на крестик ведёт себя как настоящая программа — закрывается, спрашивает «Сохранить?»
/// или прячется в трей. Без WinUI и без .NET-окон: работает на любом ПК вместе с GigaRest.
/// </summary>
public static class TestWindow
{
    public enum Mode { Normal, Ask, Tray }

    private const int WM_CLOSE = 0x10, WM_DESTROY = 0x2, WM_PAINT = 0xF;
    private const int WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_VISIBLE = 0x10000000;
    private const int MB_YESNOCANCEL = 0x3, MB_ICONQUESTION = 0x20, IDCANCEL = 2;
    private const int SW_HIDE = 0;

    private static Mode _mode;
    private static string _text = "";
    private static WndProc? _proc;

    public static int Run(string[] args)
    {
        _mode = Arg(args, "--mode") switch { "ask" => Mode.Ask, "tray" => Mode.Tray, _ => Mode.Normal };
        var title = Arg(args, "--title") ?? "Тестовое окно GigaRest";
        _text = _mode switch
        {
            Mode.Ask => "Это тестовое окно. На крестик оно спросит «Сохранить?», как Word или Блокнот.",
            Mode.Tray => "Это тестовое окно. На крестик оно спрячется в трей, как Telegram или Битрикс24.",
            _ => "Это тестовое окно. На крестик оно просто закроется.",
        };
        if (args.Contains("--tile")) _text = "Это тестовое окно. При переходе в отдых оно переедет на второй монитор.";

        _proc = Proc;
        var cls = "GigaRest.TestWindow";
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = cls,
            hbrBackground = 6, // COLOR_WINDOW + 1
            hCursor = LoadCursorW(0, 32512),
        };
        RegisterClassExW(ref wc);
        var x = int.TryParse(Arg(args, "--x"), out var px) ? px : 200;
        var y = int.TryParse(Arg(args, "--y"), out var py) ? py : 200;
        var hwnd = CreateWindowExW(0, cls, title, WS_OVERLAPPEDWINDOW | WS_VISIBLE, x, y, 520, 220, 0, 0, wc.hInstance, 0);
        if (hwnd == 0) return 1;

        while (GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static nint Proc(nint hwnd, int msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_CLOSE:
                if (_mode == Mode.Tray) { ShowWindow(hwnd, SW_HIDE); return 0; }
                if (_mode == Mode.Ask &&
                    MessageBoxW(hwnd, "Сохранить изменения в тестовом документе?", "Тестовое окно", MB_YESNOCANCEL | MB_ICONQUESTION) == IDCANCEL)
                    return 0;
                DestroyWindow(hwnd);
                return 0;
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
            case WM_PAINT:
                var hdc = BeginPaint(hwnd, out var ps);
                GetClientRect(hwnd, out var rc);
                rc.Left += 20; rc.Top += 20; rc.Right -= 20;
                DrawTextW(hdc, _text, -1, ref rc, 0x10 /* DT_WORDBREAK */);
                EndPaint(hwnd, ref ps);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private delegate nint WndProc(nint hwnd, int msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint hwnd; public int message; public nint wParam, lParam; public int time; public Native.POINT pt; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public nint hdc;
        public bool fErase;
        public Native.RECT rcPaint;
        public bool fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(int ex, string cls, string name, int style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG msg, nint hwnd, int min, int max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint hwnd, int msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(nint hwnd, string text, string caption, int type);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Native.RECT rc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(nint hdc, string text, int len, ref Native.RECT rc, int format);
    [DllImport("user32.dll")] private static extern nint LoadCursorW(nint inst, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
}
