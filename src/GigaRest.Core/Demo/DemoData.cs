using GigaRest.Core.Desktop;

namespace GigaRest.Core.Demo;

/// <summary>
/// Правдоподобный набор программ для скриншотов в CI (там нет Битрикс24 и Outlook).
/// </summary>
public static class DemoData
{
    private static readonly (string Exe, string Title, long Mb, int Windows)[] Programs =
    [
        ("Bitrix24.exe", "Битрикс24", 820, 1),
        ("OUTLOOK.EXE", "Входящие — Outlook", 640, 1),
        ("chrome.exe", "Задачи — Google Chrome", 3100, 2),
        ("YandexDisk2.exe", "", 160, 0),
        ("Telegram.exe", "Telegram", 380, 1),
        ("zen.exe", "YouTube — Zen", 900, 1),
        ("claude.exe", "Claude", 450, 1),
        ("Яндекс Музыка.exe", "Яндекс Музыка", 300, 1),
        ("steam.exe", "Steam", 310, 1),
    ];

    public static (List<ProcessInfo> Processes, List<WindowInfo> Windows, List<MonitorInfo> Monitors) Create()
    {
        var processes = new List<ProcessInfo>();
        var windows = new List<WindowInfo>();
        var pid = 1000;
        long handle = 0x10000;
        foreach (var (exe, title, mb, count) in Programs)
        {
            pid += 10;
            processes.Add(new ProcessInfo
            {
                Pid = pid, ExeName = exe, ExePath = $@"C:\Program Files\{exe[..^4]}\{exe}",
                StartTimeUtc = new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc), PrivateBytes = mb << 20,
            });
            for (var i = 0; i < count; i++)
                windows.Add(new WindowInfo { Handle = handle++, Pid = pid, Title = title, IsVisible = true });
        }
        var monitors = new List<MonitorInfo>
        {
            new() { Id = @"\\.\DISPLAY1", IsPrimary = true, Bounds = new Rect(0, 0, 3840, 2160), WorkArea = new Rect(0, 0, 3840, 2100), ScalePercent = 150 },
            new() { Id = @"\\.\DISPLAY2", Bounds = new Rect(3840, 0, 6400, 1440), WorkArea = new Rect(3840, 0, 6400, 1400), ScalePercent = 100 },
        };
        return (processes, windows, monitors);
    }
}
