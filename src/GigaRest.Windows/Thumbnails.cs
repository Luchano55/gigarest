using System.Runtime.InteropServices;

namespace GigaRest.Windows;

/// <summary>
/// Живая копия чужого окна внутри нашего (DWM thumbnail, решение D-THUMBS). Рисует сама Windows на видеокарте.
/// Миниатюра — поверх содержимого нашего окна, координаты — в пикселях клиентской области.
/// </summary>
public sealed class Thumbnail : IDisposable
{
    private nint _id;

    public Thumbnail(nint destination, nint source)
    {
        if (DwmRegisterThumbnail(destination, source, out _id) != 0) _id = 0;
    }

    public bool IsValid => _id != 0;

    /// <summary>Поставить копию в прямоугольник, сохранив пропорции окна. null — спрятать.</summary>
    public void Place(int left, int top, int width, int height)
    {
        if (_id == 0) return;
        var w = width;
        var h = height;
        if (DwmQueryThumbnailSourceSize(_id, out var size) == 0 && size.cx > 0 && size.cy > 0)
        {
            var scale = Math.Min((double)width / size.cx, (double)height / size.cy);
            w = (int)(size.cx * scale);
            h = (int)(size.cy * scale);
        }
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new Native.RECT { Left = left + (width - w) / 2, Top = top + (height - h) / 2, Right = left + (width - w) / 2 + w, Bottom = top + (height - h) / 2 + h },
            opacity = 255,
            fVisible = true,
            fSourceClientAreaOnly = true,
        };
        DwmUpdateThumbnailProperties(_id, ref props);
    }

    public void Hide()
    {
        if (_id == 0) return;
        var props = new DWM_THUMBNAIL_PROPERTIES { dwFlags = DWM_TNP_VISIBLE, fVisible = false };
        DwmUpdateThumbnailProperties(_id, ref props);
    }

    public void Dispose()
    {
        if (_id != 0) DwmUnregisterThumbnail(_id);
        _id = 0;
    }

    private const int DWM_TNP_RECTDESTINATION = 0x1, DWM_TNP_OPACITY = 0x4, DWM_TNP_VISIBLE = 0x8, DWM_TNP_SOURCECLIENTAREAONLY = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_THUMBNAIL_PROPERTIES
    {
        public int dwFlags;
        public Native.RECT rcDestination;
        public Native.RECT rcSource;
        public byte opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint dest, nint src, out nint id);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint id);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint id, ref DWM_THUMBNAIL_PROPERTIES props);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(nint id, out SIZE size);
}

/// <summary>Мелочи для окон отдыха: поднять окно без активации, сделать «прозрачным» для Chromium.</summary>
public static class WindowTools
{
    /// <summary>
    /// Окно с плитки свернули: развернуть его обратно на втором мониторе и убрать в самый низ —
    /// под экран плиток. Иначе Windows выводит развёрнутое окно наверх, и оно снова закрывает плитки.
    /// </summary>
    public static void ParkUnderTiles(long hwnd, nint tilesWindow)
    {
        var h = (nint)hwnd;
        var wp = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        if (Native.GetWindowPlacement(h, ref wp))
        {
            wp.showCmd = 3; // SW_SHOWMAXIMIZED
            wp.flags = 0;
            Native.SetWindowPlacement(h, ref wp);
        }
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        Native.SetWindowPos(h, 1 /* HWND_BOTTOM */, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        RaiseAboveAll(tilesWindow);
    }

    /// <summary>
    /// Поднять окно над всеми без прав «переднего плана»: на миг «поверх всех», потом обратно в обычные —
    /// оно остаётся наверху. Обычный SetForegroundWindow из фона Windows часто не выполняет.
    /// </summary>
    public static void RaiseAboveAll(nint hwnd)
    {
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        Native.SetWindowPos(hwnd, -1 /* HWND_TOPMOST */, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Native.SetWindowPos(hwnd, -2 /* HWND_NOTOPMOST */, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Native.SetForegroundWindow(hwnd);
    }

    public static void Activate(long hwnd)
    {
        if (Native.IsIconic((nint)hwnd)) Native.ShowWindow((nint)hwnd, 9);
        Native.SetForegroundWindow((nint)hwnd);
    }

    public static bool IsAlive(long hwnd) => Native.IsWindow((nint)hwnd);

    public static long Foreground() => GetForegroundWindow();

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

    /// <summary>Нажать «Нет» в диалоге «Сохранить?» (только для самопроверки на тестовых окнах).</summary>
    public static void AnswerNo(long dialog) => Native.PostMessageW((nint)dialog, 0x0111 /* WM_COMMAND */, 7 /* IDNO */, 0);

    /// <summary>
    /// Chrome, Electron (Claude) и Firefox (Zen) перестают рисовать окно, полностью закрытое другим окном (D-THUMBS).
    /// Служебные окна (WS_EX_TOOLWINDOW) и полупрозрачные они закрывающими не считают — делаем экран плиток таким,
    /// тогда живые копии не замирают. Заодно экран плиток не появляется на панели задач и в Alt+Tab.
    /// </summary>
    public static void MakeNonOccluding(nint hwnd)
    {
        var ex = GetWindowLongPtrW(hwnd, -20);
        SetWindowLongPtrW(hwnd, -20, (ex | 0x80 /* WS_EX_TOOLWINDOW */ | 0x80000 /* WS_EX_LAYERED */) & ~(nint)0x40000 /* WS_EX_APPWINDOW */);
        SetLayeredWindowAttributes(hwnd, 0, 254, 0x2); // LWA_ALPHA
    }

    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
}
