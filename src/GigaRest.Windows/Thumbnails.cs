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
    /// <summary>Развернуть окно на его мониторе, не забирая фокус (после «Свернуть» — обратно под плитки).</summary>
    public static void RestoreMaximizedNoActivate(long hwnd)
    {
        var wp = new Native.WINDOWPLACEMENT { length = Marshal.SizeOf<Native.WINDOWPLACEMENT>() };
        if (!Native.GetWindowPlacement((nint)hwnd, ref wp)) return;
        wp.showCmd = 3; // SW_SHOWMAXIMIZED
        wp.flags = 0;
        Native.SetWindowPlacement((nint)hwnd, ref wp);
    }

    public static void Activate(long hwnd)
    {
        if (Native.IsIconic((nint)hwnd)) Native.ShowWindow((nint)hwnd, 9);
        Native.SetForegroundWindow((nint)hwnd);
    }

    public static bool IsAlive(long hwnd) => Native.IsWindow((nint)hwnd);

    /// <summary>Нажать «Нет» в диалоге «Сохранить?» (только для самопроверки на тестовых окнах).</summary>
    public static void AnswerNo(long dialog) => Native.PostMessageW((nint)dialog, 0x0111 /* WM_COMMAND */, 7 /* IDNO */, 0);

    /// <summary>
    /// Chrome, Electron и Firefox перестают рисовать окно, полностью закрытое непрозрачным окном (D-THUMBS).
    /// Окно с альфой меньше 255 они непрозрачным не считают — тогда живые копии не замирают (?).
    /// </summary>
    public static void MakeAlmostOpaque(nint hwnd)
    {
        var ex = GetWindowLongPtrW(hwnd, -20);
        SetWindowLongPtrW(hwnd, -20, ex | 0x80000); // WS_EX_LAYERED
        SetLayeredWindowAttributes(hwnd, 0, 254, 0x2); // LWA_ALPHA
    }

    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
}
