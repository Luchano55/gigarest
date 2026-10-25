using System.Runtime.InteropServices;
using GigaRest.Core.Desktop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace GigaRest.App;

/// <summary>Общее для экранов отдыха: на весь монитор, без рамки; сцены из папки программы.</summary>
internal static class RestScreens
{
    public static string ScenesDir => Path.Combine(AppContext.BaseDirectory, "Assets", "Scenes");

    public static Uri SceneUri(string file) => new(Path.Combine(ScenesDir, file));

    /// <summary>Окно — на весь указанный монитор (координаты в настоящих пикселях), без рамки и панели задач.</summary>
    public static void FullScreenOn(Window window, MonitorInfo? monitor)
    {
        if (monitor is not null)
        {
            var b = monitor.Bounds;
            window.AppWindow.MoveAndResize(new RectInt32(b.Left, b.Top, b.Width, b.Height));
        }
        window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    public static nint Hwnd(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    public static void BringToFront(Window window)
    {
        window.AppWindow.Show();
        window.Activate();
        SetForegroundWindow(Hwnd(window));
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
}
